using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PipelineBuilder.Core.DependencyInjection;
using Microsoft.AspNetCore.Components.Forms;
using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Web.Components.Pages;
using PipelineBuilder.Web.Components.Steps;
using PipelineBuilder.Web.State;
using Xunit;

namespace PipelineBuilder.Tests;

/// <summary>Renders the wizard with bUnit and drives it like a user would.</summary>
public class WizardPageTests : BunitContext
{
    public WizardPageTests()
    {
        Services.AddPipelineBuilderCore();
        Services.AddScoped<WizardState>();
        JSInterop.SetupVoid("pipelineBuilder.downloadText", _ => true).SetVoidResult();
        JSInterop.Setup<bool>("pipelineBuilder.copyText", _ => true).SetResult(true);
    }

    public static IEnumerable<object[]> AllSteps() => Enum.GetValues<WizardStep>().Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(AllSteps))]
    public void EveryStepRendersWithoutErrors(WizardStep step)
    {
        var cut = Render<Home>();

        GoTo(cut, step);

        Assert.NotEmpty(cut.Find(".wizard-content h2").TextContent);
        Assert.Contains("active", cut.Find($"button[data-step='{step}']").ClassList);
        Assert.DoesNotContain("generation-error", cut.Markup);
    }

    [Fact]
    public void DownloadButtonSendsTheYamlToTheBrowser()
    {
        var cut = Render<Home>();
        GoTo(cut, WizardStep.DownloadExport);

        cut.Find("#download-yaml").Click();

        var invocation = JSInterop.VerifyInvoke("pipelineBuilder.downloadText");
        Assert.Equal(ExportStep.PipelineFileName, invocation.Arguments[0]);
        Assert.Contains("stages:", (string)invocation.Arguments[1]!);
        Assert.Contains("Downloaded azure-pipelines.yml", cut.Find("[role=status]").TextContent);
    }

    [Fact]
    public void CopyButtonUsesTheClipboard()
    {
        var cut = Render<Home>();
        GoTo(cut, WizardStep.DownloadExport);

        cut.Find("#copy-yaml").Click();

        JSInterop.VerifyInvoke("pipelineBuilder.copyText");
        Assert.Contains("copied", cut.Find("[role=status]").TextContent);
    }

    [Fact]
    public void EditsSurviveJumpingBetweenStepsAndReachTheYaml()
    {
        var cut = Render<Home>();
        cut.Find("#pipeline-name").Change("orders-api");

        GoTo(cut, WizardStep.EnvironmentSelection);
        cut.Find("#environments").Change("dev, prod");
        GoTo(cut, WizardStep.ProjectType);
        Assert.Equal("orders-api", cut.Find("#pipeline-name").GetAttribute("value"));

        GoTo(cut, WizardStep.YamlPreview);
        var yaml = cut.Find(".yaml-preview").TextContent;
        Assert.Contains("# Pipeline: orders-api", yaml);
        Assert.Contains("- stage: Deploy_dev", yaml);
        Assert.DoesNotContain("Deploy_preprod", yaml);
    }

    [Fact]
    public void KeyVaultCanBeTurnedOnAndOff()
    {
        var cut = Render<Home>();
        GoTo(cut, WizardStep.VariableGroupsAndKeyVault);

        cut.Find("input[type=checkbox]:not(.card input)").Change(true); // first checkbox outside the group cards is Key Vault
        cut.Find("#kv-name").Change("kv-orders");
        GoTo(cut, WizardStep.YamlPreview);
        Assert.Contains("AzureKeyVault@2", cut.Find(".yaml-preview").TextContent);

        GoTo(cut, WizardStep.VariableGroupsAndKeyVault);
        cut.Find("input[type=checkbox]:not(.card input)").Change(false);
        GoTo(cut, WizardStep.YamlPreview);
        Assert.DoesNotContain("AzureKeyVault@2", cut.Find(".yaml-preview").TextContent);
    }

    [Fact]
    public void ApplyingATemplateChangesTheGeneratedPipeline()
    {
        var cut = Render<Home>();
        GoTo(cut, WizardStep.PipelineTemplate);

        cut.Find("button[data-template='aks-deploy']").Click();
        Assert.Contains("Template applied", cut.Markup);

        GoTo(cut, WizardStep.YamlPreview);
        var yaml = cut.Find(".yaml-preview").TextContent;
        Assert.Contains("KubernetesManifest@1", yaml);
        Assert.DoesNotContain("IISWebAppDeploymentOnMachineGroup@0", yaml);
    }

    [Fact]
    public void InvalidSettingsShowErrorsInsteadOfCrashing()
    {
        var cut = Render<Home>();
        GoTo(cut, WizardStep.EnvironmentSelection);
        cut.Find("#environments").Change("Bad Name");

        GoTo(cut, WizardStep.YamlPreview);

        Assert.Contains("invalid characters", cut.Find(".generation-error").TextContent);
    }

    [Fact]
    public void StepShowsItsOwnErrorsAndTheNavigationFlagsIt()
    {
        var cut = Render<Home>();
        GoTo(cut, WizardStep.EnvironmentSelection);

        cut.Find("#environments").Change("Bad Name");

        Assert.Contains("invalid characters", cut.Find(".issue-list").TextContent);
        Assert.NotNull(cut.Find($"button[data-step='{WizardStep.EnvironmentSelection}'] .badge"));
        Assert.Empty(cut.FindAll($"button[data-step='{WizardStep.ProjectType}'] .badge"));
    }

    [Fact]
    public void GoToStepLinkOpensTheStepWithTheProblem()
    {
        var cut = Render<Home>();
        GoTo(cut, WizardStep.EnvironmentSelection);
        cut.Find("#environments").Change("Bad Name");
        GoTo(cut, WizardStep.YamlPreview);

        cut.Find($"button[data-go-to='{WizardStep.EnvironmentSelection}']").Click();

        Assert.Contains("active", cut.Find($"button[data-step='{WizardStep.EnvironmentSelection}']").ClassList);
        Assert.NotNull(cut.Find("#environments"));
    }

    [Fact]
    public void SettingsCanBeDownloaded()
    {
        var cut = Render<Home>();
        GoTo(cut, WizardStep.DownloadExport);

        cut.Find("#download-settings").Click();

        var invocation = JSInterop.VerifyInvoke("pipelineBuilder.downloadText");
        Assert.Equal(PipelineDefinitionSerializer.FileName, invocation.Arguments[0]);
        Assert.Contains(PipelineDefinitionSerializer.Format, (string)invocation.Arguments[1]!);
    }

    [Fact]
    public void SavedSettingsCanBeOpened()
    {
        var saved = WizardState.CreateDefault();
        saved.Name = "loaded-app";
        saved.Environments = new[] { "qa", "prod" };
        var cut = Render<Home>();

        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText(PipelineDefinitionSerializer.ToJson(saved), "settings.json"));

        Assert.Contains("Loaded settings for 'loaded-app'", cut.Markup);
        Assert.Equal("loaded-app", cut.Find("#pipeline-name").GetAttribute("value"));
        GoTo(cut, WizardStep.YamlPreview);
        Assert.Contains("- stage: Deploy_qa", cut.Find(".yaml-preview").TextContent);
    }

    [Fact]
    public void OpeningAnInvalidFileShowsAnError()
    {
        var cut = Render<Home>();

        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("{ nope", "settings.json"));

        Assert.Contains("Could not open the file", cut.Markup);
        Assert.Equal("enterprise-pipeline", cut.Find("#pipeline-name").GetAttribute("value"));
    }

    private static void GoTo(IRenderedComponent<Home> cut, WizardStep step) =>
        cut.Find($"button[data-step='{step}']").Click();
}
