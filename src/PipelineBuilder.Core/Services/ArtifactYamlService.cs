using PipelineBuilder.Core.Abstractions;
using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Yaml;

namespace PipelineBuilder.Core.Services;

public sealed class ArtifactYamlService : IArtifactYamlService
{
    /// <summary>Folder that <c>dotnet publish</c> writes to before the output is packaged.</summary>
    public const string PublishOutput = "$(Build.ArtifactStagingDirectory)/app";

    public IReadOnlyList<string> GenerateBuildOutputSteps(PipelineDefinition definition)
    {
        var config = definition.Artifact;
        if (config.ArtifactType is ArtifactType.DockerImage or ArtifactType.NuGetPackage)
            return Array.Empty<string>(); // these package straight from source

        if (definition.ProjectType == ProjectType.Node)
        {
            return new[]
            {
                YamlBuilder.Task("CopyFiles@2", new Dictionary<string, string>
                {
                    ["SourceFolder"] = definition.NodeOutputFolder,
                    ["Contents"] = "**",
                    ["TargetFolder"] = PublishOutput
                }, $"Copy build output ({definition.NodeOutputFolder})")
            };
        }

        if (definition.ProjectType != ProjectType.DotNet)
        {
            return new[]
            {
                YamlBuilder.PowerShellStep(
                    $"Write-Warning 'Add the build/copy commands for {definition.ProjectType} projects here. " +
                    $"Put the deployable output in {PublishOutput}.'\n" +
                    $"New-Item -ItemType Directory -Force -Path \"{PublishOutput}\" | Out-Null",
                    "Prepare build output (placeholder)")
            };
        }

        var projectPath = definition.DotNetProjectPath;
        var publishWebProjects = string.IsNullOrWhiteSpace(projectPath) || projectPath == "**/*.csproj";
        var inputs = new Dictionary<string, string>
        {
            ["command"] = "publish",
            ["publishWebProjects"] = publishWebProjects ? "true" : "false",
        };
        if (!publishWebProjects)
            inputs["projects"] = projectPath!;
        inputs["arguments"] = $"--configuration $(BuildConfiguration) --no-build --output {PublishOutput}";
        inputs["zipAfterPublish"] = "false";
        inputs["modifyOutputPath"] = "false";

        return new[] { YamlBuilder.Task("DotNetCoreCLI@2", inputs, "Publish application") };
    }

    public IReadOnlyList<string> GeneratePublishSteps(ArtifactConfig config)
    {
        return config.ArtifactType switch
        {
            ArtifactType.PipelineArtifact => new[]
            {
                YamlBuilder.Task("PublishPipelineArtifact@1", new Dictionary<string, string>
                {
                    ["targetPath"] = config.PublishPath ?? PublishOutput,
                    ["artifactName"] = config.ArtifactName,
                    ["publishLocation"] = "pipeline"
                }, $"Publish pipeline artifact: {config.ArtifactName}")
            },
            ArtifactType.BuildArtifact => new[]
            {
                YamlBuilder.Task("PublishBuildArtifacts@1", new Dictionary<string, string>
                {
                    ["PathtoPublish"] = config.PublishPath ?? PublishOutput,
                    ["ArtifactName"] = config.ArtifactName,
                    ["publishLocation"] = "Container"
                }, $"Publish build artifact: {config.ArtifactName}")
            },
            ArtifactType.ZipPackage => new[]
            {
                YamlBuilder.Task("ArchiveFiles@2", new Dictionary<string, string>
                {
                    ["rootFolderOrFile"] = config.PackagePath ?? PublishOutput,
                    ["includeRootFolder"] = "false",
                    ["archiveType"] = "zip",
                    ["archiveFile"] = $"$(Build.ArtifactStagingDirectory)/{config.ArtifactName}.zip",
                    ["replaceExistingArchive"] = "true"
                }, "Archive deployment package"),
                YamlBuilder.Task("PublishPipelineArtifact@1", new Dictionary<string, string>
                {
                    ["targetPath"] = $"$(Build.ArtifactStagingDirectory)/{config.ArtifactName}.zip",
                    ["artifactName"] = config.ArtifactName,
                    ["publishLocation"] = "pipeline"
                }, "Publish zip artifact")
            },
            ArtifactType.DockerImage => new[]
            {
                YamlBuilder.Task("Docker@2", new Dictionary<string, string>
                {
                    ["command"] = "buildAndPush",
                    ["repository"] = config.ArtifactName,
                    ["dockerfile"] = config.PackagePath ?? "**/Dockerfile",
                    ["containerRegistry"] = config.ContainerRegistryConnection,
                    ["tags"] = "$(Build.BuildId)"
                }, "Build and push Docker image")
            },
            ArtifactType.NuGetPackage => new[]
            {
                YamlBuilder.Task("DotNetCoreCLI@2", new Dictionary<string, string>
                {
                    ["command"] = "pack",
                    ["packagesToPack"] = config.PackagePath ?? "**/*.csproj;!**/*Tests*.csproj",
                    ["configuration"] = "$(BuildConfiguration)",
                    ["nobuild"] = "true",
                    ["packDirectory"] = "$(Build.ArtifactStagingDirectory)/packages"
                }, "Pack NuGet packages"),
                // dotnet push works on Linux agents; NuGetCommand@2 needs Mono there.
                YamlBuilder.Task("DotNetCoreCLI@2", new Dictionary<string, string>
                {
                    ["command"] = "push",
                    ["packagesToPush"] = "$(Build.ArtifactStagingDirectory)/packages/*.nupkg",
                    ["nuGetFeedType"] = "internal",
                    ["publishVstsFeed"] = "$(NUGET_FEED)"
                }, "Push NuGet packages")
            },
            _ => Array.Empty<string>()
        };
    }

    public IReadOnlyList<string> GenerateDownloadSteps(ArtifactConfig config, string? environment = null)
    {
        if (config.ArtifactType is ArtifactType.DockerImage or ArtifactType.NuGetPackage)
            return Array.Empty<string>(); // nothing to download: the image/package lives in a registry/feed

        var display = environment != null ? $"Download artifact for {environment}" : "Download artifact";
        return config.ArtifactType switch
        {
            ArtifactType.BuildArtifact => new[]
            {
                YamlBuilder.Task("DownloadBuildArtifacts@1", new Dictionary<string, string>
                {
                    ["buildType"] = "current",
                    ["downloadType"] = "single",
                    ["artifactName"] = config.ArtifactName,
                    ["downloadPath"] = config.DownloadPath ?? "$(Pipeline.Workspace)"
                }, display)
            },
            _ => new[]
            {
                YamlBuilder.Task("DownloadPipelineArtifact@2", new Dictionary<string, string>
                {
                    ["artifactName"] = config.ArtifactName,
                    ["targetPath"] = DownloadFolder(config)
                }, display)
            }
        };
    }

    public string GetDeployPackagePath(ArtifactConfig config) =>
        config.ArtifactType == ArtifactType.ZipPackage
            ? $"{DownloadFolder(config)}/{config.ArtifactName}.zip"
            : DownloadFolder(config);

    private static string DownloadFolder(ArtifactConfig config) =>
        config.ArtifactType == ArtifactType.BuildArtifact
            // DownloadBuildArtifacts always creates a subfolder named after the artifact.
            ? $"{config.DownloadPath ?? "$(Pipeline.Workspace)"}/{config.ArtifactName}"
            : config.DownloadPath ?? $"$(Pipeline.Workspace)/{config.ArtifactName}";
}
