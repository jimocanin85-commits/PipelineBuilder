using Microsoft.Extensions.DependencyInjection;
using SimlifiezYaml.Core.Abstractions;
using SimlifiezYaml.Core.DependencyInjection;
using SimlifiezYaml.Core.Enums;
using SimlifiezYaml.Core.Models;
using Xunit;
using YamlDotNet.Serialization;

namespace SimlifiezYaml.Tests;

public class NodeKubernetesEmailTests
{
    private readonly IServiceProvider _services = new ServiceCollection().AddSimlifiezYamlCore().BuildServiceProvider();
    private IPipelineGeneratorService Generator => _services.GetRequiredService<IPipelineGeneratorService>();

    private static PipelineDefinition Minimal() => new()
    {
        Name = "app",
        Environments = new[] { "test", "prod" },
        Governance = new GovernancePolicyConfig { RequireHealthCheck = false }
    };

    [Fact]
    public void NodeProjectsInstallBuildTestAndPackageTheOutput()
    {
        var definition = Minimal();
        definition.ProjectType = ProjectType.Node;
        definition.NodeVersion = "22.x";
        definition.NodeOutputFolder = "build";

        var steps = BuildSteps(Generator.Generate(definition).Yaml);

        Assert.Equal(new[] { "NodeTool@0", "Npm@1", "Npm@1", "Npm@1", "CopyFiles@2", "PublishPipelineArtifact@1" },
            steps.Select(s => s.GetValueOrDefault("task") as string));
        Assert.Equal("22.x", (string)Inputs(steps[0])["versionSpec"]);
        Assert.Equal("ci", (string)Inputs(steps[1])["command"]);
        Assert.Equal("run build --if-present", (string)Inputs(steps[2])["customCommand"]);
        Assert.Equal("run test --if-present", (string)Inputs(steps[3])["customCommand"]);
        Assert.Equal("true", (string)Assert.IsType<Dictionary<object, object>>(steps[3]["env"])["CI"]);
        Assert.Equal("build", (string)Inputs(steps[4])["SourceFolder"]);
    }

    [Fact]
    public void KubernetesDeploysManifestsWithTheBuiltImageAndRollsBack()
    {
        var definition = Minimal();
        definition.DeploymentTarget = DeploymentTarget.Cloud;
        definition.Artifact = new ArtifactConfig { ArtifactType = ArtifactType.DockerImage, ArtifactName = "orders" };
        definition.Deployment = new DeploymentConfig { Kind = DeploymentKind.Kubernetes, KubernetesNamespace = "orders", KubernetesDeploymentName = "orders-api" };
        definition.Rollback = new RollbackConfig { Enabled = true };

        var result = Generator.Generate(definition);
        var job = DeployJob(result.Yaml, "Deploy_prod");
        var steps = HookSteps(job, "deploy");

        Assert.Equal("prod", (string)job["environment"]);
        Assert.Contains(steps, s => (s.GetValueOrDefault("checkout") as string) == "self");
        var deploy = Assert.Single(steps, s => s.GetValueOrDefault("task") as string == "KubernetesManifest@1");
        Assert.Equal("orders", (string)Inputs(deploy)["namespace"]);
        Assert.Equal("$(DOCKER_REGISTRY)/orders:$(Build.BuildId)", (string)Inputs(deploy)["containers"]);

        var rollback = Assert.Single(HookSteps(job, "failure"));
        Assert.Equal("Kubernetes@1", (string)rollback["task"]);
        Assert.Equal("undo deployment/orders-api", (string)Inputs(rollback)["arguments"]);
        Assert.DoesNotContain(result.ValidationResults, v => v.Message.Contains("container image"));
    }

    [Fact]
    public void KubernetesNeverTargetsServerResources()
    {
        var definition = Minimal();
        definition.DeploymentTarget = DeploymentTarget.OnPrem;
        definition.Artifact = new ArtifactConfig { ArtifactType = ArtifactType.DockerImage, ArtifactName = "orders" };
        definition.Deployment = new DeploymentConfig { Kind = DeploymentKind.Kubernetes };

        Assert.DoesNotContain("resourceType: VirtualMachine", Generator.Generate(definition).Yaml);
    }

