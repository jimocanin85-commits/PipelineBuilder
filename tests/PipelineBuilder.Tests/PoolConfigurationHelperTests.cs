using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Yaml;
using Xunit;

namespace PipelineBuilder.Tests;

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
