using SimlifiezYaml.Core.Abstractions;
using SimlifiezYaml.Core.Enums;
using SimlifiezYaml.Core.Models;

namespace SimlifiezYaml.Core.Services;

public sealed class TemplateMarketplaceService : ITemplateMarketplaceService
{
    private static readonly IReadOnlyList<PipelineTemplate> Templates = new List<PipelineTemplate>
    {
        T("dotnet-web-app", ".NET Web Application", TemplateCategory.DotNet, "Build, test, and deploy ASP.NET Core to App Service",
            DeploymentTarget.Cloud, new[] { "projectPath", "webAppName" }, new[] { "Build", "Deploy_test", "Deploy_prod" }, TemplateRiskLevel.Low, "dotnet", "web"),
        T("iis-onprem", "IIS On-Premises", TemplateCategory.Iis, "Deploy to IIS with backup and health checks",
            DeploymentTarget.OnPrem, new[] { "sitePath", "appPool" }, new[] { "Build", "Deploy_test", "Deploy_prod" }, TemplateRiskLevel.Medium, "iis", "onprem"),
        T("docker-build-push", "Docker Build and Push", TemplateCategory.Docker, "Build container image and push to registry",
            DeploymentTarget.Cloud, new[] { "dockerfile", "registry" }, new[] { "Build" }, TemplateRiskLevel.Low, "docker"),
        T("terraform-azure", "Terraform Azure", TemplateCategory.Terraform, "Plan and apply Terraform for Azure resources",
            DeploymentTarget.Cloud, new[] { "workingDirectory", "serviceConnection" }, new[] { "Build", "Deploy_test" }, TemplateRiskLevel.High, "terraform", "iac"),
        T("winrm-deploy", "WinRM Deployment", TemplateCategory.WinRm, "Deploy via WinRM to Windows servers",
            DeploymentTarget.OnPrem, new[] { "targetHost", "credentialVariable" }, new[] { "Build", "Deploy_test", "Deploy_prod" }, TemplateRiskLevel.High, "winrm"),
        T("aks-deploy", "AKS Deployment", TemplateCategory.Aks, "Deploy manifests to Azure Kubernetes Service",
            DeploymentTarget.Cloud, new[] { "cluster", "namespace" }, new[] { "Build", "Deploy_test", "Deploy_prod" }, TemplateRiskLevel.High, "kubernetes", "aks"),
        T("hybrid-dotnet-docker", "Hybrid .NET + Docker", TemplateCategory.Hybrid, "Build .NET app, containerize, deploy to AKS or App Service",
            DeploymentTarget.Hybrid, new[] { "projectPath", "dockerfile" }, new[] { "Build", "Deploy_test", "Deploy_preprod", "Deploy_prod" }, TemplateRiskLevel.Medium, "hybrid")
    };

    public IReadOnlyList<PipelineTemplate> GetAllTemplates() => Templates;

    public PipelineTemplate? GetById(string id) =>
        Templates.FirstOrDefault(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public bool ApplyTo(string templateId, PipelineDefinition definition)
    {
        var template = GetById(templateId);
        if (template == null)
            return false;

        definition.TemplateId = template.Id;
        definition.DeploymentTarget = template.SupportedTargets.FirstOrDefault();

        // Environments come from the template's Deploy_* stages, e.g. Deploy_test -> test.
        var environments = template.GeneratedStages
            .Where(s => s.StartsWith("Deploy_", StringComparison.Ordinal))
            .Select(s => s["Deploy_".Length..])
            .ToList();
        if (environments.Count > 0)
            definition.Environments = environments;

        switch (template.Id)
        {
            case "dotnet-web-app":
                definition.ProjectType = ProjectType.DotNet;
                definition.Artifact.ArtifactType = ArtifactType.PipelineArtifact;
                definition.Deployment.Kind = DeploymentKind.AzureAppService;
                definition.DeploymentStrategy.StrategyType = DeploymentStrategyType.Standard;
                break;
            case "iis-onprem":
                definition.ProjectType = ProjectType.DotNet;
                definition.Artifact.ArtifactType = ArtifactType.PipelineArtifact;
                definition.Deployment.Kind = DeploymentKind.Iis;
                definition.Rollback.Enabled = true;
                break;
            case "docker-build-push":
                definition.ProjectType = ProjectType.Docker;
                definition.Artifact.ArtifactType = ArtifactType.DockerImage;
                definition.Deployment.Kind = DeploymentKind.DockerContainer;
                break;
            case "terraform-azure":
                definition.ProjectType = ProjectType.Terraform;
                definition.IaC ??= new InfrastructureAsCodeConfig { Tool = IaCTool.Terraform, WorkingDirectory = "infra" };
                definition.IaC.ServiceConnection = definition.AzureServiceConnection;
                definition.Deployment.Kind = DeploymentKind.Custom;
                break;
            case "winrm-deploy":
                // Deploys through agents registered in the environment rather than remote WinRM calls.
                definition.ProjectType = ProjectType.DotNet;
                definition.Deployment.Kind = DeploymentKind.WindowsService;
                definition.Rollback.Enabled = true;
                break;
            case "aks-deploy":
                definition.ProjectType = ProjectType.Docker;
                definition.Artifact.ArtifactType = ArtifactType.DockerImage;
                definition.Deployment.Kind = DeploymentKind.Custom;
                definition.Deployment.CustomScript ??= "kubectl apply -f manifests/ --namespace $(K8S_NAMESPACE)";
                break;
            case "hybrid-dotnet-docker":
                definition.ProjectType = ProjectType.DotNet;
                definition.Artifact.ArtifactType = ArtifactType.DockerImage;
                definition.Deployment.Kind = DeploymentKind.DockerContainer;
                break;
        }

        return true;
    }

    private static PipelineTemplate T(
        string id, string name, TemplateCategory category, string description,
        DeploymentTarget target, string[] inputs, string[] stages,
        TemplateRiskLevel risk, params string[] tags) => new()
    {
        Id = id,
        Name = name,
        Category = category,
        Description = description,
        SupportedTargets = new[] { target },
        RequiredInputs = inputs,
        GeneratedStages = stages,
        RiskLevel = risk,
        Tags = tags
    };
}
