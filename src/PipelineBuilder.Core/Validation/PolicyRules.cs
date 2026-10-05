using PipelineBuilder.Core.Abstractions;
using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;

namespace PipelineBuilder.Core.Validation;

/// <summary>
/// Rules that judge the finished pipeline: what to set up in Azure DevOps, deployment advice, variable
/// groups, plain-text secrets and Key Vault. Their findings are shown after generation and do not block it.
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
            Rule("environments.approvals", Approvals),
            Rule("deployment.kind", rules.KindSpecific),
            Rule("deployment.server-resources", rules.ServerResources),
            Rule("deployment.rolling-without-servers", rules.RollingWithoutServers),
            Rule("healthchecks.windows-only", rules.WindowsOnlyHealthChecks),
            Rule("notifications.email-recipients", EmailRecipients),
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

    // Approvals live on the Azure DevOps environment, so the YAML cannot prove they exist.
    private static IEnumerable<ValidationResult> Approvals(ValidationContext context) =>
        context.Definition.Environments
            .Where(EnvironmentNames.IsProduction)
            .Select(env => Finding(ValidationSeverity.Info,
                $"Add an approval check to the '{env}' environment in Azure DevOps (Pipelines > Environments > {env} > Approvals and checks).",
                nameof(PipelineDefinition.Environments),
                "Approvals are configured on the environment, not in YAML."));

    /// <summary>Checks that belong to the selected deployment kind (see its handler).</summary>
    private IEnumerable<ValidationResult> KindSpecific(ValidationContext context) =>
        _deploymentKinds.For(context.Definition.Deployment.Kind).Validate(context.Definition);

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
                "Updating a few servers at a time only applies when the pipeline deploys to your own servers.");
        }
    }

    private IEnumerable<ValidationResult> WindowsOnlyHealthChecks(ValidationContext context)
    {
        var definition = context.Definition;
        if (_deploymentKinds.ShellFor(definition) == ScriptShell.Bash
            && definition.HealthChecks.Any(h => h.Enabled && h.HealthCheckType == HealthCheckType.IisAppPool))
        {
            yield return Finding(ValidationSeverity.Warning,
                "IIS app pool checks only run on Windows servers, so this check is skipped.",
                nameof(PipelineDefinition.HealthChecks),
                "Remove the check, or change it to an HTTP address or Service check.");
        }
    }

    private static IEnumerable<ValidationResult> EmailRecipients(ValidationContext context) =>
        context.Definition.Notifications
            .Where(n => n.NotificationType == NotificationType.Email && !n.EmailRecipients.Any(r => !string.IsNullOrWhiteSpace(r)))
            .Select(_ => Finding(ValidationSeverity.Warning,
                "An email notification has no recipients, so it will be skipped.",
                nameof(PipelineDefinition.Notifications),
                "Add a recipient under Notifications."));

    private IEnumerable<ValidationResult> VariableGroups(ValidationContext context) =>
        _variableGroups.Validate(context.Definition.VariableGroups);

    private IEnumerable<ValidationResult> PlainTextSecrets(ValidationContext context) =>
        _secrets.ToValidationResults(_secrets.ScanYaml(context.Yaml));

    private IEnumerable<ValidationResult> KeyVault(ValidationContext context) =>
        _keyVault.Validate(context.Definition.KeyVault);
}
