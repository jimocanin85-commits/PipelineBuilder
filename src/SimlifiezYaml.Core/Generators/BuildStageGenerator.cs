using SimlifiezYaml.Core.Abstractions;
using SimlifiezYaml.Core.Enums;
using SimlifiezYaml.Core.Models;
using SimlifiezYaml.Core.Yaml;

namespace SimlifiezYaml.Core.Generators;

/// <summary>
/// The single Build stage: restore, compile once, test and package in one job, then upload the
/// artifact. Everything after the compile step uses <c>--no-build</c>, so the code is compiled once.
/// Microsoft-hosted builds run on Linux, which is faster; deployments still use the Windows pool.
/// </summary>
public sealed class BuildStageGenerator
{
    private readonly IArtifactYamlService _artifactService;

    public BuildStageGenerator(IArtifactYamlService artifactService) => _artifactService = artifactService;

    public string Generate(PipelineDefinition definition)
    {
        var steps = new List<string>();
        steps.AddRange(definition.ProjectType == ProjectType.DotNet ? DotNetSteps(definition) : PlaceholderSteps(definition));
        steps.AddRange(_artifactService.GenerateBuildOutputSteps(definition));
        steps.AddRange(_artifactService.GeneratePublishSteps(definition.Artifact));

        var pool = PoolConfigurationHelper.GenerateBuildPoolConfiguration(definition.BuildAgent, definition.PoolName);
        return $"""
- stage: Build
  displayName: 'Build'
  jobs:
  - job: BuildJob
    displayName: 'Build, test and package'
    pool:
      {pool}
    steps:
{YamlBuilder.Indent(string.Join("\n", steps), 6)}
""";
    }

    private static IEnumerable<string> DotNetSteps(PipelineDefinition definition)
    {
        // Build every project (including tests) once; publish later picks the app project(s) with --no-build.
        const string allProjects = "**/*.csproj";
        yield return YamlBuilder.Task("DotNetCoreCLI@2", new Dictionary<string, string>
        {
            ["command"] = "restore",
            ["projects"] = allProjects
        }, "Restore NuGet packages");
        yield return YamlBuilder.Task("DotNetCoreCLI@2", new Dictionary<string, string>
        {
            ["command"] = "build",
            ["projects"] = allProjects,
            ["arguments"] = "--configuration $(BuildConfiguration) --no-restore"
        }, "Build");
        yield return YamlBuilder.Task("DotNetCoreCLI@2", new Dictionary<string, string>
        {
            ["command"] = "test",
            ["projects"] = definition.TestProjectPath ?? "**/*Tests*.csproj",
            ["arguments"] = "--configuration $(BuildConfiguration) --no-build --collect:\"XPlat Code Coverage\""
        }, "Run tests");
    }

    private static IEnumerable<string> PlaceholderSteps(PipelineDefinition definition)
    {
        yield return YamlBuilder.PowerShellStep(
            $"Write-Warning 'Add the build and test commands for {definition.ProjectType} projects here.'",
            "Build and test (placeholder)");
    }
}
