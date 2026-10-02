using Microsoft.Extensions.DependencyInjection;
using PipelineBuilder.Core.Abstractions;
using PipelineBuilder.Core.DependencyInjection;
using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Yaml;
using Xunit;

namespace PipelineBuilder.Tests;

/// <summary>Deployments to Linux servers (bash, systemd) and Docker on either operating system.</summary>
public class LinuxAndDockerTests
{
    private readonly IPipelineGeneratorService _generator =
        new ServiceCollection().AddPipelineBuilderCore().BuildServiceProvider().GetRequiredService<IPipelineGeneratorService>();

    private static PipelineDefinition Minimal() => new()
    {
        Name = "orders",
        Environments = new[] { "test", "prod" }
    };

    [Fact]
    public void LinuxServiceIsDeployedWithBashAndSystemd()
    {
        var definition = Minimal();
        definition.Deployment = new DeploymentConfig { Kind = DeploymentKind.LinuxService, ServiceName = "orders", TargetPath = "/opt/orders" };
        definition.Rollback = new RollbackConfig { Enabled = true };

        var yaml = _generator.Generate(definition).Yaml;

        Assert.Contains("resourceType: VirtualMachine", yaml);
        Assert.Contains("- bash: |", yaml);
        Assert.Contains("service='orders'", yaml);
        Assert.Contains("target='/opt/orders'", yaml);
        Assert.Contains("as_root systemctl start \"$service\"", yaml);
        Assert.Contains("sync_folder \"$source\" \"$target\"", yaml);
        Assert.Contains("envRoot='/var/backups/pipelinebuilder/${{ environment }}'", yaml);
        Assert.Contains("displayName: 'Roll back Linux service'", yaml);
        Assert.DoesNotContain("robocopy", yaml);
        Assert.DoesNotContain(@"D:\backups", yaml);
    }

    [Fact]
    public void LinuxScriptsNeverUseDollarParenthesisForCommands()
    {
        // Azure DevOps reads $(name) as a pipeline variable, so bash command substitution uses backticks.
        var definition = Minimal();
        definition.Deployment = new DeploymentConfig { Kind = DeploymentKind.LinuxService, ServiceName = "orders", TargetPath = "/opt/orders" };
        definition.Rollback = new RollbackConfig { Enabled = true };
        definition.HealthChecks = new[] { new HealthCheckConfig { Enabled = true, Url = "https://orders-{environment}.contoso.com/health" } };

        var yaml = _generator.Generate(definition).Yaml;

        var macros = System.Text.RegularExpressions.Regex.Matches(yaml, @"(?<!\{)\$\(([^)]*)\)").Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.All(macros, name => Assert.Matches(@"^[A-Za-z][A-Za-z0-9_.]*$", name));
    }

    [Fact]
    public void HealthChecksOnLinuxUseCurlAndSystemctl()
    {
        var definition = Minimal();
        definition.Deployment = new DeploymentConfig { Kind = DeploymentKind.LinuxService };
        definition.HealthChecks = new[]
        {
            new HealthCheckConfig { Enabled = true, HealthCheckType = HealthCheckType.HttpEndpoint, Url = "https://orders-{environment}.contoso.com/health" },
            new HealthCheckConfig { Enabled = true, HealthCheckType = HealthCheckType.WindowsService, ServiceName = "orders" },
            new HealthCheckConfig { Enabled = true, HealthCheckType = HealthCheckType.PortCheck, Port = 8080 },
            new HealthCheckConfig { Enabled = true, HealthCheckType = HealthCheckType.CustomPowerShell, CustomScript = "test -f /opt/orders/ready" },
            new HealthCheckConfig { Enabled = true, HealthCheckType = HealthCheckType.IisAppPool }
        };

        var result = _generator.Generate(definition);

        Assert.Contains("uri='https://orders-${{ environment }}.contoso.com/health'", result.Yaml);
        Assert.Contains("curl -s -o /dev/null", result.Yaml);
        Assert.Contains("systemctl is-active --quiet \"$service\"", result.Yaml);
        Assert.Contains("/dev/tcp/localhost/$port", result.Yaml);
        Assert.Contains("test -f /opt/orders/ready", result.Yaml);
        Assert.DoesNotContain("Invoke-WebRequest", result.Yaml);
        Assert.DoesNotContain("WebAdministration", result.Yaml);
        Assert.Contains(result.ValidationResults, v => v.RuleId == "healthchecks.windows-only");
    }

