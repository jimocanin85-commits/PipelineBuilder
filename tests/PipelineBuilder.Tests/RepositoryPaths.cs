namespace PipelineBuilder.Tests;

/// <summary>Locates files in the repository from the test output folder.</summary>
internal static class RepositoryPaths
{
    /// <summary>The folder that contains <c>PipelineBuilder.sln</c>.</summary>
    public static string Root { get; } = FindRoot();

    /// <summary>Golden files: the approved YAML for each case in <see cref="GoldenFileTests"/>.</summary>
    public static string GoldenFolder => Path.Combine(Root, "tests", "PipelineBuilder.Tests", "Golden");

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PipelineBuilder.sln")))
                return dir.FullName;
        }

        throw new InvalidOperationException($"Could not find PipelineBuilder.sln above {AppContext.BaseDirectory}.");
    }
}
