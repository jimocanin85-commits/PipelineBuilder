using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PipelineBuilder.Core.Abstractions;
using PipelineBuilder.Core.DependencyInjection;
using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Services;
using PipelineBuilder.Core.Yaml;
using PipelineBuilder.Web.Components.Pages;
using PipelineBuilder.Web.State;
using Xunit;

namespace PipelineBuilder.Tests;

/// <summary>
/// One build agent deploys to the servers over the network: PowerShell remoting (WinRM) to Windows
/// servers and SSH to Linux servers. The scripts are the same ones an agent on the server would run.
/// </summary>
public class DeployFromAgentTests : BunitContext
{
    private readonly IPipelineGeneratorService _generator =
        new ServiceCollection().AddPipelineBuilderCore().BuildServiceProvider().GetRequiredService<IPipelineGeneratorService>();

    public DeployFromAgentTests()
    {
        Services.AddPipelineBuilderCore();
        Services.AddScoped<WizardState>();
        // The page keeps the wizard in the browser's storage; here nothing is stored, and nothing is there.
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static PipelineDefinition FromAgent(string templateId)
    {
        var definition = WizardState.CreateDefault();
        Assert.True(new TemplateCatalogue().ApplyTo(templateId, definition));
        definition.Deployment.RunFrom = DeployFrom.Agent;
        definition.BuildAgent = BuildAgentType.SelfHosted;
        definition.PoolName = "OnPrem";
        return definition;
    }

    private string Yaml(PipelineDefinition definition) => _generator.Generate(definition).Yaml.ReplaceLineEndings("\n");

    public static IEnumerable<object[]> TemplateIds() => TemplateCatalogue.LoadBuiltIn().Select(t => new object[] { t.Id });

    [Theory]
    [MemberData(nameof(TemplateIds))]
    public void EveryTemplateStillProducesAValidPipeline(string templateId)
    {
        var result = _generator.Generate(FromAgent(templateId)); // throws when the YAML is invalid

        Assert.DoesNotContain("resourceType: VirtualMachine", result.Yaml);
        Assert.DoesNotContain(result.ValidationResults, v => v.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void TheDeployJobRunsOnTheAgentAndReadsTheServersFromAVariable()
    {
        var yaml = Yaml(FromAgent("windows-service-onprem"));

        Assert.Contains("# Builds and deployments run on this agent.\npool:\n  name: 'OnPrem'", yaml);
        Assert.Contains("      variables:\n        DeployServers: $(SERVERS_${{ replace(environment, '-', '_') }})", yaml);
        Assert.Contains("      environment: ${{ environment }}", yaml);
        Assert.DoesNotContain("rolling:", yaml);
    }

    [Fact]
    public void WindowsServersGetTheSameScriptOverPowerShellRemoting()
    {
        var yaml = Yaml(FromAgent("windows-service-onprem"));
        var deploy = yaml[yaml.IndexOf("- stage: Deploy_", StringComparison.Ordinal)..];

        Assert.Contains("Invoke-Command -ComputerName $server", deploy);
        Assert.Contains("powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $file", deploy);
        Assert.Contains("Stop-Service -Name $service -Force", deploy); // the ordinary deploy script, sent as text
        Assert.Contains("$script = @'\n", deploy);
        Assert.DoesNotContain("- bash: |", deploy);
        Assert.DoesNotContain("ssh ", deploy);

        // The package is copied to the servers first, and the script reads it from there.
        Assert.Contains("Copy-Item -Path $package -Destination $folder -ToSession $session -Recurse -Force", deploy);
        Assert.Contains(@"$package = (Join-Path $env:LOCALAPPDATA 'PipelineBuilder\$(System.DefinitionId)\drop')", deploy);
        Assert.True(deploy.IndexOf("Copy the package to the servers", StringComparison.Ordinal)
                    < deploy.IndexOf("Deploy Windows service", StringComparison.Ordinal));
    }

    [Fact]
    public void LinuxServersGetTheSameScriptOverSsh()
    {
        var definition = FromAgent("linux-service");
        definition.Deployment.SshUser = "deploy";
        var yaml = Yaml(definition);
        var deploy = yaml[yaml.IndexOf("- stage: Deploy_", StringComparison.Ordinal)..];

        Assert.Contains("$user = 'deploy'", deploy);
        Assert.Contains("scp -q -o BatchMode=yes $file \"${login}:$remote\"", deploy);
        Assert.Contains("ssh -o BatchMode=yes $login \"bash $remote;", deploy);
        Assert.Contains("as_root systemctl start \"$service\"", deploy); // the ordinary bash script, sent as text
        Assert.Contains("package=\"$HOME\"'/.pipelinebuilder/$(System.DefinitionId)/drop'", deploy);
        Assert.Contains("scp -q -r -o BatchMode=yes $package \"${login}:$folder\"", deploy);
        Assert.DoesNotContain("- bash: |", deploy); // the agent runs PowerShell; bash runs on the servers
        Assert.DoesNotContain("Invoke-Command", deploy);
    }

    [Fact]
    public void BackupRollbackAndHealthChecksAlsoRunOnTheServers()
    {
        var definition = FromAgent("windows-service-onprem");
        definition.HealthChecks = new[] { new HealthCheckConfig { Enabled = true, HealthCheckType = HealthCheckType.WindowsService, ServiceName = "Orders" } };
        var yaml = Yaml(definition);

        var failure = yaml[yaml.IndexOf("failure:\n", StringComparison.Ordinal)..];
        Assert.Contains("Invoke-Command -ComputerName $server", failure);
        Assert.Contains("Restored $target from $backup", failure);

        // The check is sent as text and starts the way an ordinary step does: stop at the first error.
        Assert.Matches(@"\$script = @'\n +\$ErrorActionPreference = 'Stop'\n +\$svc = Get-Service -Name 'Orders'", yaml);
        Assert.Contains("displayName: 'Windows service health check'", yaml);
        Assert.Equal(4, yaml.Split("Invoke-Command -ComputerName $server").Length - 1); // backup, deploy, health check, rollback
    }

    [Fact]
    public void IisIsDeployedByAScriptBecauseTheIisTaskOnlyRunsOnTheServerItself()
    {
        var yaml = Yaml(FromAgent("iis-onprem"));

        Assert.DoesNotContain("IISWebAppDeploymentOnMachineGroup@0", yaml);
        Assert.Contains("Stop-WebAppPool -Name $pool", yaml);
        Assert.Contains("Sync-Folder -Source $source -Destination $target -Mirror", yaml);
        Assert.Contains("Start-WebAppPool -Name $pool", yaml);

        var onServer = WizardState.CreateDefault();
        Assert.Contains("IISWebAppDeploymentOnMachineGroup@0", Yaml(onServer));
    }

    [Theory]
    [InlineData(ServerOs.Linux, "ssh -o BatchMode=yes")]
    [InlineData(ServerOs.Windows, "Invoke-Command -ComputerName $server")]
    public void DockerHasNoPackageToCopyAndNoLoginOnTheAgent(ServerOs os, string transport)
    {
        var definition = FromAgent("docker-build-push");
        definition.Deployment.ServerOs = os;
        var yaml = Yaml(definition);
        var deploy = yaml[yaml.IndexOf("- stage: Deploy_", StringComparison.Ordinal)..];

        Assert.Contains(transport, deploy);
        Assert.Contains("docker pull", deploy);
        Assert.DoesNotContain("command: 'login'", deploy);
        Assert.DoesNotContain("Copy the package to the servers", deploy);
    }

    [Fact]
    public void TheNeedsListNamesAServerVariablePerEnvironment()
    {
        var definition = FromAgent("linux-service");
        definition.Environments = new[] { "test", "pre-prod", "prod" };

        var needs = _generator.Generate(definition).Requirements;

        var variables = needs.Where(n => n.Kind == RequirementKind.Variable).Select(n => n.Name).ToList();
        Assert.Equal(new[] { "SERVERS_TEST", "SERVERS_PRE_PROD", "SERVERS_PROD", "DEPLOY_PATH", "SERVICE_NAME", "SSH_USER" }, variables);
        Assert.Contains("Servers in pre-prod", needs.Single(n => n.Name == "SERVERS_PRE_PROD").Purpose);
        Assert.DoesNotContain(needs, n => n.Name == ServerScript.ServersVariable);
        Assert.All(needs.Where(n => n.Kind == RequirementKind.Environment), n => Assert.DoesNotContain("servers registered", n.Purpose));
    }

    [Fact]
    public void AMicrosoftAgentCannotReachTheServers()
    {
        var definition = FromAgent("iis-onprem");
        definition.BuildAgent = BuildAgentType.MicrosoftHosted;
        Assert.Contains(_generator.Generate(definition).ValidationResults, v => v.RuleId == "deployment.from-agent-needs-own-pool");

        definition.BuildAgent = BuildAgentType.SelfHosted;
        Assert.DoesNotContain(_generator.Generate(definition).ValidationResults, v => v.RuleId == "deployment.from-agent-needs-own-pool");
    }

    [Fact]
    public void RollingIsIgnoredBecauseTheAgentTakesOneServerAtATime()
    {
        var definition = FromAgent("iis-onprem");
        definition.DeploymentStrategy.StrategyType = DeploymentStrategyType.Rolling;

        var result = _generator.Generate(definition);

        Assert.DoesNotContain("rolling:", result.Yaml);
        Assert.DoesNotContain(result.ValidationResults, v => v.RuleId == "deployment.rolling-without-servers");
    }

    [Fact]
    public void KubernetesAlreadyDeploysFromTheAgentSoNothingChanges()
    {
        var withSetting = FromAgent("aks-deploy");
        var without = FromAgent("aks-deploy");
        without.Deployment.RunFrom = DeployFrom.Server;

        Assert.Equal(Yaml(without), Yaml(withSetting));
    }

    [Fact]
    public void AScriptStepIsUnchangedWhenAnAgentOnTheServerRunsIt()
    {
        var onServer = new DeploymentConfig();

        Assert.Equal(YamlBuilder.BashStep("echo hi", "Say hi"), ServerScript.Step(onServer, ScriptShell.Bash, "echo hi", "Say hi"));
        Assert.Equal(YamlBuilder.PowerShellStep("Write-Host hi", "Say hi"), ServerScript.Step(onServer, ScriptShell.PowerShell, "Write-Host hi", "Say hi"));
        Assert.Equal("$HOME/.pipelinebuilder/$(System.DefinitionId)/drop.zip", ServerScript.RemotePackagePath(ScriptShell.Bash, "$(Pipeline.Workspace)/drop/drop.zip"));
    }

    [Fact]
    public void TheWizardOffersTheChoiceOnlyForServers()
    {
        var cut = Render<Home>();
        cut.Find($"button[data-step='{WizardStep.Target}']").Click();
        Assert.NotEmpty(cut.FindAll("#rolling"));

        cut.Find("#run-from-Agent").Change(true);

        Assert.Empty(cut.FindAll("#rolling"));
        Assert.Empty(cut.FindAll("#ssh-user")); // Windows servers use the agent's own account
        Assert.Contains("PowerShell remoting", cut.Find(".wizard-content").TextContent);
        Assert.Contains("Microsoft's agents cannot reach", cut.Find(".issue-list").TextContent);
        Assert.True(cut.Find("details.more").HasAttribute("open"));

        cut.Find($"button[data-step='{WizardStep.Result}']").Click();
        var variables = cut.FindAll(".needs-list li[data-need='Variable'] code").Select(e => e.TextContent);
        Assert.Equal(new[] { "SERVERS_TEST", "SERVERS_PREPROD", "SERVERS_PROD" }, variables);

        cut.Find($"button[data-step='{WizardStep.Start}']").Click();
        cut.Find("button[data-template='linux-service']").Click();
        cut.Find($"button[data-step='{WizardStep.Target}']").Click();
        Assert.NotEmpty(cut.FindAll("#ssh-user"));
        Assert.Contains("with SSH", cut.Find(".wizard-content").TextContent);

        cut.Find($"button[data-step='{WizardStep.Start}']").Click();
        cut.Find("button[data-template='aks-deploy']").Click();
        cut.Find($"button[data-step='{WizardStep.Target}']").Click();
        Assert.Empty(cut.FindAll("#run-from"));
    }

    [Fact]
    public void TheOwnScriptHintSaysWhereTheBuildIsOnTheServer()
    {
        var cut = Render<Home>();
        cut.Find("button[data-template='own-script']").Click();
        cut.Find($"button[data-step='{WizardStep.Target}']").Click();
        Assert.Contains("The build is in $(Pipeline.Workspace)/drop.", cut.Find(".wizard-content").TextContent);

        cut.Find("#run-from-Agent").Change(true);

        Assert.Contains(@"The build is in $env:LOCALAPPDATA\PipelineBuilder\$(System.DefinitionId)\drop.", cut.Find(".wizard-content").TextContent);
    }

    // The package and the scripts may hold secrets, and what is copied to a server is installed
    // there. So nothing is put in a folder that other accounts on the server can write to.
    [Theory]
    [InlineData("windows-service-onprem")]
    [InlineData("iis-onprem")]
    [InlineData("windows-files")]
    [InlineData("linux-service")]
    [InlineData("docker-build-push")]
    public void NothingIsPutInAFolderSharedByAllAccountsOnTheServer(string template)
    {
        var definition = FromAgent(template);
        definition.HealthChecks = new[] { new HealthCheckConfig { Enabled = true, HealthCheckType = HealthCheckType.HttpEndpoint, Url = "https://localhost/health" } };
        var yaml = Yaml(definition);

        Assert.DoesNotContain("/tmp/", yaml);
        Assert.DoesNotContain("/var/tmp", yaml);
        Assert.DoesNotContain("ProgramData", yaml);
    }

    [Fact]
    public void TheWorkFolderOnALinuxServerIsOpenToTheLoginAccountOnly()
    {
        var yaml = Yaml(FromAgent("linux-service"));

        // Before the package, and before every script, is copied there.
        Assert.Contains("\"mkdir -p .pipelinebuilder && chmod 700 .pipelinebuilder && rm -rf '$folder' && mkdir '$folder'\"", yaml);
        Assert.Contains("ssh -o BatchMode=yes $login \"mkdir -p .pipelinebuilder && chmod 700 .pipelinebuilder\"", yaml);
        Assert.Contains("$remote = '.pipelinebuilder/step-$(Build.BuildId).sh'", yaml);
        // Host keys are checked: an unknown server is refused, never accepted silently.
        Assert.DoesNotContain("StrictHostKeyChecking", yaml);
    }

    [Fact]
    public void APathInTheHomeFolderIsFilledInByTheShellAndTheRestIsQuoted()
    {
        Assert.Equal("\"$HOME\"'/.pipelinebuilder/7/it'\\''s'", ServerScript.PathLiteral(ScriptShell.Bash, "$HOME/.pipelinebuilder/7/it's"));
        Assert.Equal("'/opt/my app'", ServerScript.PathLiteral(ScriptShell.Bash, "/opt/my app"));
        Assert.Equal(@"(Join-Path $env:LOCALAPPDATA 'PipelineBuilder\7\it''s')", ServerScript.PathLiteral(ScriptShell.PowerShell, @"$env:LOCALAPPDATA\PipelineBuilder\7\it's"));
        Assert.Equal(@"'D:\apps\my app'", ServerScript.PathLiteral(ScriptShell.PowerShell, @"D:\apps\my app"));
    }

    [Fact]
    public void AScriptThatWouldEndTheHereStringIsReported()
    {
        var definition = FromAgent("own-script");
        definition.Deployment.CustomScript = "$text = @'\nhello\n'@\nWrite-Host $text";

        var findings = _generator.Generate(definition).ValidationResults;

        Assert.Contains(findings, f => f.Severity == ValidationSeverity.Error && f.Message.Contains("starts with '@"));

        definition.Deployment.RunFrom = DeployFrom.Server;
        Assert.DoesNotContain(_generator.Generate(definition).ValidationResults, f => f.Message.Contains("starts with '@"));
    }
}