    [Fact]
    public void DockerOnLinuxLogsInAndRunsTheContainerWithPortsAndEnvironment()
    {
        var definition = Minimal();
        definition.Artifact = new ArtifactConfig { ArtifactType = ArtifactType.DockerImage, ArtifactName = "orders", ContainerRegistryConnection = "our-registry" };
        definition.Deployment = new DeploymentConfig
        {
            Kind = DeploymentKind.DockerContainer,
            ServerOs = ServerOs.Linux,
            ContainerName = "orders",
            ContainerPorts = new[] { "8080:80" },
            ContainerEnvironment = new[] { "ASPNETCORE_ENVIRONMENT=Production", "GREETING=it's up" }
        };
        definition.Rollback = new RollbackConfig { Enabled = true };

        var result = _generator.Generate(definition);
        var yaml = result.Yaml;

        Assert.Contains("command: 'login'", yaml);
        Assert.Contains("containerRegistry: 'our-registry'", yaml);
        Assert.Contains("image='$(DOCKER_REGISTRY)/orders:$(Build.BuildId)'", yaml);
        Assert.Contains("docker run -d --name \"$name\" --restart unless-stopped -p '8080:80' -e 'ASPNETCORE_ENVIRONMENT=Production' -e 'GREETING=it'\\''s up' \"$image\"", yaml);
        Assert.Contains("docker inspect --format '{{.Config.Image}}'", yaml);
        Assert.DoesNotContain("- powershell: |", yaml.Split("- stage: Notify")[0]);
        Assert.DoesNotContain(result.ValidationResults, v => v.Message.Contains("publishes no ports"));
    }

    [Fact]
    public void DockerOnWindowsUsesPowerShell()
    {
        var definition = Minimal();
        definition.Artifact = new ArtifactConfig { ArtifactType = ArtifactType.DockerImage, ArtifactName = "orders" };
        definition.Deployment = new DeploymentConfig { Kind = DeploymentKind.DockerContainer, ServerOs = ServerOs.Windows, ContainerPorts = new[] { "8080:80" } };

        var result = _generator.Generate(definition);

        Assert.Contains("docker run -d --name $name --restart unless-stopped -p '8080:80' $image", result.Yaml);
        Assert.DoesNotContain("- bash: |", result.Yaml);
    }

    [Fact]
    public void DockerWithoutPortsOrImageIsFlagged()
    {
        var definition = Minimal();
        definition.Deployment = new DeploymentConfig { Kind = DeploymentKind.DockerContainer };

        var findings = _generator.Generate(definition).ValidationResults;

        Assert.Contains(findings, v => v.Severity == ValidationSeverity.Warning && v.Message.Contains("need a container image"));
        Assert.Contains(findings, v => v.Severity == ValidationSeverity.Info && v.Message.Contains("publishes no ports"));
    }

    [Theory]
    [InlineData(ServerOs.Linux, "- bash: |", "task.logissue type=warning")]
    [InlineData(ServerOs.Windows, "- powershell: |", "Write-Warning")]
    public void CustomScriptsRunInTheServersShell(ServerOs os, string step, string placeholder)
    {
        var definition = Minimal();
        definition.Deployment = new DeploymentConfig { Kind = DeploymentKind.Custom, ServerOs = os };

        var deployStage = _generator.Generate(definition).Yaml.Split("- stage: Deploy_")[1];

        Assert.Contains(step, deployStage);
        Assert.Contains(placeholder, deployStage);
    }

    [Theory]
    [InlineData("plain", "'plain'")]
    [InlineData("it's", @"'it'\''s'")]
    [InlineData("$(VAR) and $HOME", "'$(VAR) and $HOME'")]
    [InlineData("two\nlines", "'two lines'")]
    [InlineData(null, "''")]
    public void BashLiteralsCannotBreakOutOfTheString(string? value, string expected)
    {
        Assert.Equal(expected, YamlBuilder.BashLiteral(value));
    }

    [Fact]
    public void BashStepsAreWrittenAsBlockScalars()
    {
        var step = YamlBuilder.BashStep("echo one\necho two", "Say it");

        Assert.Equal("    - bash: |\n        echo one\n        echo two\n      displayName: 'Say it'", step);
    }
}
