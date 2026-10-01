using System.Text.RegularExpressions;
using PipelineBuilder.Core.Abstractions;
using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;

namespace PipelineBuilder.Core.Services;

/// <summary>
/// Service for scanning YAML content for plaintext secrets with resilient pattern matching.
/// Handles invalid regex patterns gracefully with logging.
/// </summary>
public sealed class SecretsGovernanceService : ISecretsGovernanceService
{
    private static readonly (Regex Pattern, string Label, SecretSeverity Severity)[] SecretPatterns = InitializePatterns();

    /// <summary>
    /// Safely initializes secret detection patterns with error handling for invalid regex.
    /// </summary>
    private static (Regex Pattern, string Label, SecretSeverity Severity)[] InitializePatterns()
    {
        var patterns = new List<(Regex, string, SecretSeverity)>();
        var patternConfigs = new[]
        {
            (@"password\s*[:=]\s*['""]?[^'""$\s][^'""\n]{3,}", "password", SecretSeverity.Error),
            (@"connectionstring\s*[:=]\s*['""]?[^'""$\s]", "connection string", SecretSeverity.Error),
            (@"clientsecret\s*[:=]\s*['""]?[^'""$\s]", "client secret", SecretSeverity.Error),
            (@"-----BEGIN\s+(RSA\s+)?PRIVATE\s+KEY-----", "private key", SecretSeverity.Error),
            (@"(api[_-]?key|token)\s*[:=]\s*['""]?[a-zA-Z0-9]{16,}", "token or API key", SecretSeverity.Warning),
            (@"AccountKey=[^;$\s]+", "storage account key", SecretSeverity.Error)
        };

        foreach (var (patternStr, label, severity) in patternConfigs)
        {
            try
            {
                patterns.Add((new Regex(patternStr, RegexOptions.IgnoreCase), label, severity));
            }
            catch (RegexParseException ex)
            {
                // Log but continue with other patterns
                System.Diagnostics.Debug.WriteLine($"Invalid regex pattern for '{label}': {ex.Message}");
            }
        }

        return patterns.ToArray();
    }

    private static readonly Regex VariableReference = new(@"\$\([^)]*\)|\$\{\{.*?\}\}", RegexOptions.Compiled);

    public IReadOnlyList<SecretGovernanceResult> ScanYaml(string yaml)
    {
        var results = new List<SecretGovernanceResult>();
        var lines = yaml.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Contains("***"))
                continue;

            // Variable references ($(name), ${{ expr }}) are the safe way to use secrets, so
            // remove them before scanning; a literal secret elsewhere on the same line still counts.
            var scanned = VariableReference.Replace(line, string.Empty);

            foreach (var (pattern, label, severity) in SecretPatterns)
            {
                if (!pattern.IsMatch(scanned)) continue;
                results.Add(new SecretGovernanceResult
                {
                    HasPlainTextSecret = true,
                    SecretLocation = $"Line {i + 1}: {line.Trim()}",
                    Severity = severity,
                    Recommendation = $"Possible plaintext {label} detected. Use Azure DevOps secret variables, variable groups, or Azure Key Vault instead of embedding values in YAML."
                });
            }
        }
        return results;
    }

    public IReadOnlyList<ValidationResult> ToValidationResults(IReadOnlyList<SecretGovernanceResult> results) =>
        results.Select(r => new ValidationResult
        {
            Severity = r.Severity switch
            {
                SecretSeverity.Error => ValidationSeverity.Error,
                SecretSeverity.Warning => ValidationSeverity.Warning,
                _ => ValidationSeverity.Info
            },
            Message = r.Recommendation,
            AffectedField = r.SecretLocation,
            SuggestedFix = "Store secrets in Azure DevOps Library (variable groups), mark variables as secret, or use AzureKeyVault@2."
        }).ToList();
}
