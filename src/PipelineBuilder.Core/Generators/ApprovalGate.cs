using System.Text;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Yaml;

namespace PipelineBuilder.Core.Generators;

/// <summary>
/// An approval written in the pipeline file: before deploying to a chosen environment, the stage
/// waits until a person presses Resume. It is a job without an agent that runs Azure's manual
/// validation task, and the deploy job depends on it.
/// </summary>
/// <remarks>
/// This is not the approval set on an Azure DevOps environment (Approvals and checks). That one
/// lives outside the file and cannot be removed by editing it; this one travels with the file.
/// </remarks>
internal static class ApprovalGate
{
    public const string JobName = "Approve";

    /// <summary>The environments that wait for approval, in deployment order.</summary>
    public static IReadOnlyList<string> Environments(PipelineDefinition definition) =>
        definition.Environments
            .Where(e => definition.Approval.Environments.Contains(e, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>The approval job, only present in the stages of the chosen environments; empty when there are none.</summary>
    public static string Job(PipelineDefinition definition, string environment)
    {
        var environments = Environments(definition);
        if (environments.Count == 0)
            return string.Empty;

        var approval = definition.Approval;
        var wait = approval.WaitMinutes;
        var approvers = approval.Approvers?.Trim();

        var sb = new StringBuilder();
        sb.Append($"  # A person must approve before this stage deploys to {string.Join(" or ", environments)}.\n");
        sb.Append("  - ").Append(Condition(environments)).Append('\n');
        sb.Append($"    - job: {JobName}\n");
        sb.Append("      displayName: 'Wait for approval'\n");
        sb.Append("      pool: server\n");
        sb.Append($"      timeoutInMinutes: {wait + 5}\n"); // the job must outlast the task
        sb.Append("      steps:\n");
        // Naming approvers needs version 1 of the task; version 0 also exists on older Azure DevOps Server.
        sb.Append($"      - task: ManualValidation@{(string.IsNullOrEmpty(approvers) ? 0 : 1)}\n");
        sb.Append($"        displayName: {YamlBuilder.YamlString($"Approve the deployment to {environment}")}\n");
        sb.Append($"        timeoutInMinutes: {wait}\n");
        sb.Append("        inputs:\n");
        sb.Append($"          notifyUsers: {YamlBuilder.YamlString(approval.NotifyUsers?.Trim())}\n");
        if (!string.IsNullOrEmpty(approvers))
            sb.Append($"          approvers: {YamlBuilder.YamlString(approvers)}\n");
        sb.Append($"          instructions: {YamlBuilder.YamlString($"Resume to deploy build $(Build.BuildNumber) to {environment}. Reject to stop here.")}\n");
        sb.Append("          onTimeout: 'reject'\n");
        return sb.ToString();
    }

    /// <summary>Lines for the deploy job that make it wait for the approval job; empty when there are none.</summary>
    public static string DependsOn(PipelineDefinition definition)
    {
        var environments = Environments(definition);
        return environments.Count == 0
            ? string.Empty
            : $"    {Condition(environments)}\n      dependsOn: {JobName}\n";
    }

    private static string Condition(IReadOnlyList<string> environments) =>
        "${{ if in(environment, " + string.Join(", ", environments.Select(YamlBuilder.YamlString)) + ") }}:";
}
