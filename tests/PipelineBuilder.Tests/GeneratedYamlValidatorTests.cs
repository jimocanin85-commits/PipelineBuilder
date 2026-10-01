using PipelineBuilder.Core.Yaml;
using Xunit;

namespace PipelineBuilder.Tests;

public class GeneratedYamlValidatorTests
{
    [Fact]
    public void AcceptsAValidPipeline()
    {
        const string yaml = """
            stages:
            - stage: Build
              jobs:
              - job: BuildJob
            - stage: Deploy_test
              dependsOn: Build
              jobs:
              - job: Infrastructure
              - deployment: DeployTotest
                dependsOn: Infrastructure
            """;
        Assert.Empty(GeneratedYamlValidator.Validate(yaml));
    }

    [Theory]
    [InlineData("stages:\n- stage: A\n  jobs:\n   - job: x\n  - job: y", "Not valid YAML")]
    [InlineData("trigger: none", "no 'stages' list")]
    [InlineData("stages:\n- stage: A\n- stage: A", "defined more than once")]
    [InlineData("stages:\n- stage: pre-prod", "Invalid stage name")]
    [InlineData("stages:\n- stage: B\n  dependsOn: A", "unknown stage 'A'")]
    [InlineData("stages:\n- stage: B\n  dependsOn:\n  - A", "unknown stage 'A'")]
    [InlineData("stages:\n- stage: A\n  jobs:\n  - job: X\n    dependsOn: Y", "unknown job 'Y'")]
    [InlineData("stages:\n- stage: A\n  jobs:\n  - job: X\n  - deployment: X", "Job 'X' is defined more than once")]
    public void ReportsProblems(string yaml, string expected)
    {
        var problems = GeneratedYamlValidator.Validate(yaml);
        Assert.Contains(problems, p => p.Contains(expected));
    }
}
