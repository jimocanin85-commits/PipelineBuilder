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

/// <summary>
/// Build once, configure per environment: the same package gets each environment's values in its
/// settings files, from that environment's own variable group.
/// </summary>
public class SettingsPerEnvironmentTests : BunitContext
{
    private readonly IPipelineGeneratorService _generator =
        new ServiceCollection().AddPipelineBuilderCore().BuildServiceProvider().GetRequiredService<IPipelineGeneratorService>();

    public SettingsPerEnvironmentTests()
    {
        Services.AddPipelineBuilderCore();
        Services.AddScoped<WizardState>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static PipelineDefinition Template(string id, string? settingsFiles = WizardState.DefaultSettingsFiles)
    {
        var definition = WizardState.CreateDefault();
        Assert.True(new TemplateCatalogue().ApplyTo(id, definition));
        definition.Deployment.SettingsFiles = settingsFiles;
        return definition;
    }

    private static List<Dictionary<object, object>> DeploySteps(string yaml)
    {
        var job = PipelineYaml.DeployJob(PipelineYaml.Parse(yaml));
        var strategy = (Dictionary<object, object>)job["strategy"];
        var lifecycle = (Dictionary<object, object>)strategy.Values.Single();
        var deploy = (Dictionary<object, object>)lifecycle["deploy"];
        return ((List<object>)deploy["steps"]).Cast<Dictionary<object, object>>().ToList();
    }

    private static string Name(Dictionary<object, object> step) =>
        step.GetValueOrDefault("task") as string ?? step.GetValueOrDefault("displayName") as string ?? step.Keys.First().ToString()!;

    [Theory]
    [InlineData("iis-onprem")]
    [InlineData("windows-service-onprem")]
    [InlineData("linux-service")]
    [InlineData("windows-files")]
    [InlineData("ansible-playbook")]
    [InlineData("own-script")]
    public void ThePackageGetsTheEnvironmentsValuesBeforeItIsDeployed(string template)
    {
        var steps = DeploySteps(_generator.Generate(Template(template)).Yaml);

        var transform = Assert.Single(steps, step => Name(step) == "FileTransform@1");
        var inputs = (Dictionary<object, object>)transform["inputs"];
        Assert.Equal("json", (string)inputs["fileType"]);
        Assert.Equal("**/appsettings.json", (string)inputs["targetFiles"]);
        Assert.StartsWith("$(Pipeline.Workspace)/", (string)inputs["folderPath"]);
        // After the package is downloaded, before anything is deployed.
        var names = steps.Select(Name).ToList();
        Assert.True(names.FindIndex(n => n.StartsWith("DownloadPipelineArtifact", StringComparison.Ordinal)) < names.IndexOf("FileTransform@1"));
    }

    [Fact]
    public void KeyVaultSecretsArriveBeforeTheSettingsAreFilledIn()
    {
        var definition = Template("iis-onprem");
        definition.KeyVault = new KeyVaultConfig { KeyVaultName = "kv-orders", SecretsFilter = "DbPassword" };

        var names = DeploySteps(_generator.Generate(definition).Yaml).Select(Name).ToList();

        Assert.True(names.IndexOf("AzureKeyVault@2") < names.IndexOf("FileTransform@1"));
    }

    [Fact]
    public void WhenTheBuildAgentDeploysTheValuesAreFilledInBeforeThePackageIsCopied()
    {
        var definition = Template("windows-service-onprem");
        definition.Deployment.RunFrom = DeployFrom.Agent;
        definition.BuildAgent = BuildAgentType.SelfHosted;
        definition.PoolName = "OnPrem";

        var names = DeploySteps(_generator.Generate(definition).Yaml).Select(Name).ToList();

        Assert.True(names.IndexOf("FileTransform@1") < names.IndexOf("Copy the package to the servers"));
    }

    [Theory]
    [InlineData("docker-build-push", WizardState.DefaultSettingsFiles)] // an image is configured where it runs
    [InlineData("aks-deploy", WizardState.DefaultSettingsFiles)]
    [InlineData("iis-onprem", null)]                                   // off unless asked for
    [InlineData("iis-onprem", "  ")]
    public void NothingIsFilledInWhenThereIsNoPackageOrNothingWasAskedFor(string template, string? files)
    {
        Assert.DoesNotContain("FileTransform", _generator.Generate(Template(template, files)).Yaml);
    }

    [Fact]
    public void TickingItGivesEveryEnvironmentItsOwnVariableGroup()
    {
        var cut = Render<Home>();
        cut.Find("button[data-step='Target']").Click();
        cut.Find("details.more").SetAttribute("open", "");
        var wizard = Services.GetRequiredService<WizardState>();
        wizard.VariableGroups.Add(new VariableGroupConfig { Name = "vg-own-prod", Scope = VariableGroupScope.Environment, EnvironmentName = "prod" });

        cut.Find("#settings-per-environment").Change(true);

        Assert.Equal("**/appsettings.json", cut.Find("#settings-files").GetAttribute("value"));
        Assert.Equal(new[] { "vg-own-prod", "vg-my-app-test", "vg-my-app-preprod" }, wizard.VariableGroups.Select(g => g.Name));
        Assert.All(wizard.VariableGroups, group => Assert.Equal(VariableGroupScope.Environment, group.Scope));
        cut.Find("button[data-step='Result']").Click();
        var yaml = cut.Find(".yaml-preview").TextContent;
        Assert.Contains("- task: FileTransform@1", yaml);
        Assert.Contains("group: 'vg-my-app-test'", yaml);

        cut.Find("button[data-step='Target']").Click();
        cut.Find("#settings-per-environment").Change(false);
        Assert.Empty(cut.FindAll("#settings-files"));
        cut.Find("button[data-step='Result']").Click();
        Assert.DoesNotContain("FileTransform", cut.Find(".yaml-preview").TextContent);
    }

    [Fact]
    public void ADockerImageDoesNotOfferIt()
    {
        var cut = Render<Home>();
        cut.Find("button[data-template='docker-build-push']").Click();
        cut.Find("button[data-step='Target']").Click();

        Assert.Empty(cut.FindAll("#settings-per-environment"));
    }
}
