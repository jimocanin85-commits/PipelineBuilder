using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Services;
using PipelineBuilder.Web.State;
using Xunit;

namespace PipelineBuilder.Tests.RealScripts;

/// <summary>
/// Runs the generated bash scripts on a real Linux machine: a systemd service, a Docker container, and
/// a build agent that reaches the server over SSH. Each test deploys version 1, deploys version 2,
/// then runs the rollback and expects version 1 back.
/// </summary>
/// <remarks>Skipped unless <c>PIPELINEBUILDER_REAL_SCRIPTS=linux</c>. The machine needs sudo without a password, systemd, Docker and Python 3.</remarks>
public class LinuxRealScriptTests
{
    private const string NotPrepared = "Runs only on a prepared Linux machine (PIPELINEBUILDER_REAL_SCRIPTS=linux).";

    private static PipelineDefinition Template(string id)
    {
        var definition = WizardState.CreateDefault();
        Assert.True(new TemplateCatalogue().ApplyTo(id, definition));
        return definition;
    }

    /// <summary>A package with a tiny web server, so the HTTP, port and service checks have something real to check.</summary>
    private static Dictionary<string, string> Package(string version, string folder, int port) => new()
    {
        ["run.sh"] = $"exec python3 -m http.server {port} --directory {folder}\n",
        ["version.txt"] = version + "\n",
        [$"only-in-{version}.txt"] = "x\n"
    };

    private static void CreateService(string service, string folder)
    {
        ScriptHost.Shell($"""
            sudo mkdir -p {folder} /var/backups/pipelinebuilder
            sudo chown -R "$USER" {folder} /var/backups/pipelinebuilder
            printf '[Unit]\nDescription=PipelineBuilder test service\n[Service]\nExecStart=/bin/sh {folder}/run.sh\n' | sudo tee /etc/systemd/system/{service}.service > /dev/null
            sudo systemctl daemon-reload
            sudo systemctl stop {service} 2>/dev/null || true
            """);
    }

    private static HealthCheckConfig[] Checks(string service, int port) => new[]
    {
        new HealthCheckConfig { Enabled = true, HealthCheckType = HealthCheckType.HttpEndpoint, Url = $"http://localhost:{port}/version.txt", RetryCount = 5, TimeoutSeconds = 5 },
        new HealthCheckConfig { Enabled = true, HealthCheckType = HealthCheckType.PortCheck, Port = port },
        new HealthCheckConfig { Enabled = true, HealthCheckType = HealthCheckType.WindowsService, ServiceName = service }
    };

    /// <summary>This machine plays both parts: the build agent, and the server it logs in to.</summary>
    private static void AllowSshToThisMachine() => ScriptHost.Shell("""
        command -v sshd > /dev/null || sudo apt-get install -y -qq openssh-server > /dev/null
        sudo systemctl start ssh
        mkdir -p ~/.ssh && chmod 700 ~/.ssh
        [ -f ~/.ssh/id_ed25519 ] || ssh-keygen -q -t ed25519 -N '' -f ~/.ssh/id_ed25519
        grep -q -f ~/.ssh/id_ed25519.pub ~/.ssh/authorized_keys 2> /dev/null || cat ~/.ssh/id_ed25519.pub >> ~/.ssh/authorized_keys
        chmod 600 ~/.ssh/authorized_keys
        ssh-keyscan localhost >> ~/.ssh/known_hosts 2> /dev/null
        ssh -o BatchMode=yes "$USER@localhost" true
        """);

    private static string Served(int port) => ScriptHost.Shell($"curl -s --retry 5 --retry-connrefused http://localhost:{port}/version.txt");

    [SkippableFact]
    public void ALinuxServiceIsDeployedBackedUpAndRolledBack()
    {
        Skip.IfNot(ScriptHost.EnabledFor("linux"), NotPrepared);
        const string service = "pbtest";
        const string folder = "/opt/pbtest";
        const int port = 8765;
        CreateService(service, folder);

        var definition = Template("linux-service");
        definition.Deployment.ServiceName = service;
        definition.Deployment.TargetPath = folder;
        definition.HealthChecks = Checks(service, port);
        var host = new ScriptHost();

        host.NewBuild("101", "drop", Package("1", folder, port));
        ScriptHost.AssertSucceeded(host.Deploy(definition));
        Assert.Equal("1", Served(port));
        Assert.Equal("active", ScriptHost.Shell($"systemctl is-active {service}"));

        host.NewBuild("102", "drop", Package("2", folder, port));
        ScriptHost.AssertSucceeded(host.Deploy(definition));
        Assert.Equal("2", Served(port));
        Assert.False(File.Exists($"{folder}/only-in-1.txt"), "Files that are no longer in the build are removed.");
        Assert.Equal("1", File.ReadAllText("/var/backups/pipelinebuilder/test/102/version.txt").Trim());

        ScriptHost.AssertSucceeded(host.RollBack(definition));
        Assert.Equal("1", Served(port));
        Assert.True(File.Exists($"{folder}/only-in-1.txt"));
        Assert.Equal("active", ScriptHost.Shell($"systemctl is-active {service}"));
    }

