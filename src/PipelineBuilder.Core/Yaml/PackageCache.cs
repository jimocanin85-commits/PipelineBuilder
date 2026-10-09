namespace PipelineBuilder.Core.Yaml;

/// <summary>
/// The folders where NuGet and npm keep downloaded packages during a build. The build job sets them,
/// so a Cache step can keep the packages between runs. They are the pipeline's own, not something to set up.
/// </summary>
public static class PackageCache
{
    public const string NuGetVariable = "NUGET_PACKAGES";
    public const string NpmVariable = "npm_config_cache";
}
