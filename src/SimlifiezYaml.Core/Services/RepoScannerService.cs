using System.Text.RegularExpressions;
using SimlifiezYaml.Core.Abstractions;
using SimlifiezYaml.Core.Enums;
using SimlifiezYaml.Core.Models;

namespace SimlifiezYaml.Core.Services;

public sealed class RepoScannerService : IRepoScannerService
{
    private static readonly (string Pattern, Action<RepoScanResultBuilder> Apply)[] Rules =
    {
        ("*.sln", b => { b.HasDotNet = true; b.Suggest("dotnet-web-app"); }),
        ("*.csproj", b => b.HasDotNet = true),
        ("*Tests*.csproj", b => { b.HasTests = true; b.Suggest("dotnet-with-tests"); }),
        ("Dockerfile", b => { b.HasDockerfile = true; b.Suggest("docker-build-push"); }),
        ("docker-compose.yml", b => b.Suggest("docker-compose-deploy")),
        ("docker-compose.yaml", b => b.Suggest("docker-compose-deploy")),
        ("package.json", b => { b.ProjectType = ProjectType.Node; b.Suggest("node-build"); }),
        ("*.tf", b => { b.HasTerraform = true; b.Suggest("terraform-azure"); }),
        ("*.bicep", b => { b.HasBicep = true; b.Suggest("bicep-deploy"); }),
        ("*.yaml", b => b.Suggest("kubernetes-aks")),
        ("azure-pipelines.yml", b => b.Suggest("existing-pipeline-migrate")),
        ("azure-pipelines.yaml", b => b.Suggest("existing-pipeline-migrate"))
    };

    public RepoScanResult ScanFileList(IReadOnlyList<string> relativePaths)
    {
        var builder = new RepoScanResultBuilder();
        foreach (var path in relativePaths)
        {
            builder.DetectedFiles.Add(path);
            foreach (var (pattern, apply) in Rules)
            {
                if (MatchesPattern(path, pattern))
                    apply(builder);
            }
        }

        builder.ProjectType ??= builder.HasDotNet ? ProjectType.DotNet
            : builder.HasDockerfile ? ProjectType.Docker
            : builder.HasTerraform ? ProjectType.Terraform
            : ProjectType.Unknown;

        if (builder.HasDockerfile && builder.HasDotNet)
            builder.Suggest("hybrid-dotnet-docker");

        return builder.Build();
    }

    /// <summary>
    /// Matches a simple glob (<c>*</c> wildcards only) against the file name part of <paramref name="path"/>.
    /// </summary>
    private static bool MatchesPattern(string path, string pattern)
    {
        var fileName = path.Replace('\\', '/').Split('/').Last();
        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
        return Regex.IsMatch(fileName, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private sealed class RepoScanResultBuilder
    {
        public ProjectType? ProjectType { get; set; }
        public bool HasDotNet { get; set; }
        public bool HasTests { get; set; }
        public bool HasDockerfile { get; set; }
        public bool HasTerraform { get; set; }
        public bool HasBicep { get; set; }
        public List<string> Suggested { get; } = new();
        public List<string> DetectedFiles { get; } = new();

        public void Suggest(string templateId)
        {
            if (!Suggested.Contains(templateId))
                Suggested.Add(templateId);
        }

        public RepoScanResult Build() => new()
        {
            ProjectType = ProjectType ?? Enums.ProjectType.Unknown,
            HasDockerfile = HasDockerfile,
            HasTests = HasTests,
            HasTerraform = HasTerraform,
            HasBicep = HasBicep,
            SuggestedTemplates = Suggested.Distinct().ToList(),
            DetectedFiles = DetectedFiles
        };
    }
}
