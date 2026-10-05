using System.Text.RegularExpressions;
using PipelineBuilder.Core.Abstractions;
using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Yaml;

namespace PipelineBuilder.Core.Services;

public sealed class KeyVaultYamlService : IKeyVaultYamlService
{
    /// <summary>What Azure Key Vault accepts as a secret name.</summary>
    private static readonly Regex SecretName = new("^[A-Za-z0-9-]{1,127}$", RegexOptions.Compiled);

    public string GeneratePreJobSteps(KeyVaultConfig config)
    {
        return YamlBuilder.Task("AzureKeyVault@2", new Dictionary<string, string>
        {
            ["azureSubscription"] = config.ServiceConnection,
            ["KeyVaultName"] = config.KeyVaultName,
            // Only the secrets that are named; every secret in the vault only when none is.
            ["SecretsFilter"] = config.SecretNames.Count > 0 ? string.Join(",", config.SecretNames) : "*",
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

        // Least privilege: a deployment should only be handed the secrets it uses.
        if (config.SecretNames.Count == 0)
        {
            results.Add(new ValidationResult
            {
                Severity = ValidationSeverity.Warning,
                Message = "The pipeline gets every secret in the Key Vault, also the ones it does not use.",
                AffectedField = nameof(PipelineDefinition.KeyVault),
                SuggestedFix = "Name the secrets it needs under 'Secrets to get'."
            });
        }

        foreach (var name in config.SecretNames.Where(name => !SecretName.IsMatch(name)))
        {
            results.Add(new ValidationResult
            {
                Severity = ValidationSeverity.Error,
                Message = $"'{name}' is not a Key Vault secret name. A name has letters, digits and hyphens.",
                AffectedField = nameof(PipelineDefinition.KeyVault),
                SuggestedFix = "Type the names as they are in the vault, separated by commas."
            });
        }
        return results;
    }
}
