using PipelineBuilder.Core.Validation;
using PipelineBuilder.Web.State;
using Xunit;

namespace PipelineBuilder.Tests;

public class WizardStateTests
{
    [Fact]
    public void ANewWizardDeploysToTestPreprodAndProd()
    {
        var wizard = new WizardState(PipelineValidator.CreateDefault());

        Assert.Equal("test, preprod, prod", wizard.EnvironmentsCsv);
        Assert.False(wizard.SkipPreprod);
    }

    [Theory]
    [InlineData("test, preprod, prod", "test, prod", "test, preprod, prod")]
    [InlineData("test, PreProd, production", "test, production", "test, preprod, production")]
    [InlineData("dev, qa", "dev, qa", "dev, qa, preprod")]
    public void PreprodCanBeSkippedAndGoesBackRightBeforeProduction(string environments, string skipped, string restored)
    {
        var wizard = new WizardState(PipelineValidator.CreateDefault()) { EnvironmentsCsv = environments };

        wizard.SkipPreprod = true;
        Assert.True(wizard.SkipPreprod);
        Assert.Equal(skipped, wizard.EnvironmentsCsv);

        wizard.SkipPreprod = false;
        Assert.False(wizard.SkipPreprod);
        Assert.Equal(restored, wizard.EnvironmentsCsv);
    }
}
