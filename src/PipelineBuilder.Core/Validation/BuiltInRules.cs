using PipelineBuilder.Core.Abstractions;

namespace PipelineBuilder.Core.Validation;

/// <summary>The rules that ship with PipelineBuilder, in the order their findings are shown.</summary>
public static class BuiltInRules
{
    public static IReadOnlyList<ValidationRule> Create(
        IDeploymentKinds deploymentKinds,
        IVariableGroupService variableGroups,
        ISecretsGovernanceService secrets,
        IKeyVaultYamlService keyVault)
    {
        ArgumentNullException.ThrowIfNull(deploymentKinds);
        ArgumentNullException.ThrowIfNull(variableGroups);
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(keyVault);

        return InputRules.All()
            .Concat(PolicyRules.All(deploymentKinds, variableGroups, secrets, keyVault))
            .ToList();
    }
}
