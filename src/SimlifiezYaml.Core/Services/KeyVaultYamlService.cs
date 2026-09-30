using SimlifiezYaml.Core.Abstractions;
using SimlifiezYaml.Core.Enums;
using SimlifiezYaml.Core.Models;
using SimlifiezYaml.Core.Yaml;

namespace SimlifiezYaml.Core.Services;

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
                Message = "Key Vault name is required when Key Vault integration is enabled.",
                AffectedField = nameof(PipelineDefinition.KeyVault),
                SuggestedFix = "Specify the Azure Key Vault name linked to your service connection."
            });
        }
        return results;
    }
}
