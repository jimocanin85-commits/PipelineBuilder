using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Services;
using PipelineBuilder.Web.State;
using Xunit;

namespace PipelineBuilder.Tests.RealScripts;

/// <summary>
/// Runs the generated scripts with Windows PowerShell 5.1 on a real Windows machine: files in a folder,
/// a Windows service, and a build agent that reaches the server with PowerShell remoting (WinRM),
/// including an IIS website.
/// </summary>
/// <remarks>Skipped unless <c>PIPELINEBUILDER_REAL_SCRIPTS=windows</c>. The machine needs an administrator account and IIS (see the Real scripts workflow).</remarks>
public class WindowsRealScriptTests
{
    private const string NotPrepared = "Runs only on a prepared Windows machine (PIPELINEBUILDER_REAL_SCRIPTS=windows).";

    private static PipelineDefinition Template(string id)
    {
        var definition = WizardState.CreateDefault();
        Assert.True(new TemplateCatalogue().ApplyTo(id, definition));
        return definition;
    }

    private static Dictionary<string, string> Package(string version) => new()
    {
        ["version.txt"] = version,
        [$"only-in-{version}.txt"] = "x"
    };

    private static string NewFolder(string name)
    {
        var folder = Path.Combine(@"C:\pbtest", name + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string Version(string folder) => File.ReadAllText(Path.Combine(folder, "version.txt")).Trim();

    /// <summary>Deploys version 1, then version 2, then rolls back, and checks the folder after each.</summary>
    private static void DeployTwiceAndRollBack(ScriptHost host, PipelineDefinition definition, string target, string backups, int firstBuild)
    {
        host.NewBuild($"{firstBuild}", "drop", Package("1"));
        ScriptHost.AssertSucceeded(host.Deploy(definition));
        Assert.Equal("1", Version(target));

        host.NewBuild($"{firstBuild + 1}", "drop", Package("2"));
        ScriptHost.AssertSucceeded(host.Deploy(definition));
        Assert.Equal("2", Version(target));
        Assert.False(File.Exists(Path.Combine(target, "only-in-1.txt")), "Files that are no longer in the build are removed.");
        Assert.Equal("1", Version(Path.Combine(backups, "test", $"{firstBuild + 1}")));

        ScriptHost.AssertSucceeded(host.RollBack(definition));
        Assert.Equal("1", Version(target));
        Assert.True(File.Exists(Path.Combine(target, "only-in-1.txt")));
    }

    [SkippableFact]
    public void FilesAreCopiedBackedUpAndRolledBack()
    {
        Skip.IfNot(ScriptHost.EnabledFor("windows"), NotPrepared);
        var root = NewFolder("files");
        var definition = Template("windows-files");
        definition.Deployment.TargetPath = Path.Combine(root, "app");
        definition.Rollback.BackupPath = Path.Combine(root, "backups");

        DeployTwiceAndRollBack(new ScriptHost(), definition, definition.Deployment.TargetPath, definition.Rollback.BackupPath, 301);
    }

    [SkippableFact]
    public void AWindowsServiceIsStoppedUpdatedAndStartedAgain()
    {
        Skip.IfNot(ScriptHost.EnabledFor("windows"), NotPrepared);
        // Any running service that may be stopped will do; its own files are not touched.
        var service = ScriptHost.Shell(
            "(Get-Service | Where-Object { $_.Status -eq 'Running' -and $_.CanStop -and $_.Name -in 'Spooler','W32Time','BITS','Themes','wuauserv' } | Select-Object -First 1).Name");
        Skip.If(service.Length == 0, "No service on this machine that the test may stop and start.");
        var root = NewFolder("service");
        var definition = Template("windows-service-onprem");
        definition.Deployment.ServiceName = service;
        definition.Deployment.TargetPath = Path.Combine(root, "app");
        definition.Rollback.BackupPath = Path.Combine(root, "backups");
        definition.HealthChecks = new[] { new HealthCheckConfig { Enabled = true, HealthCheckType = HealthCheckType.WindowsService, ServiceName = service } };

        DeployTwiceAndRollBack(new ScriptHost(), definition, definition.Deployment.TargetPath, definition.Rollback.BackupPath, 311);

        Assert.Equal("Running", ScriptHost.Shell($"(Get-Service -Name '{service}').Status"));
    }

    [SkippableFact]
    public void ABuildAgentDeploysToAWindowsServerOverWinRm()
    {
        Skip.IfNot(ScriptHost.EnabledFor("windows"), NotPrepared);
        var root = NewFolder("remote");
        var definition = Template("windows-files");
        definition.Deployment.RunFrom = DeployFrom.Agent;
        definition.Deployment.TargetPath = Path.Combine(root, "app");
        definition.Rollback.BackupPath = Path.Combine(root, "backups");
        var host = new ScriptHost();
        host.Set("DeployServers", "localhost");

        DeployTwiceAndRollBack(host, definition, definition.Deployment.TargetPath, definition.Rollback.BackupPath, 321);

        // On the server the package lies in the login account's own profile, not in a folder every account can write to.
        var staged = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PipelineBuilder", "42", "drop");
        Assert.True(File.Exists(Path.Combine(staged, "version.txt")), $"Expected the package in {staged}.");
    }

    [SkippableFact]
    public void AFolderVariableThatIsNotSetStopsTheDeployment()
    {
        Skip.IfNot(ScriptHost.EnabledFor("windows"), NotPrepared);
        var definition = Template("windows-files");
        definition.Deployment.TargetPath = null; // becomes $(DEPLOY_PATH), which nobody has set
        definition.Rollback.BackupPath = Path.Combine(NewFolder("unset"), "backups");
        var host = new ScriptHost();
        host.NewBuild("341", "drop", Package("1"));

        host.AssertStoppedBecauseTheFolderIsNotSet(host.Deploy(definition));
    }

    [SkippableFact]
    public void AStepThatFailsOnTheServerFailsOnTheBuildAgent()
    {
        Skip.IfNot(ScriptHost.EnabledFor("windows"), NotPrepared);
        var definition = Template("own-script");
        definition.Deployment.RunFrom = DeployFrom.Agent;
        definition.Deployment.CustomScript = "Write-Host 'about to fail'\nexit 3";
        var host = new ScriptHost();
        host.Set("DeployServers", "localhost");
        host.NewBuild("331", "drop", Package("1"));

        var results = host.Deploy(definition);

        var deploy = results[^1];
        Assert.NotEqual(0, deploy.ExitCode);
        Assert.Contains("about to fail", deploy.Output);
        Assert.Contains("exit code 3", deploy.Output);
    }

    [SkippableFact]
    public void AnIisWebsiteIsDeployedOverWinRmAndRolledBack()
    {
        Skip.IfNot(ScriptHost.EnabledFor("windows"), NotPrepared);
        var root = NewFolder("iis");
        var site = Path.Combine(root, "site");
        const int port = 8767;
        ScriptHost.Shell($$"""
            Import-Module WebAdministration
            New-Item -ItemType Directory -Force -Path '{{site}}' | Out-Null
            Get-Website -Name 'pbtest' | Remove-Website
            New-Website -Name 'pbtest' -PhysicalPath '{{site}}' -Port {{port}} | Out-Null
            """);

        var definition = Template("iis-onprem");
        definition.Deployment.RunFrom = DeployFrom.Agent;
        definition.Deployment.WebsiteName = "pbtest";
        definition.Rollback.BackupPath = Path.Combine(root, "backups");
        definition.HealthChecks = new[]
        {
            new HealthCheckConfig { Enabled = true, HealthCheckType = HealthCheckType.HttpEndpoint, Url = $"http://localhost:{port}/version.txt", RetryCount = 5, TimeoutSeconds = 10 },
            new HealthCheckConfig { Enabled = true, HealthCheckType = HealthCheckType.IisAppPool, AppPoolName = "DefaultAppPool" },
            new HealthCheckConfig { Enabled = true, HealthCheckType = HealthCheckType.PortCheck, Port = port }
        };
        var host = new ScriptHost();
        host.Set("DeployServers", "localhost");

        DeployTwiceAndRollBack(host, definition, site, definition.Rollback.BackupPath, 341);

        Assert.Equal("1", ScriptHost.Shell($"(Invoke-WebRequest -Uri 'http://localhost:{port}/version.txt' -UseBasicParsing).Content.Trim()"));
        Assert.Equal("Started", ScriptHost.Shell("Import-Module WebAdministration; (Get-WebAppPoolState -Name 'DefaultAppPool').Value"));
    }
}
