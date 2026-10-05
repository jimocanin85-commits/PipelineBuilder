using Microsoft.Extensions.DependencyInjection;
using PipelineBuilder.Core.Abstractions;
using PipelineBuilder.Core.DependencyInjection;
using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Web.State;
using Xunit;

namespace PipelineBuilder.Tests;

/// <summary>The list of what a pipeline needs in Azure DevOps: environments, connections, variables.</summary>
public class PipelineRequirementsTests
{
    private readonly IPipelineGeneratorService _generator =
        new ServiceCollection().AddPipelineBuilderCore().BuildServiceProvider().GetRequiredService<IPipelineGeneratorService>();

    private IReadOnlyList<PipelineRequirement> Needs(PipelineDefinition definition) => _generator.Generate(definition).Requirements;

    private static IEnumerable<string> Names(IEnumerable<PipelineRequirement> needs, RequirementKind kind) =>
        needs.Where(n => n.Kind == kind).Select(n => n.Name);

    [Fact]
    public void TheDefaultPipelineOnlyNeedsItsEnvironments()
    {
        var needs = Needs(WizardState.CreateDefault());

        Assert.Equal(new[] { "test", "preprod", "prod" }, Names(needs, RequirementKind.Environment));
        Assert.All(needs, n => Assert.Equal(RequirementKind.Environment, n.Kind));
        Assert.Contains("servers registered", needs[0].Purpose);
        Assert.DoesNotContain("approval", needs[0].Purpose);
        Assert.Contains("approval", needs[2].Purpose);
    }

    [Fact]
    public void EveryNeedSaysWhereItIsCreated()
    {
        var definition = WizardState.CreateDefault();
        definition.VariableGroups = new[] { new VariableGroupConfig { Name = "vg-orders" } };
        definition.Artifact = new ArtifactConfig { ArtifactType = ArtifactType.DockerImage, ArtifactName = "orders", ContainerRegistryConnection = "our-registry" };
        definition.Deployment = new DeploymentConfig { Kind = DeploymentKind.DockerContainer, ServerOs = ServerOs.Linux };

        var needs = Needs(definition);

        Assert.All(needs, n => Assert.False(string.IsNullOrWhiteSpace(n.Where)));
        Assert.EndsWith("Virtual machines", needs.First(n => n.Kind == RequirementKind.Environment).Where);
        Assert.Contains("Service connections", needs.Single(n => n.Kind == RequirementKind.ServiceConnection).Where);
        Assert.Contains("Library", needs.Single(n => n.Kind == RequirementKind.VariableGroup).Where);
        Assert.Contains("Variables", needs.First(n => n.Kind == RequirementKind.Variable).Where);

        definition.Deployment = new DeploymentConfig { Kind = DeploymentKind.Kubernetes };
        Assert.EndsWith("New environment", Needs(definition).First(n => n.Kind == RequirementKind.Environment).Where);
    }

    [Fact]
    public void EmptyFieldsBecomeVariables()
    {
        var definition = WizardState.CreateDefault();
        definition.Deployment = new DeploymentConfig { Kind = DeploymentKind.LinuxService };

        var needs = Needs(definition);

        Assert.Equal(new[] { "DEPLOY_PATH", "SERVICE_NAME" }, Names(needs, RequirementKind.Variable));
        Assert.Equal("Name of the service", needs.Single(n => n.Name == "SERVICE_NAME").Purpose);
        Assert.DoesNotContain(needs, n => n.Name is "BuildConfiguration" or "Build.BuildId");
    }

    [Fact]
    public void WebhooksAndPasswordsAreSecrets()
    {
        var definition = WizardState.CreateDefault();
        definition.Notifications = new[]
        {
            new NotificationConfig { NotificationType = NotificationType.TeamsWebhook, NotifyOnFailure = true },
            new NotificationConfig { NotificationType = NotificationType.Email, NotifyOnFailure = true, EmailRecipients = new[] { "ops@contoso.com" } }
        };

        var needs = Needs(definition);

        Assert.True(needs.Single(n => n.Name == "TEAMS_WEBHOOK_URL").IsSecret);
        Assert.True(needs.Single(n => n.Name == "SMTP_PASSWORD").IsSecret);
        Assert.False(needs.Single(n => n.Name == "SMTP_HOST").IsSecret);
    }

    [Fact]
    public void ConnectionsEnteredByNameAreListedAsConnections()
    {
        var definition = WizardState.CreateDefault();
        definition.Artifact = new ArtifactConfig { ArtifactType = ArtifactType.DockerImage, ArtifactName = "orders", ContainerRegistryConnection = "our-registry" };
        definition.Deployment = new DeploymentConfig { Kind = DeploymentKind.Kubernetes, KubernetesServiceConnection = "our-cluster", KubernetesNamespace = "orders" };
        definition.KeyVault = new KeyVaultConfig { KeyVaultName = "kv-orders", ServiceConnection = "our-azure" };

        var needs = Needs(definition);

        Assert.Equal(new[] { "our-registry", "our-cluster", "our-azure" }, Names(needs, RequirementKind.ServiceConnection));
        Assert.DoesNotContain(needs, n => n.Name is "DOCKER_SERVICE_CONNECTION" or "K8S_SERVICE_CONNECTION" or "K8S_NAMESPACE");
        Assert.Contains(needs, n => n.Name == "DOCKER_REGISTRY");
        Assert.Equal("Environment", needs.First(n => n.Kind == RequirementKind.Environment).Purpose);
    }

    [Fact]
    public void ConnectionsLeftAsVariablesAreListedAsVariables()
    {
        var definition = WizardState.CreateDefault();
        definition.Artifact = new ArtifactConfig { ArtifactType = ArtifactType.DockerImage, ArtifactName = "orders" };
        definition.Deployment = new DeploymentConfig { Kind = DeploymentKind.DockerContainer, ServerOs = ServerOs.Linux };

        var needs = Needs(definition);

        Assert.Empty(Names(needs, RequirementKind.ServiceConnection));
        Assert.Equal(new[] { "CONTAINER_NAME", "DOCKER_REGISTRY", "DOCKER_SERVICE_CONNECTION" }, Names(needs, RequirementKind.Variable));
    }

    [Fact]
    public void VariableGroupsAndOwnVariablesAreListed()
    {
        var definition = WizardState.CreateDefault();
        definition.VariableGroups = new[] { new VariableGroupConfig { Name = "vg-orders", ContainsSecrets = true } };
        definition.Deployment = new DeploymentConfig { Kind = DeploymentKind.Custom, CustomScript = "Write-Host '$(ORDERS_API_TOKEN) $(Build.BuildId)'" };

        var needs = Needs(definition);

        var group = needs.Single(n => n.Kind == RequirementKind.VariableGroup);
        Assert.Equal("vg-orders", group.Name);
        Assert.True(group.IsSecret);
        var own = needs.Single(n => n.Kind == RequirementKind.Variable);
        Assert.Equal("ORDERS_API_TOKEN", own.Name);
        Assert.True(own.IsSecret);
        Assert.Equal("Variable used by your settings", own.Purpose);
    }
}