    [SkippableFact]
    public void AFailingHealthCheckFailsTheDeployment()
    {
        Skip.IfNot(ScriptHost.EnabledFor("linux"), NotPrepared);
        const string service = "pbcheck";
        const string folder = "/opt/pbcheck";
        const int port = 8764;
        CreateService(service, folder);

        var definition = Template("linux-service");
        definition.Deployment.ServiceName = service;
        definition.Deployment.TargetPath = folder;
        definition.Rollback.Enabled = false;
        definition.HealthChecks = new[]
        {
            new HealthCheckConfig { Enabled = true, HealthCheckType = HealthCheckType.HttpEndpoint, Url = $"http://localhost:{port}/missing.txt", RetryCount = 2, TimeoutSeconds = 5 }
        };
        var host = new ScriptHost();
        host.NewBuild("111", "drop", Package("1", folder, port));

        var results = host.Deploy(definition);

        var check = results[^1];
        Assert.Equal("HTTP endpoint health check", check.Name);
        Assert.NotEqual(0, check.ExitCode);
        Assert.Contains("HTTP 404", check.Output);
    }

    [SkippableFact]
    public void ADockerContainerIsReplacedAndRolledBack()
    {
        Skip.IfNot(ScriptHost.EnabledFor("linux"), NotPrepared);
        // A registry on this machine, with two versions of a small image.
        ScriptHost.Shell("""
            docker rm -f pbregistry pbapp > /dev/null 2>&1 || true
            docker run -d --name pbregistry -p 5000:5000 registry:2 > /dev/null
            for version in 201 202; do
              dir=`mktemp -d`
              printf 'FROM busybox\nLABEL version=%s\nCMD ["sleep", "3600"]\n' "$version" > "$dir/Dockerfile"
              docker build -q -t localhost:5000/pbapp:$version "$dir" > /dev/null
              for attempt in 1 2 3 4 5; do docker push -q localhost:5000/pbapp:$version > /dev/null && break || sleep 2; done
              docker image rm localhost:5000/pbapp:$version > /dev/null
            done
            sudo mkdir -p /var/backups/pipelinebuilder && sudo chown -R "$USER" /var/backups/pipelinebuilder
            """);

        var definition = Template("docker-build-push");
        definition.Artifact.ArtifactName = "pbapp";
        definition.Deployment.ContainerName = "pbapp";
        definition.Deployment.ContainerPorts = new[] { "8763:80" };
        definition.Deployment.ContainerEnvironment = new[] { "GREETING=it's up" };
        var host = new ScriptHost();
        host.Set("DOCKER_REGISTRY", "localhost:5000");

        host.Set("Build.BuildId", "201");
        ScriptHost.AssertSucceeded(host.Deploy(definition));
        Assert.Equal("localhost:5000/pbapp:201 true", RunningImage());
        Assert.Contains("GREETING=it's up", ScriptHost.Shell("docker inspect --format '{{json .Config.Env}}' pbapp"));

        host.Set("Build.BuildId", "202");
        ScriptHost.AssertSucceeded(host.Deploy(definition));
        Assert.Equal("localhost:5000/pbapp:202 true", RunningImage());

        ScriptHost.AssertSucceeded(host.RollBack(definition));
        Assert.Equal("localhost:5000/pbapp:201 true", RunningImage());

        static string RunningImage() => ScriptHost.Shell("docker inspect --format '{{.Config.Image}} {{.State.Running}}' pbapp");
    }

    [SkippableFact]
    public void ABuildAgentDeploysToALinuxServerOverSsh()
    {
        Skip.IfNot(ScriptHost.EnabledFor("linux"), NotPrepared);
        const string service = "pbremote";
        const string folder = "/opt/pbremote";
        const int port = 8762;
        CreateService(service, folder);
        AllowSshToThisMachine();

        var definition = Template("linux-service");
        definition.Deployment.RunFrom = DeployFrom.Agent;
        definition.Deployment.SshUser = Environment.UserName;
        definition.Deployment.ServiceName = service;
        definition.Deployment.TargetPath = folder;
        definition.HealthChecks = Checks(service, port);
        var host = new ScriptHost();
        host.Set("DeployServers", "localhost");

        host.NewBuild("121", "drop", Package("1", folder, port));
        var first = host.Deploy(definition);
        ScriptHost.AssertSucceeded(first);
        Assert.Equal("Copy the package to the servers", first[0].Name);
        Assert.Equal("1", Served(port));

        host.NewBuild("122", "drop", Package("2", folder, port));
        ScriptHost.AssertSucceeded(host.Deploy(definition));
        Assert.Equal("2", Served(port));

        ScriptHost.AssertSucceeded(host.RollBack(definition));
        Assert.Equal("1", Served(port));
        Assert.Equal("active", ScriptHost.Shell($"systemctl is-active {service}"));
    }

    [SkippableFact]
    public void AStepThatFailsOnTheServerFailsOnTheBuildAgent()
    {
        Skip.IfNot(ScriptHost.EnabledFor("linux"), NotPrepared);
        AllowSshToThisMachine();
        var definition = Template("own-script");
        definition.Deployment.ServerOs = ServerOs.Linux;
        definition.Deployment.RunFrom = DeployFrom.Agent;
        definition.Deployment.SshUser = Environment.UserName;
        definition.Deployment.CustomScript = "echo 'about to fail'\nexit 3";
        var host = new ScriptHost();
        host.Set("DeployServers", "localhost");
        host.NewBuild("131", "drop", new Dictionary<string, string> { ["version.txt"] = "1" });

        var results = host.Deploy(definition);

        var deploy = results[^1];
        Assert.NotEqual(0, deploy.ExitCode);
        Assert.Contains("about to fail", deploy.Output);
        Assert.Contains("exit code 3", deploy.Output);
    }
}
