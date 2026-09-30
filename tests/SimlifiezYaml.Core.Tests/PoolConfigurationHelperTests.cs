using SimlifiezYaml.Core.Enums;
using SimlifiezYaml.Core.Models;
using SimlifiezYaml.Core.Yaml;
using Xunit;

namespace SimlifiezYaml.Core.Tests;

public class PoolConfigurationHelperTests
{
    [Fact]
    public void SelfHostedWithPoolName()
    {
        var config = PoolConfigurationHelper.GeneratePoolConfiguration(BuildAgentType.SelfHosted, "MyPool");
        Assert.Equal("name: 'MyPool'", config);
    }

    [Fact]
    public void SelfHostedWithoutPoolName()
    {
        var config = PoolConfigurationHelper.GeneratePoolConfiguration(BuildAgentType.SelfHosted, null);
        Assert.Equal("name: 'Default'", config);
    }

    [Fact]
    public void MicrosoftHosted()
    {
        var config = PoolConfigurationHelper.GeneratePoolConfiguration(BuildAgentType.MicrosoftHosted, "ignored");
        Assert.Equal("vmImage: 'windows-latest'", config);
    }
}
