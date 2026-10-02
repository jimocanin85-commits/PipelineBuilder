using System.Text.RegularExpressions;
using PipelineBuilder.Core.Abstractions;
using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Yaml;

namespace PipelineBuilder.Core.Validation;

/// <summary>
/// Rules that judge the finished pipeline: governance policy, deployment advice, variable groups,
/// plain-text secrets and Key Vault. Their findings are shown after generation and do not block it.
/// </summary>
internal sealed class PolicyRules
{
    private readonly IDeploymentKinds _deploymentKinds;
    private readonly IVariableGroupService _variableGroups;
    private readonly ISecretsGovernanceService _secrets;
    private readonly IKeyVaultYamlService _keyVault;

    private PolicyRules(IDeploymentKinds deploymentKinds, IVariableGroupService variableGroups, ISecretsGovernanceService secrets, IKeyVaultYamlService keyVault)
    {
        _deploymentKinds = deploymentKinds;
        _variableGroups = variableGroups;
        _secrets = secrets;
        _keyVault = keyVault;
    }

    public static IEnumerable<ValidationRule> All(
        IDeploymentKinds deploymentKinds, IVariableGroupService variableGroups, ISecretsGovernanceService secrets, IKeyVaultYamlService keyVault)
    {
        var rules = new PolicyRules(deploymentKinds, variableGroups, secrets, keyVault);
        return new[]
        {
            Rule("governance.approvals", Approvals),
            Rule("deployment.kind", rules.KindSpecific),
            Rule("deployment.servers-on-cloud", rules.ServersOnCloud),
            Rule("deployment.server-resources", rules.ServerResources),
            Rule("deployment.rolling-without-servers", rules.RollingWithoutServers),
            Rule("deployment.placeholder-strategy", PlaceholderStrategy),
            Rule("notifications.email-recipients", EmailRecipients),
            Rule("deployment.slot-swap", rules.SlotSwap),
            Rule("governance.health-check", HealthCheck),
            Rule("governance.rollback", Rollback),
            Rule("governance.naming-convention", NamingConvention),
            Rule("governance.forbidden-tasks", ForbiddenTasks),
            Rule("governance.required-tasks", RequiredTasks),
            Rule("variables.groups", rules.VariableGroups),
            Rule("secrets.plain-text", rules.PlainTextSecrets),
            Rule("keyvault.configuration", rules.KeyVault),
        };
    }

    private static ValidationRule Rule(string id, Func<ValidationContext, IEnumerable<ValidationResult>> check) =>
        new(id, ValidationStage.Generated, check);

    private static ValidationResult Finding(ValidationSeverity severity, string message, string field, string fix) => new()
    {
        Severity = severity,
        Message = message,
        AffectedField = field,
        SuggestedFix = fix
    };

    private static IEnumerable<ValidationResult> Approvals(ValidationContext context)
    {
        if (!context.Definition.Governance.RequiredApprovals)
            yield break;

        // Approvals live on the Azure DevOps environment, so the YAML cannot prove they exist.
        // Remind the user for every environment that should be gated.
        foreach (var env in context.Definition.Environments.Where(e => e.Contains("prod", StringComparison.OrdinalIgnoreCase)))
        {
            yield return Finding(ValidationSeverity.Info,
                $"Add an approval check to the '{env}' environment in Azure DevOps (Pipelines > Environments > {env} > Approvals and checks).",
                "Environments",
                "Approvals are configured on the environment, not in YAML.");
        }
    }

    /// <summary>Checks that belong to the selected deployment kind (see its handler).</summary>
    private IEnumerable<ValidationResult> KindSpecific(ValidationContext context) =>
        _deploymentKinds.For(context.Definition.Deployment.Kind).Validate(context.Definition);

    private IEnumerable<ValidationResult> ServersOnCloud(ValidationContext context)
    {
        var definition = context.Definition;
        if (_deploymentKinds.For(definition.Deployment.Kind).RunsOnServers && definition.DeploymentTarget == DeploymentTarget.Cloud)
        {
            yield return Finding(ValidationSeverity.Warning,
                $"{definition.Deployment.Kind} deployments need your own servers, but the deployment target is Cloud, so they would run on a hosted build agent.",
                nameof(PipelineDefinition.DeploymentTarget),
                "Set the deployment target to OnPrem or Hybrid and register the servers in each Azure DevOps environment.");
        }
    }

    private IEnumerable<ValidationResult> ServerResources(ValidationContext context)
    {
        if (_deploymentKinds.UsesServerResources(context.Definition))
        {
            yield return Finding(ValidationSeverity.Info,
                "Deployments run on the servers registered in each Azure DevOps environment (Virtual machine resources).",
                nameof(PipelineDefinition.Environments),
                "Register the target servers under Pipelines > Environments > <environment> > Add resource > Virtual machines.");
        }
    }

    private IEnumerable<ValidationResult> RollingWithoutServers(ValidationContext context)
    {
        var definition = context.Definition;
        if (definition.DeploymentStrategy.StrategyType == DeploymentStrategyType.Rolling && !_deploymentKinds.UsesServerResources(definition))
        {
            yield return Finding(ValidationSeverity.Warning,
                "Rolling deployments need servers registered in the environment; this pipeline deploys everything at once.",
                nameof(PipelineDefinition.DeploymentStrategy),
                "Use an on-premises deployment target, or choose the Standard strategy.");
        }
    }