    [Fact]
    public void KubernetesWithoutAnImageIsFlagged()
    {
        var definition = Minimal();
        definition.Deployment = new DeploymentConfig { Kind = DeploymentKind.Kubernetes };

        Assert.Contains(Generator.Generate(definition).ValidationResults, v => v.Message.Contains("container image"));
    }

    [Fact]
    public void EmailIsSentOverSmtpWithTheSecretMappedIn()
    {
        var definition = Minimal();
        definition.Notifications = new[]
        {
            new NotificationConfig { NotificationType = NotificationType.Email, EmailRecipients = new[] { "ops@contoso.com", "o'brien@contoso.com" }, NotifyOnFailure = true }
        };

        var root = Parse(Generator.Generate(definition).Yaml);
        var stage = Assert.Single(Stages(root), s => (string)s["stage"] == "Notify_Failure");
        var step = Assert.Single(Steps(Assert.Single(Jobs(stage))));
        var script = (string)step["powershell"];

        Assert.Contains("System.Net.Mail.SmtpClient", script);
        Assert.Contains("$recipients = @('ops@contoso.com', 'o''brien@contoso.com')", script);
        Assert.Contains("SMTP_HOST is not set", script);
        Assert.Equal("$(SMTP_PASSWORD)", (string)Assert.IsType<Dictionary<object, object>>(step["env"])["SMTP_PASSWORD"]);
    }

    [Fact]
    public void EmailWithoutRecipientsIsFlagged()
    {
        var definition = Minimal();
        definition.Notifications = new[] { new NotificationConfig { NotificationType = NotificationType.Email, NotifyOnFailure = true } };

        Assert.Contains(Generator.Generate(definition).ValidationResults, v => v.Message.Contains("no recipients"));
    }

    [Fact]
    public void PackageJsonSuggestsTheNodeTemplate()
    {
        var scan = _services.GetRequiredService<IRepoScannerService>().ScanFileList(new[] { "package.json", "src/index.ts" });

        Assert.Equal(ProjectType.Node, scan.ProjectType);
        Assert.Contains("node-web-app", scan.SuggestedTemplates);
    }

    private static Dictionary<object, object> Parse(string yaml) =>
        Assert.IsType<Dictionary<object, object>>(new DeserializerBuilder().Build().Deserialize<object>(yaml));

    private static List<Dictionary<object, object>> Stages(Dictionary<object, object> root) =>
        ((List<object>)root["stages"]).Cast<Dictionary<object, object>>().ToList();

    private static List<Dictionary<object, object>> Jobs(Dictionary<object, object> stage) =>
        ((List<object>)stage["jobs"]).Cast<Dictionary<object, object>>().ToList();

    private static List<Dictionary<object, object>> Steps(Dictionary<object, object> node) =>
        ((List<object>)node["steps"]).Cast<Dictionary<object, object>>().ToList();

    private static Dictionary<object, object> Inputs(Dictionary<object, object> step) =>
        Assert.IsType<Dictionary<object, object>>(step["inputs"]);

    private static List<Dictionary<object, object>> BuildSteps(string yaml) =>
        Steps(Assert.Single(Jobs(Assert.Single(Stages(Parse(yaml)), s => (string)s["stage"] == "Build"))));

    private static Dictionary<object, object> DeployJob(string yaml, string stage) =>
        Assert.Single(Jobs(Assert.Single(Stages(Parse(yaml)), s => (string)s["stage"] == stage)),
            j => ((string?)j.GetValueOrDefault("deployment"))?.StartsWith("DeployTo") == true);

    private static List<Dictionary<object, object>> HookSteps(Dictionary<object, object> job, string hook)
    {
        var runOnce = (Dictionary<object, object>)((Dictionary<object, object>)job["strategy"])["runOnce"];
        var node = hook == "deploy"
            ? (Dictionary<object, object>)runOnce["deploy"]
            : (Dictionary<object, object>)((Dictionary<object, object>)runOnce["on"])[hook];
        return Steps(node);
    }
}
