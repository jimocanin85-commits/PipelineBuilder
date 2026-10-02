using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Services;
using Xunit;

namespace PipelineBuilder.Tests;

public class ArtifactYamlServiceTests
{
    private readonly ArtifactYamlService _sut = new();

    [Theory]
    [InlineData(ArtifactType.PipelineArtifact, "PublishPipelineArtifact@1")]
    [InlineData(ArtifactType.DockerImage, "Docker@2")]
    [InlineData(ArtifactType.ZipPackage, "ArchiveFiles@2")]
    public void GeneratePublishSteps_ContainsExpectedTask(ArtifactType type, string task)
    {
        var steps = _sut.GeneratePublishSteps(new ArtifactConfig { ArtifactType = type, ArtifactName = "drop" });
        Assert.Contains(steps, s => s.Contains(task));
    }

    [Fact]
    public void GenerateDownloadSteps_UsesPipelineArtifactTask()
    {
        var steps = _sut.GenerateDownloadSteps(new ArtifactConfig { ArtifactType = ArtifactType.PipelineArtifact, ArtifactName = "drop" }, "test");
        Assert.Contains("DownloadPipelineArtifact@2", steps[0]);
    }
}
