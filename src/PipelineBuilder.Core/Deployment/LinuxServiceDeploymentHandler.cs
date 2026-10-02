using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Yaml;

namespace PipelineBuilder.Core.Deployment;

/// <summary>
/// Copies the package to a folder on the Linux servers and restarts its systemd service.
/// The agent's user needs write access to the folder and may run <c>systemctl</c> through sudo.
/// </summary>
public sealed class LinuxServiceDeploymentHandler : DeploymentKindHandler
{
    public override DeploymentKind Kind => DeploymentKind.LinuxService;
    public override RollbackTarget? RollbackTarget => Enums.RollbackTarget.LinuxService;

    public override ScriptShell Shell(DeploymentConfig deployment) => ScriptShell.Bash;

    public override IReadOnlyList<string> GenerateDeploySteps(PipelineDefinition definition, string environment, string packagePath)
    {
        var deployment = definition.Deployment;
        return new[]
        {
            YamlBuilder.BashStep($$"""
{{BashSnippets.Strict}}
service={{YamlBuilder.BashLiteral(deployment.ServiceNameOrDefault)}}
target={{YamlBuilder.BashLiteral(deployment.TargetPathOrDefault)}}
package={{YamlBuilder.BashLiteral(packagePath)}}
{{BashSnippets.SyncFolderFunction}}
{{BashSnippets.AsRootFunction}}
{{BashSnippets.ResolvePackageSource}}
as_root systemctl stop "$service" || echo "Service $service was not running"
sync_folder "$source" "$target"
as_root systemctl start "$service"
echo "Deployed to $target and started $service"
""", $"Deploy Linux service ({environment})")
        };
    }

    public override IReadOnlyList<string> GenerateBackupSteps(RollbackConfig config, DeploymentConfig deployment, string environment) => new[]
    {
        YamlBuilder.BashStep($$"""
{{RollbackScripts.BashHeader(config, environment)}}
target={{YamlBuilder.BashLiteral(deployment.TargetPathOrDefault)}}
{{BashSnippets.SyncFolderFunction}}
if [ -d "$target" ]; then
  sync_folder "$target" "$backup"
  echo "Backed up $target to $backup"
else
  echo "Nothing to back up: $target does not exist yet"
fi
{{RollbackScripts.BashPrune(config)}}
""", "Back up deployment folder before deploy")
    };

    public override IReadOnlyList<string> GenerateRollbackSteps(RollbackConfig config, DeploymentConfig deployment, string environment) => new[]
    {
        YamlBuilder.BashStep($$"""
{{RollbackScripts.BashHeader(config, environment)}}
{{RollbackScripts.BashRequireBackup}}
service={{YamlBuilder.BashLiteral(deployment.ServiceNameOrDefault)}}
target={{YamlBuilder.BashLiteral(deployment.TargetPathOrDefault)}}
{{BashSnippets.SyncFolderFunction}}
{{BashSnippets.AsRootFunction}}
as_root systemctl stop "$service" || true
sync_folder "$backup" "$target"
as_root systemctl start "$service"
echo "Restored $target from $backup and restarted $service"
""", "Roll back Linux service")
    };
}
