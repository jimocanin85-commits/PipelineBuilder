using PipelineBuilder.Core.Abstractions;
using PipelineBuilder.Core.Models;

namespace PipelineBuilder.Core.Services;

public sealed class YamlExplanationService : IYamlExplanationService
{
    private static readonly Dictionary<string, string> TaskExplanations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DotNetCoreCLI@2"] = "DotNetCoreCLI@2 runs .NET CLI commands such as restore, build, test, and publish for the selected project path.",
        ["PublishPipelineArtifact@1"] = "PublishPipelineArtifact@1 publishes files from the staging directory as a pipeline artifact for downstream deployment jobs.",
        ["DownloadPipelineArtifact@2"] = "DownloadPipelineArtifact@2 retrieves a previously published pipeline artifact into the agent workspace.",
        ["AzureKeyVault@2"] = "AzureKeyVault@2 downloads secrets from Azure Key Vault and maps them to pipeline variables before subsequent tasks run.",
        ["Docker@2"] = "Docker@2 builds and optionally pushes container images to a container registry.",
        ["ArchiveFiles@2"] = "ArchiveFiles@2 compresses build output into a zip package for deployment.",
        ["PublishBuildArtifacts@1"] = "PublishBuildArtifacts@1 publishes build output to Azure DevOps build artifacts (classic).",
        ["DownloadBuildArtifacts@1"] = "DownloadBuildArtifacts@1 downloads build artifacts from the current or specified build.",
        ["IISWebAppDeploymentOnMachineGroup@0"] = "IISWebAppDeploymentOnMachineGroup@0 deploys a web package to an IIS website on the server the deployment job runs on.",
        ["KubernetesManifest@1"] = "KubernetesManifest@1 applies Kubernetes manifests and substitutes the container image built by this run.",
        ["Kubernetes@1"] = "Kubernetes@1 runs a kubectl command (here: rollout undo) against the cluster in the service connection.",
        ["NodeTool@0"] = "NodeTool@0 installs the requested Node.js version on the agent.",
        ["Npm@1"] = "Npm@1 runs npm commands such as ci, build and test.",
        ["CopyFiles@2"] = "CopyFiles@2 copies the build output into the artifact staging folder."
    };

    public IReadOnlyList<YamlBlockExplanation> ExplainYaml(string yaml)
    {
        var lines = yaml.Split('\n');
        var explanations = new List<YamlBlockExplanation>();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (!line.TrimStart().StartsWith("- task:", StringComparison.Ordinal))
                continue;

            var taskName = line.Split(':').LastOrDefault()?.Trim() ?? string.Empty;
            var end = Math.Min(i + 12, lines.Length - 1);
            var snippet = string.Join('\n', lines.Skip(i).Take(end - i + 1));
            explanations.Add(new YamlBlockExplanation
            {
                YamlSnippet = snippet,
                Explanation = ExplainTask(taskName),
                TaskName = taskName,
                LineStart = i + 1,
                LineEnd = end + 1
            });
        }
        return explanations;
    }

    private static string ExplainTask(string taskName) =>
        TaskExplanations.TryGetValue(taskName, out var explanation)
            ? explanation
            : $"Task {taskName} executes an Azure DevOps pipeline step. Review task documentation for input details.";
}
