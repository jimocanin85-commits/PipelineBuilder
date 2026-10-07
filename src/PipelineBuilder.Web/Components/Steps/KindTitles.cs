using PipelineBuilder.Core.Enums;

namespace PipelineBuilder.Web.Components.Steps;

/// <summary>What each kind of deployment is called in the wizard.</summary>
public static class KindTitles
{
    public static string For(DeploymentKind kind) => kind switch
    {
        DeploymentKind.Iis => "IIS website",
        DeploymentKind.WindowsService => "Windows service",
        DeploymentKind.LinuxService => "Linux service",
        DeploymentKind.FileShare => "Files",
        DeploymentKind.DockerContainer => "Docker container",
        DeploymentKind.Kubernetes => "Kubernetes",
        DeploymentKind.Ansible => "Ansible playbook",
        _ => "Your own script"
    };
}
