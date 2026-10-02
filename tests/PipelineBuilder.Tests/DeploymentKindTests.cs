using Microsoft.Extensions.DependencyInjection;
using PipelineBuilder.Core.Abstractions;
using PipelineBuilder.Core.DependencyInjection;
using PipelineBuilder.Core.Deployment;
using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Yaml;
using PipelineBuilder.Web.State;
using Xunit;

namespace PipelineBuilder.Tests;

/// <summary>The deployment kind handlers and the registry that picks one (architecture goal M5).</summary>
public class DeploymentKindTests
{
    private readonly DeploymentKindRegistry _kinds = DeploymentKindRegistry.CreateDefault();

    [Fact]
    public void EveryDeploymentKindHasItsOwnHandler()
    {
        foreach (var kind in Enum.GetValues<DeploymentKind>())
            Assert.Equal(kind, _kinds.For(kind).Kind);
    }

    [Fact]
    public void EveryRollbackTargetHasAHandler()
    {
        foreach (var target in Enum.GetValues<RollbackTarget>())
            Assert.Contains(_kinds.All, h => h.RollbackTarget == target);
    }

    [Fact]
    public void AnUnknownKindFallsBackToCustom()
    {
        Assert.Equal(DeploymentKind.Custom, _kinds.For((DeploymentKind)99).Kind);
    }

    [Fact]
    public void TheRegistryNeedsACustomHandler()
    {
        Assert.Throws<ArgumentException>(() => new DeploymentKindRegistry(new IDeploymentKindHandler[] { new IisDeploymentHandler() }));
    }

    [Theory]
    [InlineData(DeploymentKind.Iis, true)]
    [InlineData(DeploymentKind.WindowsService, true)]
    [InlineData(DeploymentKind.DockerContainer, true)]
    [InlineData(DeploymentKind.Custom, true)]
    [InlineData(DeploymentKind.Kubernetes, false)]
    public void ServerResourcesFollowTheKind(DeploymentKind kind, bool expected)
    {
        var definition = new PipelineDefinition { Deployment = new DeploymentConfig { Kind = kind } };

        Assert.Equal(expected, _kinds.UsesServerResources(definition));
    }

    [Fact]
    public void CustomDeploymentsBackUpAndRollBackWithTheChosenTarget()
    {
        var definition = new PipelineDefinition
        {
            Deployment = new DeploymentConfig { Kind = DeploymentKind.Custom, CustomScript = "Write-Host deploy" },
            Rollback = new RollbackConfig { Enabled = true, Target = RollbackTarget.WindowsService }
        };

        Assert.Contains("Back up deployment folder before deploy", string.Join("\n", _kinds.GenerateBackupSteps(definition, "test")));
        Assert.Contains("Roll back Windows service", string.Join("\n", _kinds.GenerateRollbackSteps(definition, "test")));
    }

    [Fact]
    public void ACustomRollbackScriptReplacesTheBuiltInRollback()
    {
        var definition = new PipelineDefinition
        {
            Deployment = new DeploymentConfig { Kind = DeploymentKind.Iis },
            Rollback = new RollbackConfig { Enabled = true, RollbackScript = "scripts/rollback.cmd" }
        };

        var steps = string.Join("\n", _kinds.GenerateRollbackSteps(definition, "test"));

        Assert.Contains("Execute custom rollback script", steps);
        Assert.DoesNotContain("Roll back IIS site", steps);
    }

    [Fact]
    public void NoBackupOrRollbackWhenRollbackIsOff()
    {
        var definition = new PipelineDefinition
        {
            Deployment = new DeploymentConfig { Kind = DeploymentKind.Iis },
            Rollback = new RollbackConfig { Enabled = false }
        };

        Assert.Empty(_kinds.GenerateBackupSteps(definition, "test"));
        Assert.Empty(_kinds.GenerateRollbackSteps(definition, "test"));
    }

    [Fact]
    public void AHandlerRegisteredAfterTheBuiltInsReplacesTheBuiltInOne()
    {
        var services = new ServiceCollection().AddPipelineBuilderCore();
        services.AddSingleton<IDeploymentKindHandler>(new OwnIisHandler());
        var provider = services.BuildServiceProvider();

        Assert.IsType<OwnIisHandler>(provider.GetRequiredService<IDeploymentKinds>().For(DeploymentKind.Iis));

        var yaml = provider.GetRequiredService<IPipelineGeneratorService>().Generate(WizardState.CreateDefault()).Yaml;
        Assert.Contains("Our own IIS deploy", yaml);
        Assert.DoesNotContain("IISWebAppDeploymentOnMachineGroup@0", yaml);
    }

    [Fact]
    public void WithoutAHandlerForTheChosenTargetCustomHasNoBackupOrRollback()
    {
        var onlyCustom = new DeploymentKindRegistry(new IDeploymentKindHandler[] { new CustomDeploymentHandler() });
        var definition = new PipelineDefinition
        {
            Deployment = new DeploymentConfig { Kind = DeploymentKind.Custom },
            Rollback = new RollbackConfig { Enabled = true, Target = RollbackTarget.Iis }
        };

        Assert.Empty(onlyCustom.GenerateBackupSteps(definition, "test"));
        Assert.Empty(onlyCustom.GenerateRollbackSteps(definition, "test"));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("Write-Host deploy", false)]
    public void CustomWarnsOnlyWithoutAScript(string? script, bool warns)
    {
        var definition = new PipelineDefinition { Deployment = new DeploymentConfig { Kind = DeploymentKind.Custom, CustomScript = script } };
        var handler = _kinds.For(DeploymentKind.Custom);

        Assert.Equal(warns, handler.Validate(definition).Any(v => v.Severity == ValidationSeverity.Warning));
        var deploy = string.Join("\n", handler.GenerateDeploySteps(definition, "test", "$(Pipeline.Workspace)/drop"));
        Assert.Equal(warns, deploy.Contains("No deployment kind selected", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(ArtifactType.DockerImage, false)]
    [InlineData(ArtifactType.ZipPackage, true)]
    public void KubernetesWarnsWithoutADockerImage(ArtifactType artifact, bool warns)
    {
        var definition = new PipelineDefinition
        {
            Deployment = new DeploymentConfig { Kind = DeploymentKind.Kubernetes },
            Artifact = new ArtifactConfig { ArtifactType = artifact }
        };

        Assert.Equal(warns, _kinds.For(DeploymentKind.Kubernetes).Validate(definition).Any());
    }

    [Fact]
    public void TheBaseHandlerHasNoBackupRollbackOrChecks()
    {
        var handler = new OwnIisHandler();
        var definition = WizardState.CreateDefault();

        Assert.Empty(handler.GenerateBackupSteps(definition.Rollback, definition.Deployment, "test"));
        Assert.Empty(handler.GenerateRollbackSteps(definition.Rollback, definition.Deployment, "test"));
        Assert.Empty(handler.Validate(definition));
        Assert.True(handler.RunsOnServers);
        Assert.False(handler.NeedsRepositoryCheckout);
    }

    private sealed class OwnIisHandler : DeploymentKindHandler
    {
        public override DeploymentKind Kind => DeploymentKind.Iis;
        public override RollbackTarget? RollbackTarget => Core.Enums.RollbackTarget.Iis;

        public override IReadOnlyList<string> GenerateDeploySteps(PipelineDefinition definition, string environment, string packagePath) =>
            new[] { YamlBuilder.PowerShellStep("Write-Host 'deploying'", "Our own IIS deploy") };
    }
}
