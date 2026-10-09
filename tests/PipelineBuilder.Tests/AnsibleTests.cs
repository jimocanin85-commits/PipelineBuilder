using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PipelineBuilder.Core.Abstractions;
using PipelineBuilder.Core.DependencyInjection;
using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Services;
using PipelineBuilder.Web.Components.Pages;
using PipelineBuilder.Web.State;
using Xunit;

namespace PipelineBuilder.Tests;

/// <summary>The pipeline runs the team's own Ansible playbook, once per environment, from a Linux agent.</summary>
public class AnsibleTests : BunitContext
{
    private readonly IPipelineGeneratorService _generator =
        new ServiceCollection().AddPipelineBuilderCore().BuildServiceProvider().GetRequiredService<IPipelineGeneratorService>();

    public AnsibleTests()
    {
        Services.AddPipelineBuilderCore();
        Services.AddScoped<WizardState>();
        // The page keeps the wizard in the browser's storage; here nothing is stored, and nothing is there.
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static PipelineDefinition Ansible()
    {
        var definition = WizardState.CreateDefault();
        Assert.True(new TemplateCatalogue().ApplyTo("ansible-playbook", definition));
        return definition;
    }

    private GeneratedPipeline Generate(PipelineDefinition definition) => _generator.Generate(definition);

    private static string DeployStage(GeneratedPipeline pipeline)
    {
        var yaml = pipeline.Yaml.ReplaceLineEndings("\n");
        return yaml[yaml.IndexOf("- stage: Deploy_", StringComparison.Ordinal)..];
    }

    [Fact]
    public void ThePlaybookRunsOncePerEnvironmentFromTheAgent()
    {
        var deploy = DeployStage(Generate(Ansible()));

        Assert.Contains("- checkout: self", deploy);
        Assert.Contains("ansible-playbook 'site.yml' \\\n", deploy);
        Assert.Contains("--inventory 'inventories/${{ environment }}' \\\n", deploy);
        Assert.Contains("--extra-vars 'environment_name=${{ environment }}'", deploy);
        Assert.Contains("--extra-vars 'build_id=$(Build.BuildId)'", deploy);
        Assert.Contains("--extra-vars 'package_path=$(Pipeline.Workspace)/drop'", deploy);
        Assert.Contains("Ansible is not installed on this agent", deploy);
        Assert.Contains("      environment: ${{ environment }}", deploy);
        Assert.DoesNotContain("resourceType: VirtualMachine", deploy);
        Assert.DoesNotContain("DownloadSecureFile@1", deploy);
        Assert.DoesNotContain("--private-key", deploy);
        Assert.DoesNotContain("pool:", deploy);
    }

    [Fact]
    public void RollbackIsLeftToThePlaybook()
    {
        var definition = Ansible();
        definition.Rollback.Enabled = true;

        var deploy = DeployStage(Generate(definition));

        Assert.DoesNotContain("failure:", deploy);
        Assert.DoesNotContain("Back up", deploy);
    }

    [Fact]
    public void AKeyAndALinuxPoolCanBeNamed()
    {
        var definition = Ansible();
        definition.Deployment.PlaybookPath = "ansible/deploy.yml";
        definition.Deployment.InventoryPath = "ansible/{Environment}.ini";
        definition.Deployment.AnsibleSshKeyFile = " ansible-key ";
        definition.Deployment.AgentPool = "linux-agents";

        var pipeline = Generate(definition);
        var deploy = DeployStage(pipeline);

        Assert.Contains("      pool:\n        name: 'linux-agents'", deploy);
        Assert.Contains("- task: DownloadSecureFile@1", deploy);
        Assert.Contains("name: ansibleKey", deploy);
        Assert.Contains("secureFile: 'ansible-key'", deploy);
        Assert.Contains("chmod 600 \"$(ansibleKey.secureFilePath)\"", deploy);
        Assert.Contains("ansible-playbook 'ansible/deploy.yml' \\\n", deploy);
        Assert.Contains("--inventory 'ansible/${{ environment }}.ini'", deploy);
        Assert.Contains("--private-key \"$(ansibleKey.secureFilePath)\"", deploy);

        var key = Assert.Single(pipeline.Requirements, n => n.Kind == RequirementKind.SecureFile);
        Assert.Equal("ansible-key", key.Name);
        Assert.Contains("Secure files", key.Where);
        Assert.DoesNotContain(pipeline.Requirements, n => n.Kind == RequirementKind.Variable);
    }

    [Fact]
    public void ADockerImageIsHandedToThePlaybookByName()
    {
        var definition = Ansible();
        definition.ProjectType = ProjectType.Docker;
        definition.Artifact = new ArtifactConfig { ArtifactType = ArtifactType.DockerImage, ArtifactName = "orders" };

        var deploy = DeployStage(Generate(definition));

        Assert.Contains("--extra-vars 'image=$(DOCKER_REGISTRY)/orders:$(Build.BuildId)'", deploy);
        Assert.DoesNotContain("package_path", deploy);
    }

    [Theory]
    [InlineData(HealthCheckType.HttpEndpoint, false)]
    [InlineData(HealthCheckType.WindowsService, true)]
    [InlineData(HealthCheckType.PortCheck, true)]
    public void ChecksThatLookAtTheMachineAreFlaggedBecauseTheyWouldLookAtTheAgent(HealthCheckType type, bool flagged)
    {
        var definition = Ansible();
        definition.HealthChecks = new[] { new HealthCheckConfig { Enabled = true, HealthCheckType = type, Url = "https://my-app.contoso.com/health" } };

        var findings = Generate(definition).ValidationResults;

        Assert.Equal(flagged, findings.Any(f => f.RuleId == "healthchecks.need-servers"));
    }

    [Fact]
    public void ChecksOnYourOwnServersAreNotFlagged()
    {
        var definition = WizardState.CreateDefault();
        definition.HealthChecks = new[] { new HealthCheckConfig { Enabled = true, HealthCheckType = HealthCheckType.WindowsService, ServiceName = "W3SVC" } };

        Assert.DoesNotContain(Generate(definition).ValidationResults, f => f.RuleId == "healthchecks.need-servers");
    }

    [Fact]
    public void TheWizardShowsThePlaybookFieldsAndLeavesOutWhatDoesNotApply()
    {
        var cut = Render<Home>();

        cut.Find("button[data-template='ansible-playbook']").Click();
        cut.Find($"button[data-step='{WizardStep.Target}']").Click();

        Assert.NotNull(cut.Find("#playbook"));
        Assert.NotNull(cut.Find("#inventory"));
        Assert.Contains("Linux agent", cut.Find(".wizard-content").TextContent);
        Assert.Empty(cut.FindAll("#run-from"));
        Assert.Empty(cut.FindAll("#rolling"));

        cut.Find("#ansible-key").Change("ansible-key");
        cut.Find($"button[data-step='{WizardStep.Safety}']").Click();
        Assert.Empty(cut.FindAll("#rollback-enabled"));
        Assert.Contains("Rollback is up to your playbook", cut.Find(".wizard-content").TextContent);

        cut.Find($"button[data-step='{WizardStep.Result}']").Click();
        Assert.Contains("ansible-key", cut.Find(".needs-list li[data-need='SecureFile']").TextContent);
        Assert.Contains("ansible-playbook 'site.yml'", cut.Find(".yaml-preview").TextContent);
    }
}