    private static IEnumerable<ValidationResult> PlaceholderStrategy(ValidationContext context)
    {
        var strategy = context.Definition.DeploymentStrategy.StrategyType;
        if (strategy is DeploymentStrategyType.Canary or DeploymentStrategyType.BlueGreen)
        {
            yield return Finding(ValidationSeverity.Warning,
                $"{strategy} traffic routing depends on your load balancer and is generated as a placeholder step.",
                nameof(PipelineDefinition.DeploymentStrategy),
                "Replace the placeholder step with your traffic-switch commands.");
        }
    }

    private static IEnumerable<ValidationResult> EmailRecipients(ValidationContext context) =>
        context.Definition.Notifications
            .Where(n => n.NotificationType == NotificationType.Email && !n.EmailRecipients.Any(r => !string.IsNullOrWhiteSpace(r)))
            .Select(_ => Finding(ValidationSeverity.Warning,
                "An email notification has no recipients, so it will be skipped.",
                nameof(PipelineDefinition.Notifications),
                "Add at least one recipient on the Notifications step."));

    private IEnumerable<ValidationResult> SlotSwap(ValidationContext context)
    {
        var definition = context.Definition;
        if (definition.DeploymentStrategy.StrategyType == DeploymentStrategyType.SlotSwap
            && !_deploymentKinds.For(definition.Deployment.Kind).SupportsSlotSwap)
        {
            yield return Finding(ValidationSeverity.Error,
                "The slot-swap strategy only works with Azure App Service deployments.",
                nameof(PipelineDefinition.DeploymentStrategy),
                "Set the deployment kind to Azure App Service, or choose another strategy.");
        }
    }

    private static IEnumerable<ValidationResult> HealthCheck(ValidationContext context)
    {
        var definition = context.Definition;
        if (definition.Governance.RequireHealthCheck
            && definition.Environments.Any(EnvironmentNames.IsProduction)
            && !definition.HealthChecks.Any(h => h.Enabled))
        {
            yield return Finding(ValidationSeverity.Error,
                "Production deployments require at least one enabled health check.",
                nameof(PipelineDefinition.HealthChecks),
                "Enable an HTTP, IIS, or service health check before prod deployment.");
        }
    }

    private static IEnumerable<ValidationResult> Rollback(ValidationContext context)
    {
        var definition = context.Definition;
        if (definition.Governance.RequireRollback && definition.DeploymentTarget == DeploymentTarget.OnPrem && !definition.Rollback.Enabled)
        {
            yield return Finding(ValidationSeverity.Error,
                "On-premises deployments require rollback configuration.",
                nameof(PipelineDefinition.Rollback),
                "Enable rollback and specify a backup path or rollback script.");
        }
    }

    private static IEnumerable<ValidationResult> NamingConvention(ValidationContext context)
    {
        var definition = context.Definition;
        var pattern = definition.Governance.NamingConvention;
        if (string.IsNullOrWhiteSpace(pattern))
            return Array.Empty<ValidationResult>();

        try
        {
            if (Regex.IsMatch(definition.Name, pattern))
                return Array.Empty<ValidationResult>();

            return new[]
            {
                Finding(ValidationSeverity.Warning,
                    $"Pipeline name '{definition.Name}' does not match naming convention.",
                    nameof(PipelineDefinition.Name),
                    $"Rename pipeline to match pattern: {pattern}")
            };
        }
        catch (RegexParseException ex)
        {
            return new[]
            {
                Finding(ValidationSeverity.Error,
                    $"Governance naming convention regex is invalid: {ex.Message}",
                    nameof(GovernancePolicyConfig.NamingConvention),
                    "Provide a valid regular expression for pipeline naming.")
            };
        }
    }

    private static IEnumerable<ValidationResult> ForbiddenTasks(ValidationContext context)
    {
        foreach (var forbidden in context.Definition.Governance.ForbiddenTasks.Where(f => !string.IsNullOrWhiteSpace(f)))
        {
            var uses = context.Tasks.Count(t => PipelineTaskInventory.Matches(t, forbidden));
            if (uses > 0)
            {
                yield return Finding(ValidationSeverity.Error,
                    $"Forbidden task '{forbidden}' is used by {uses} step(s) in the generated pipeline.",
                    "Yaml",
                    $"Remove or replace task '{forbidden}' per governance policy.");
            }
        }
    }

    private static IEnumerable<ValidationResult> RequiredTasks(ValidationContext context)
    {
        foreach (var required in context.Definition.Governance.RequiredTasks.Where(r => !string.IsNullOrWhiteSpace(r)))
        {
            if (!context.Tasks.Any(t => PipelineTaskInventory.Matches(t, required)))
            {
                yield return Finding(ValidationSeverity.Warning,
                    $"Required task '{required}' is missing from generated YAML.",
                    "Yaml",
                    $"Add task '{required}' to the appropriate stage.");
            }
        }
    }

    private IEnumerable<ValidationResult> VariableGroups(ValidationContext context) =>
        _variableGroups.Validate(context.Definition.VariableGroups, context.Definition.Governance);

    private IEnumerable<ValidationResult> PlainTextSecrets(ValidationContext context) =>
        _secrets.ToValidationResults(_secrets.ScanYaml(context.Yaml));

    private IEnumerable<ValidationResult> KeyVault(ValidationContext context) =>
        _keyVault.Validate(context.Definition.KeyVault);
}
