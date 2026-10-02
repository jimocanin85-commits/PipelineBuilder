using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using PipelineBuilder.Core.DependencyInjection;
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
    public void TheFirstScreenShowsThePipelineAndWhatItNeeds()
    {
        var cut = Render<Home>();

        Assert.Equal(new[] { "Build", "test", "prod" }, cut.FindAll(".flow li strong").Select(e => e.TextContent));
        var needs = cut.Find(".needs-list").TextContent;
        Assert.Contains("Environment with your servers registered", needs);
        Assert.Contains("with an approval", needs);
        Assert.Empty(cut.FindAll(".needs-list li[data-need='Variable']"));
    }

    [Fact]
    public void ChoosingATemplateShowsTheVariablesItNeeds()
    {
        var cut = Render<Home>();

        cut.Find("button[data-template='linux-service']").Click();

        Assert.Contains("applied", cut.Find("button[data-template='linux-service']").ClassList);
        var variables = cut.FindAll(".needs-list li[data-need='Variable'] code").Select(e => e.TextContent).ToList();
        Assert.Equal(new[] { "DEPLOY_PATH", "SERVICE_NAME" }, variables);

        // Filling in the fields removes the need for the variables.
        GoTo(cut, WizardStep.Target);
        cut.Find("#service").Change("orders");
        cut.Find("#install-path").Change("/opt/orders");
        GoTo(cut, WizardStep.Result);
        Assert.Empty(cut.FindAll(".needs-list li[data-need='Variable']"));
        Assert.Contains("service='orders'", cut.Find(".yaml-preview").TextContent);
    }

    [Fact]
    public void ChoosingATemplateChangesTheGeneratedPipeline()
    {
        var cut = Render<Home>();

        cut.Find("button[data-template='aks-deploy']").Click();

        GoTo(cut, WizardStep.Result);
        var yaml = cut.Find(".yaml-preview").TextContent;
        Assert.Contains("KubernetesManifest@1", yaml);
        Assert.DoesNotContain("IISWebAppDeploymentOnMachineGroup@0", yaml);
    }

    [Fact]
    public void ADockerTemplateNamesTheImageAfterThePipeline()
    {
        var cut = Render<Home>();
        cut.Find("#pipeline-name").Change("Orders API");

        cut.Find("button[data-template='docker-build-push']").Click();

        Assert.Equal("orders-api", cut.Find("#image-name").GetAttribute("value"));
        GoTo(cut, WizardStep.Result);
        Assert.Contains("image='$(DOCKER_REGISTRY)/orders-api:$(Build.BuildId)'", cut.Find(".yaml-preview").TextContent);
    }

    [Fact]
    public void RollingIsOnlyOfferedForServers()
    {
        var cut = Render<Home>();
        GoTo(cut, WizardStep.Target);
        Assert.NotEmpty(cut.FindAll("#rolling"));

        GoTo(cut, WizardStep.Start);
        cut.Find("button[data-template='aks-deploy']").Click();
        GoTo(cut, WizardStep.Target);

        Assert.Empty(cut.FindAll("#rolling"));
    }

    [Fact]
    public void DownloadButtonSendsTheYamlToTheBrowser()
    {
        var cut = Render<Home>();
        GoTo(cut, WizardStep.Result);

        cut.Find("#download-yaml").Click();

        var invocation = JSInterop.VerifyInvoke("pipelineBuilder.downloadText");
        Assert.Equal(ResultStep.PipelineFileName, invocation.Arguments[0]);
        Assert.Contains("stages:", (string)invocation.Arguments[1]!);
        Assert.Contains("Downloaded azure-pipelines.yml", cut.Find("[role=status]").TextContent);
    }

    [Fact]
    public void CopyButtonUsesTheClipboard()
    {
        var cut = Render<Home>();
        GoTo(cut, WizardStep.Result);

        cut.Find("#copy-yaml").Click();

        JSInterop.VerifyInvoke("pipelineBuilder.copyText");
        Assert.Contains("copied", cut.Find("[role=status]").TextContent);
    }

    [Fact]
    public void EditsSurviveJumpingBetweenStepsAndReachTheYaml()
    {
        var cut = Render<Home>();
        cut.Find("#pipeline-name").Change("orders-api");

        GoTo(cut, WizardStep.Target);
        cut.Find("#environments").Change("dev, prod");
        GoTo(cut, WizardStep.Start);
        Assert.Equal("orders-api", cut.Find("#pipeline-name").GetAttribute("value"));

        GoTo(cut, WizardStep.Result);
        var yaml = cut.Find(".yaml-preview").TextContent;
        Assert.Contains("# Pipeline: orders-api", yaml);
        Assert.Contains("- stage: Deploy_dev", yaml);
        Assert.DoesNotContain("Deploy_test", yaml);
    }

    [Fact]
    public void KeyVaultCanBeTurnedOnAndOff()
    {
        var cut = Render<Home>();
        GoTo(cut, WizardStep.Target);

        cut.Find("#keyvault-enabled").Change(true);
        cut.Find("#kv-name").Change("kv-orders");
        GoTo(cut, WizardStep.Result);
        Assert.Contains("AzureKeyVault@2", cut.Find(".yaml-preview").TextContent);

        GoTo(cut, WizardStep.Target);
        cut.Find("#keyvault-enabled").Change(false);
        GoTo(cut, WizardStep.Result);
        Assert.DoesNotContain("AzureKeyVault@2", cut.Find(".yaml-preview").TextContent);
    }

    [Fact]
    public void AVariableGroupCanBeAdded()
    {
        var cut = Render<Home>();
        GoTo(cut, WizardStep.Target);

        cut.Find("#add-variable-group").Click();

        GoTo(cut, WizardStep.Result);
        Assert.Contains("- group: 'vg-my-app'", cut.Find(".yaml-preview").TextContent);
        Assert.Contains("vg-my-app", cut.Find(".needs-list li[data-need='VariableGroup']").TextContent);
    }

    [Fact]
    public void InvalidSettingsShowErrorsInsteadOfCrashing()
    {
        var cut = Render<Home>();
        GoTo(cut, WizardStep.Target);
        cut.Find("#environments").Change("Bad Name");

        GoTo(cut, WizardStep.Result);

        Assert.Contains("invalid characters", cut.Find(".generation-error").TextContent);
    }

    [Fact]
    public void StepShowsItsOwnErrorsAndTheNavigationFlagsIt()
    {
        var cut = Render<Home>();
        GoTo(cut, WizardStep.Target);

        cut.Find("#environments").Change("Bad Name");

        Assert.Contains("invalid characters", cut.Find(".issue-list").TextContent);
        Assert.NotNull(cut.Find($"button[data-step='{WizardStep.Target}'] .badge"));
        Assert.Empty(cut.FindAll($"button[data-step='{WizardStep.Start}'] .badge"));
    }

    [Fact]
    public void GoToLinkOpensTheStepWithTheProblem()
    {
        var cut = Render<Home>();
        GoTo(cut, WizardStep.Target);
        cut.Find("#environments").Change("Bad Name");
        GoTo(cut, WizardStep.Result);

        cut.Find($"button[data-go-to='{WizardStep.Target}']").Click();

        Assert.Contains("active", cut.Find($"button[data-step='{WizardStep.Target}']").ClassList);
        Assert.NotNull(cut.Find("#environments"));
    }

    [Fact]
    public void SettingsCanBeDownloaded()
    {
        var cut = Render<Home>();
        GoTo(cut, WizardStep.Result);

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
        GoTo(cut, WizardStep.Result);
        Assert.Contains("- stage: Deploy_qa", cut.Find(".yaml-preview").TextContent);
    }

    [Fact]
    public void OpeningAnInvalidFileShowsAnError()
    {
        var cut = Render<Home>();

        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("{ nope", "settings.json"));

        Assert.Contains("Could not open the file", cut.Markup);
        Assert.Equal("my-app", cut.Find("#pipeline-name").GetAttribute("value"));
    }

    private static void GoTo(IRenderedComponent<Home> cut, WizardStep step) =>
        cut.Find($"button[data-step='{step}']").Click();
}
