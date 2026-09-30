using System.Text;
using SimlifiezYaml.Core.Abstractions;
using SimlifiezYaml.Core.Enums;
using SimlifiezYaml.Core.Models;
using SimlifiezYaml.Core.Yaml;

namespace SimlifiezYaml.Core.Generators;

public sealed class BuildStageGenerator
{
    public string Generate(PipelineDefinition definition)
    {
        var pool = PoolConfigurationHelper.GeneratePoolConfiguration(definition.BuildAgent, definition.PoolName);

        var steps = new StringBuilder();
        if (definition.ProjectType == ProjectType.DotNet)
        {
            var project = definition.DotNetProjectPath ?? "**/*.csproj";
            steps.AppendLine(YamlBuilder.Task("DotNetCoreCLI@2", new Dictionary<string, string>
            {
                ["command"] = "restore",
                ["projects"] = project
            }, "Restore NuGet packages"));
            steps.AppendLine();
            steps.AppendLine(YamlBuilder.Task("DotNetCoreCLI@2", new Dictionary<string, string>
            {
                ["command"] = "build",
                ["projects"] = project,
                ["arguments"] = "--configuration $(BuildConfiguration) --no-restore"
            }, "Build solution"));
        }
        else
        {
            steps.AppendLine(YamlBuilder.PowerShellStep(
                "Write-Host 'Build steps configured for project type: " + definition.ProjectType + "'",
                "Build placeholder"));
        }

        return $"""
- stage: Build
  displayName: 'Build'
  jobs:
  - job: BuildJob
    displayName: 'Compile and package'
    pool:
      {pool}
    steps:
{YamlBuilder.Indent(steps.ToString(), 6)}
""";
    }
}
