using PipelineBuilder.Core.Abstractions;
using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;

namespace PipelineBuilder.Core.Validation;

/// <summary>
/// Rules that judge the finished pipeline: deployment advice, variable groups, plain-text secrets and
/// Key Vault. Their findings are shown after generation and do not block it. What to set up in
/// Azure DevOps is not a finding: it is listed by <c>PipelineRequirements</c>.
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
            Rule("deployment.kind", rules.KindSpecific),
            Rule("deployment.rolling-without-servers", rules.RollingWithoutServers),
            Rule("deployment.from-agent-needs-own-pool", rules.FromAgentNeedsOwnPool),
            Rule("healthchecks.windows-only", rules.WindowsOnlyHealthChecks),
            Rule("healthchecks.need-servers", rules.HealthChecksThatNeedServers),
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

    /// <summary>Checks that belong to the selected deployment kind (see its handler).</summary>
    private IEnumerable<ValidationResult> KindSpecific(ValidationContext context) =>
        _deploymentKinds.For(context.Definition.Deployment.Kind).Validate(context.Definition);

    private IEnumerable<ValidationResult> RollingWithoutServers(ValidationContext context)
    {
        var definition = context.Definition;
        // A build agent that deploys over the network already takes the servers one at a time.
        if (definition.DeploymentStrategy.StrategyType == DeploymentStrategyType.Rolling
            && !_deploymentKinds.UsesServerResources(definition)
            && !_deploymentKinds.DeploysFromAgentToServers(definition))
        {
            yield return Finding(ValidationSeverity.Warning,
                "Updating a few servers at a time needs an agent on each server. This pipeline updates everything at once.",
                nameof(PipelineDefinition.DeploymentStrategy),
                "Untick 'Update the servers a few at a time'.");
        }
    }

    private IEnumerable<ValidationResult> FromAgentNeedsOwnPool(ValidationContext context)
    {
        var definition = context.Definition;
        if (_deploymentKinds.DeploysFromAgentToServers(definition) && definition.BuildAgent == BuildAgentType.MicrosoftHosted)
        {
            yield return Finding(ValidationSeverity.Warning,
                "The build agent deploys over the network, but Microsoft's agents cannot reach servers inside your network.",
                nameof(PipelineDefinition.BuildAgent),
                "Tick 'Build on our own agent pool' under More settings, or let an agent on each server deploy.");
        }
    }

    private IEnumerable<ValidationResult> WindowsOnlyHealthChecks(ValidationContext context)
    {
        var definition = context.Definition;
        if (_deploymentKinds.ShellFor(definition) == ScriptShell.Bash
            && definition.HealthChecks.Any(h => h.Enabled && h.HealthCheckType == HealthCheckType.IisAppPool))
        {
            yield return Finding(ValidationSeverity.Warning,
                "An IIS app pool check only works on Windows servers, so it is skipped.",
                nameof(PipelineDefinition.HealthChecks),
                "Remove the check, or change it to an HTTP address or Service check.");
        }
    }

    /// <summary>A service, port or app pool check looks at the machine it runs on, which must be one of the servers.</summary>
    private IEnumerable<ValidationResult> HealthChecksThatNeedServers(ValidationContext context)
    {
        var definition = context.Definition;
        if (!_deploymentKinds.For(definition.Deployment.Kind).RunsOnServers
            && definition.HealthChecks.Any(h => h.Enabled && h.HealthCheckType is HealthCheckType.IisAppPool or HealthCheckType.WindowsService or HealthCheckType.PortCheck))
        {
            yield return Finding(ValidationSeverity.Warning,
                "A service, port or app pool check looks at the machine it runs on. Here that is the build agent, not your servers.",
                nameof(PipelineDefinition.HealthChecks),
                "Change the check to an HTTP address.");
        }
    }

    private static IEnumerable<ValidationResult> EmailRecipients(ValidationContext context) =>
        context.Definition.Notifications
            .Where(n => n.NotificationType == NotificationType.Email && !n.EmailRecipients.Any(r => !string.IsNullOrWhiteSpace(r)))
            .Select(_ => Finding(ValidationSeverity.Warning,
                "An email notification has no recipients, so it is skipped.",
                nameof(PipelineDefinition.Notifications),
                "Add a recipient under Notifications."));

    private IEnumerable<ValidationResult> VariableGroups(ValidationContext context) =>
        _variableGroups.Validate(context.Definition.VariableGroups);

    private IEnumerable<ValidationResult> PlainTextSecrets(ValidationContext context) =>
        _secrets.ToValidationResults(_secrets.ScanYaml(context.Yaml));

    private IEnumerable<ValidationResult> KeyVault(ValidationContext context) =>
        _keyVault.Validate(context.Definition.KeyVault);
}
