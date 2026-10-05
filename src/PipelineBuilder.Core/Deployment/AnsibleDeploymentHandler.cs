using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Yaml;

namespace PipelineBuilder.Core.Deployment;

/// <summary>
/// Runs the team's own Ansible playbook, once per environment, from a Linux agent. The playbook
/// decides what happens on the servers, so there is no built-in backup or rollback. It is given
/// three values: <c>environment_name</c>, <c>build_id</c> and <c>package_path</c> (or <c>image</c>
/// for a Docker image).
/// </summary>
public sealed class AnsibleDeploymentHandler : DeploymentKindHandler
{
    /// <summary>Step name of the downloaded SSH key; its path is <c>$(ansibleKey.secureFilePath)</c>.</summary>
    private const string KeyStep = "ansibleKey";

    public override DeploymentKind Kind => DeploymentKind.Ansible;
    public override RollbackTarget? RollbackTarget => null;
    public override bool RunsOnServers => false;
    public override bool NeedsRepositoryCheckout => true;
    public override bool SupportsRollback => false;

    public override ScriptShell Shell(DeploymentConfig deployment) => ScriptShell.Bash;

    public override IReadOnlyList<string> GenerateDeploySteps(PipelineDefinition definition, string environment, string packagePath)
    {
        var deployment = definition.Deployment;
        var hasKey = !string.IsNullOrWhiteSpace(deployment.AnsibleSshKeyFile);
        var inventory = deployment.InventoryPathOrDefault.Replace("{environment}", environment, StringComparison.OrdinalIgnoreCase);
        var build = definition.Artifact.ArtifactType == ArtifactType.DockerImage
            ? $"image=$(DOCKER_REGISTRY)/{definition.Artifact.ArtifactName}:$(Build.BuildId)"
            : $"package_path={packagePath}";

        var arguments = new List<string> { $"--inventory {YamlBuilder.BashLiteral(inventory)}" };
        if (hasKey)
            arguments.Add($"--private-key \"$({KeyStep}.secureFilePath)\"");
        arguments.Add($"--extra-vars {YamlBuilder.BashLiteral($"environment_name={environment}")}");
        arguments.Add("--extra-vars 'build_id=$(Build.BuildId)'");
        arguments.Add($"--extra-vars {YamlBuilder.BashLiteral(build)}");

        // ssh refuses a key file that other users can read.
        var protectKey = hasKey ? $"chmod 600 \"$({KeyStep}.secureFilePath)\"\n" : string.Empty;

        var steps = new List<string>();
        if (hasKey)
        {
            // A secure file is downloaded for this job only and removed when the job ends.
            steps.Add($"""
    - task: DownloadSecureFile@1
      name: {KeyStep}
      displayName: 'Download the SSH key'
      inputs:
        secureFile: {YamlBuilder.YamlString(deployment.AnsibleSshKeyFile!.Trim())}
""");
        }

        steps.Add(YamlBuilder.BashStep($$"""
{{BashSnippets.Strict}}
if ! command -v ansible-playbook >/dev/null 2>&1; then
  echo "##vso[task.logissue type=error]Ansible is not installed on this agent. Ansible needs a Linux agent."
  exit 1
fi
{{protectKey}}ansible-playbook {{YamlBuilder.BashLiteral(deployment.PlaybookPathOrDefault)}} \
  {{string.Join(" \\\n  ", arguments)}}
""", $"Run Ansible playbook ({environment})"));
        return steps;
    }
}
