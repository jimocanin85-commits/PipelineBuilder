using System.Text;
using System.Text.RegularExpressions;
using PipelineBuilder.Core.Abstractions;
using PipelineBuilder.Core.Generators;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Yaml;

namespace PipelineBuilder.Core.Services;

/// <summary>
/// Assembles the pipeline document (header, trigger, variables, pool, stages) from the
/// fragments produced by the stage generators. Each section starts with one comment line that
/// says what it does, so the file can be read without knowing Azure Pipelines.
/// </summary>
public sealed class PipelineYamlAssembler
{
    private readonly StringBuilder _yaml = new();

    /// <summary>
    /// Adds the YAML header comment and metadata.
    /// </summary>
    public PipelineYamlAssembler AddHeader(string pipelineName)
    {
        _yaml.AppendLine("# Made with PipelineBuilder. To change it, open your saved settings there, or edit this file.");
        _yaml.AppendLine($"# Pipeline: {pipelineName}");
        _yaml.AppendLine();
        return this;
    }

    /// <summary>
    /// Adds the trigger section from a <see cref="TriggerConfig"/>, including branch excludes and path filters.
    /// </summary>
    public PipelineYamlAssembler AddTrigger(TriggerConfig trigger)
    {
        var anyBranch = trigger.TriggerAll || trigger.IncludeBranches.Count == 0;
        var include = anyBranch
            ? new[] { "'*'" }
            : trigger.IncludeBranches.Select(YamlBuilder.YamlString).ToArray();

        _yaml.AppendLine(anyBranch ? "# Runs when any branch changes." : "# Runs when one of these branches changes.");
        _yaml.AppendLine("trigger:");
        _yaml.AppendLine("  branches:");
        _yaml.AppendLine("    include:");
        foreach (var branch in include)
            _yaml.AppendLine($"      - {branch}");
        if (trigger.ExcludeBranches.Count > 0)
        {
            _yaml.AppendLine("    exclude:");
            foreach (var branch in trigger.ExcludeBranches)
                _yaml.AppendLine($"      - {YamlBuilder.YamlString(branch)}");
        }

        var includePaths = trigger.PathFilters.Where(p => !p.StartsWith('!')).ToList();
        var excludePaths = trigger.PathFilters.Where(p => p.StartsWith('!')).Select(p => p[1..]).ToList();
        if (includePaths.Count > 0 || excludePaths.Count > 0)
        {
            _yaml.AppendLine("  paths:");
            if (includePaths.Count > 0)
            {
                _yaml.AppendLine("    include:");
                foreach (var path in includePaths)
                    _yaml.AppendLine($"      - {YamlBuilder.YamlString(path)}");
            }
            if (excludePaths.Count > 0)
            {
                _yaml.AppendLine("    exclude:");
                foreach (var path in excludePaths)
                    _yaml.AppendLine($"      - {YamlBuilder.YamlString(path)}");
            }
        }
        _yaml.AppendLine();
        return this;
    }

    /// <summary>
    /// Adds the list of environments as a parameter. The deploy stage is written once and repeated
    /// for each of them, so adding an environment means adding a line here.
    /// </summary>
    public PipelineYamlAssembler AddEnvironments(IReadOnlyList<string> environments)
    {
        _yaml.AppendLine("# The environments to deploy to, in order. Each must exist under Pipelines > Environments.");
        _yaml.AppendLine("parameters:");
        _yaml.AppendLine("- name: environments");
        _yaml.AppendLine("  displayName: 'Environments, in deployment order'");
        _yaml.AppendLine("  type: object");
        _yaml.AppendLine("  default:");
        foreach (var environment in environments)
            _yaml.AppendLine($"  - {YamlBuilder.YamlString(environment)}");
        _yaml.AppendLine();
        return this;
    }

    /// <summary>
    /// Adds the variables section if variables are provided.
    /// </summary>
    public PipelineYamlAssembler AddVariables(string? variables)
    {
        if (!string.IsNullOrWhiteSpace(variables))
        {
            _yaml.AppendLine("# Values used by the steps below.");
            _yaml.AppendLine(variables);
            _yaml.AppendLine();
        }
        return this;
    }

    /// <summary>
    /// Sets the default agent pool for every job that does not choose its own. Deployments to the
    /// user's own servers run on those servers instead, which the comment says.
    /// </summary>
    public PipelineYamlAssembler AddPool(string poolConfiguration, bool deploysOnServers = false)
    {
        _yaml.AppendLine(deploysOnServers
            ? "# Builds run on this agent. Deployments run on the servers registered in each environment."
            : "# Builds and deployments run on this agent.");
        _yaml.AppendLine("pool:");
        _yaml.AppendLine($"  {poolConfiguration}");
        _yaml.AppendLine();
        return this;
    }

    /// <summary>
    /// Marks the beginning of the stages section.
    /// </summary>
    public PipelineYamlAssembler StartStages()
    {
        _yaml.AppendLine("stages:");
        return this;
    }

    /// <summary>
    /// Adds any stage with custom content.
    /// </summary>
    public PipelineYamlAssembler AddStage(string? stageYaml)
    {
        if (!string.IsNullOrWhiteSpace(stageYaml))
        {
            _yaml.AppendLine(stageYaml);
        }
        return this;
    }

    /// <summary>
    /// Adds notification stages. Success notifications run only when the last deployment
    /// succeeded; failure notifications run when any earlier stage failed. The outcome has to be
    /// decided at stage level: a step-level <c>failed()</c> would only look at the notify job itself.
    /// </summary>
    public PipelineYamlAssembler AddNotificationStages(
        IReadOnlyList<NotificationConfig> notifications,
        NotificationStepGenerator notificationGenerator,
        PipelineDefinition definition)
    {
        if (notifications.Count == 0)
            return this;

        var successSteps = notificationGenerator.GenerateSteps(definition, succeeded: true);
        var failureSteps = notificationGenerator.GenerateSteps(definition, succeeded: false);
        if (successSteps.Count + failureSteps.Count > 0)
            _yaml.AppendLine("# Notify: tell the team how it went.");

        // Without dependsOn a stage follows the one before it, which is the last deployment.
        if (successSteps.Count > 0)
            AppendNotifyStage("Notify_Success", "Notify on success", dependsOn: null, $"and(succeeded(), {DeploymentStageGenerator.NotPullRequest})", successSteps);

        // Depend directly on every stage so failed() is true whichever one failed.
        if (failureSteps.Count > 0)
            AppendNotifyStage("Notify_Failure", "Notify on failure", DeploymentStageGenerator.AllStagesDependsOn, $"and(failed(), {DeploymentStageGenerator.NotPullRequest})", failureSteps);

        return this;
    }

    private void AppendNotifyStage(string name, string displayName, string? dependsOn, string condition, IReadOnlyList<string> steps)
    {
        _yaml.AppendLine($"- stage: {name}");
        _yaml.AppendLine($"  displayName: {YamlBuilder.YamlString(displayName)}");
        if (dependsOn != null)
            _yaml.AppendLine(dependsOn);
        _yaml.AppendLine($"  condition: {condition}");
        _yaml.AppendLine("  jobs:");
        _yaml.AppendLine("  - job: Notify");
        _yaml.AppendLine("    steps:");
        _yaml.AppendLine(YamlBuilder.Indent(string.Join("\n", steps), 2));
    }

    /// <summary>
    /// Gets the assembled YAML document.
    /// </summary>
    public string Build() => _yaml.ToString().TrimEnd();
}
