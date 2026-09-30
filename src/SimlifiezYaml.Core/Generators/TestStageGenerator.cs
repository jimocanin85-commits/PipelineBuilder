using System.Text;
using SimlifiezYaml.Core.Abstractions;
using SimlifiezYaml.Core.Enums;
using SimlifiezYaml.Core.Models;
using SimlifiezYaml.Core.Yaml;

namespace SimlifiezYaml.Core.Generators;

public sealed class TestStageGenerator
{
    public string Generate(PipelineDefinition definition)
    {
        var pool = PoolConfigurationHelper.GeneratePoolConfiguration(definition.BuildAgent, definition.PoolName);

        var steps = new StringBuilder();
        if (definition.ProjectType == ProjectType.DotNet)
        {
            steps.AppendLine(YamlBuilder.Task("DotNetCoreCLI@2", new Dictionary<string, string>
            {
                ["command"] = "test",
                ["projects"] = definition.TestProjectPath ?? "**/*Tests*.csproj",
                ["arguments"] = "--configuration $(BuildConfiguration) --collect:\"XPlat Code Coverage\""
            }, "Run unit tests"));
        }
        else
        {
            steps.AppendLine(YamlBuilder.PowerShellStep("Write-Host 'No test runner configured'", "Test placeholder"));
        }

        return $"""
- stage: Test
  displayName: 'Test'
  dependsOn: Build
  condition: succeeded()
  jobs:
  - job: TestJob
    displayName: 'Run tests'
    pool:
      {pool}
    steps:
{YamlBuilder.Indent(steps.ToString(), 6)}
""";
    }
}
