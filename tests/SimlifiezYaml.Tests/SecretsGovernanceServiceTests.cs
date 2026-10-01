using SimlifiezYaml.Core.Enums;
using SimlifiezYaml.Core.Services;
using Xunit;

namespace SimlifiezYaml.Tests;

public class SecretsGovernanceServiceTests
{
    private readonly SecretsGovernanceService _sut = new();

    [Fact]
    public void ScanYaml_DetectsPlaintextPassword()
    {
        var yaml = "variables:\n  dbPassword: 'SuperSecret123!'";
        var results = _sut.ScanYaml(yaml);
        Assert.Contains(results, r => r.HasPlainTextSecret && r.Severity == SecretSeverity.Error);
    }

    [Fact]
    public void ScanYaml_IgnoresVariableReferences()
    {
        var yaml = "password: $(DB_PASSWORD)";
        var results = _sut.ScanYaml(yaml);
        Assert.Empty(results);
    }
}
