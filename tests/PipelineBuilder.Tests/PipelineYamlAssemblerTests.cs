using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Services;
using Xunit;

namespace PipelineBuilder.Tests;

public class PipelineYamlAssemblerTests
{
    [Fact]
    public void BuildsHeaderTriggerVariablesPoolAndStages()
    {
        var yaml = new PipelineYamlAssembler()
            .AddHeader("test-pipeline")
            .AddTrigger(TriggerConfig.Default)
            .AddVariables("variables:\n  - name: BuildConfiguration\n    value: Release")
            .AddPool("vmImage: 'windows-latest'")
            .StartStages()
            .Build();

        Assert.Contains("# Pipeline: test-pipeline", yaml);
        Assert.Contains("trigger:", yaml);
        Assert.Contains("      - 'main'", yaml);
        Assert.Contains("      - 'develop'", yaml);
        Assert.Contains("variables:", yaml);
        Assert.Contains("pool:\n  vmImage: 'windows-latest'", yaml.ReplaceLineEndings("\n"));
        Assert.EndsWith("stages:", yaml);
    }

    [Fact]
    public void IgnoresEmptyVariablesAndStages()
    {
        var yaml = new PipelineYamlAssembler()
            .AddHeader("test")
            .AddVariables(null)
            .AddVariables("   ")
            .StartStages()
            .AddStage(null)
            .AddStage("")
            .Build();

        Assert.DoesNotContain("variables:", yaml);
        Assert.DoesNotContain("\n\n\n", yaml.ReplaceLineEndings("\n"));
        Assert.EndsWith("stages:", yaml);
    }

    [Fact]
    public void AddsCustomStage()
    {
        var yaml = new PipelineYamlAssembler()
            .StartStages()
            .AddStage("- stage: CustomStage\n  displayName: 'Custom'")
            .Build();

        Assert.Contains("- stage: CustomStage", yaml);
    }

    [Fact]
    public void TriggerAllBranchesUsesWildcard()
    {
        var yaml = new PipelineYamlAssembler().AddTrigger(TriggerConfig.AllBranches).Build();

        Assert.Contains("      - '*'", yaml);
        Assert.DoesNotContain("'main'", yaml);
    }

    [Fact]
    public void TriggerOnlyEmitsPathsWhenFiltersAreSet()
    {
        var yaml = new PipelineYamlAssembler().AddTrigger(TriggerConfig.MainOnly).Build();

        Assert.Contains("      - 'main'", yaml);
        Assert.DoesNotContain("develop", yaml);
        Assert.DoesNotContain("paths:", yaml);
        Assert.DoesNotContain("exclude:", yaml);
    }
}
