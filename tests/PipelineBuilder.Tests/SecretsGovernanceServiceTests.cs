using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Services;
using Xunit;

namespace PipelineBuilder.Tests;

/// <summary>The scan for secrets written in plain text in the YAML.</summary>
public class SecretsGovernanceServiceTests
{
    private readonly SecretsGovernanceService _sut = new();

    [Theory]
    [InlineData("  dbPassword: 'SuperSecret123!'", true)]
    [InlineData("password = 'abc12345' # $(unused)", true)]
    [InlineData("  connectionString: 'Server=db;Password=x' $(Build.BuildId)", true)]
    [InlineData("password: $(DB_PASSWORD)", false)]
    [InlineData("password: ${{ variables.dbPassword }}", false)]
    [InlineData("SMTP_PASSWORD: '$(SMTP_PASSWORD)'", false)]
    [InlineData("password: ***", false)]
    public void ASecretIsFoundAlsoNextToAVariableReferenceButAReferenceAloneIsFine(string line, bool expected)
    {
        var found = _sut.ScanYaml(line).Any(r => r.HasPlainTextSecret);
        Assert.Equal(expected, found);
    }

    [Fact]
    public void APasswordInPlainTextIsAnError()
    {
        var results = _sut.ScanYaml("variables:\n  dbPassword: 'SuperSecret123!'");
        Assert.Contains(results, r => r.HasPlainTextSecret && r.Severity == SecretSeverity.Error);
    }
}
