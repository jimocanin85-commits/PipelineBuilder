using SimlifiezYaml.Core.Abstractions;
using SimlifiezYaml.Core.Models;
using SimlifiezYaml.Core.Yaml;

namespace SimlifiezYaml.Core.Generators;

/// <summary>
/// Builds the deployable output (e.g. <c>dotnet publish</c>) and publishes it as an artifact.
/// This runs in its own job, so it produces its own output rather than relying on the Build job's agent.
/// </summary>
public sealed class ArtifactStageGenerator
{
    private readonly IArtifactYamlService _artifactService;

    public ArtifactStageGenerator(IArtifactYamlService artifactService) => _artifactService = artifactService;

    public string Generate(PipelineDefinition definition)
    {
        var pool = PoolConfigurationHelper.GeneratePoolConfiguration(definition.BuildAgent, definition.PoolName);
        var steps = string.Join("\n", _artifactService.GenerateBuildOutputSteps(definition)
            .Concat(_artifactService.GeneratePublishSteps(definition.Artifact)));
        return $"""
- stage: Artifact
  displayName: 'Publish artifacts'
  dependsOn: Test
  condition: succeeded()
  jobs:
  - job: PublishArtifact
    displayName: 'Package build output'
    pool:
      {pool}
    steps:
{YamlBuilder.Indent(steps, 6)}
""";
    }
}
