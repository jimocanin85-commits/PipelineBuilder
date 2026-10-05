using PipelineBuilder.Core.Abstractions;
using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Yaml;

namespace PipelineBuilder.Core.Services;

public sealed class KeyVaultYamlService : IKeyVaultYamlService
{
    public string GeneratePreJobSteps(KeyVaultConfig config)
    {
        return YamlBuilder.Task("AzureKeyVault@2", new Dictionary<string, string>
        {
            ["azureSubscription"] = config.ServiceConnection,
            ["KeyVaultName"] = config.KeyVaultName,
            ["SecretsFilter"] = config.SecretsFilter,
            ["RunAsPreJob"] = config.RunAsPreJob.ToString().ToLowerInvariant()
        }, "Download secrets from Azure Key Vault");
    }

    public IReadOnlyList<ValidationResult> Validate(KeyVaultConfig? config)
    {
        if (config == null) return Array.Empty<ValidationResult>();
        var results = new List<ValidationResult>();
        if (string.IsNullOrWhiteSpace(config.KeyVaultName))
        {
            results.Add(new ValidationResult
            {
                Severity = ValidationSeverity.Error,
                Message = "Type the Key Vault name.",
                AffectedField = nameof(PipelineDefinition.KeyVault),
                SuggestedFix = "It is the vault's name in the Azure portal."
            });
        }
        return results;
    }
}
