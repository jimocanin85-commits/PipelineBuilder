using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Services;
using Xunit;

namespace PipelineBuilder.Tests;

/// <summary>Plain-text secrets in the YAML, and secret variable groups shared too widely.</summary>
public class GovernanceAndSecretsTests
{
    [Theory]
    [InlineData("password = 'abc12345' # $(unused)", true)]
    [InlineData("  connectionString: 'Server=db;Password=x' $(Build.BuildId)", true)]
    [InlineData("password: $(DB_PASSWORD)", false)]
    [InlineData("password: ${{ variables.dbPassword }}", false)]
    [InlineData("SMTP_PASSWORD: '$(SMTP_PASSWORD)'", false)]
    [InlineData("password: ***", false)]
    public void SecretsNextToVariableReferencesAreStillFound(string line, bool expected)
    {
        var found = new SecretsGovernanceService().ScanYaml(line).Any(r => r.HasPlainTextSecret);
        Assert.Equal(expected, found);
    }

    [Fact]
    public void SecretGroupsSharedWithTheWholePipelineAreFlagged()
    {
        var groups = new[]
        {
            new VariableGroupConfig { Name = "vg-shared-secrets", Scope = VariableGroupScope.Pipeline, ContainsSecrets = true },
            new VariableGroupConfig { Name = "vg-prod-secrets", Scope = VariableGroupScope.Environment, EnvironmentName = "prod", ContainsSecrets = true },
            new VariableGroupConfig { Name = "vg-common", Scope = VariableGroupScope.Pipeline }
        };

        var warnings = new VariableGroupService().Validate(groups);

        var warning = Assert.Single(warnings);
        Assert.Equal(ValidationSeverity.Warning, warning.Severity);
        Assert.Contains("vg-shared-secrets", warning.Message);
    }
}
