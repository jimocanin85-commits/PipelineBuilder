using SimlifiezYaml.Core.Abstractions;
using SimlifiezYaml.Core.Models;

namespace SimlifiezYaml.Core.Services;

public sealed class YamlExplanationService : IYamlExplanationService
{
    private static readonly Dictionary<string, string> TaskExplanations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DotNetCoreCLI@2"] = "DotNetCoreCLI@2 runs .NET CLI commands such as restore, build, test, and publish for the selected project path.",
        ["PublishPipelineArtifact@1"] = "PublishPipelineArtifact@1 publishes files from the staging directory as a pipeline artifact for downstream deployment jobs.",
        ["DownloadPipelineArtifact@2"] = "DownloadPipelineArtifact@2 retrieves a previously published pipeline artifact into the agent workspace.",
        ["AzureKeyVault@2"] = "AzureKeyVault@2 downloads secrets from Azure Key Vault and maps them to pipeline variables before subsequent tasks run.",
        ["AzureAppServiceManage@0"] = "AzureAppServiceManage@0 manages Azure App Service including start, stop, and slot swap operations.",
        ["TerraformTaskV4@4"] = "TerraformTaskV4@4 executes Terraform init, plan, or apply using the configured Azure backend and service connection.",
        ["Docker@2"] = "Docker@2 builds and optionally pushes container images to a container registry.",
        ["NuGetCommand@2"] = "NuGetCommand@2 restores, packs, or pushes NuGet packages.",
        ["ArchiveFiles@2"] = "ArchiveFiles@2 compresses build output into a zip package for deployment.",
        ["PublishBuildArtifacts@1"] = "PublishBuildArtifacts@1 publishes build output to Azure DevOps build artifacts (classic).",
        ["DownloadBuildArtifacts@1"] = "DownloadBuildArtifacts@1 downloads build artifacts from the current or specified build.",
        ["AzureCLI@2"] = "AzureCLI@2 runs Azure CLI or PowerShell scripts authenticated via a service connection.",
        ["AzureResourceManagerTemplateDeployment@3"] = "Deploys an ARM template to a resource group using an Azure Resource Manager service connection."
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
