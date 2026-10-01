using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Services;
using Xunit;

namespace PipelineBuilder.Tests;

public class RepoScannerServiceTests
{
    private readonly RepoScannerService _sut = new();

    [Fact]
    public void ScanFileList_DetectsDotNetAndDocker()
    {
        var result = _sut.ScanFileList(new[] { "src/App.csproj", "Dockerfile", "tests/App.Tests.csproj" });
        Assert.Equal(ProjectType.DotNet, result.ProjectType);
        Assert.True(result.HasDockerfile);
        Assert.True(result.HasTests);
        Assert.Contains("docker-build-push", result.SuggestedTemplates);
    }
}
