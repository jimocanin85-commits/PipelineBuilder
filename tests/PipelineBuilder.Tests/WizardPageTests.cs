using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using PipelineBuilder.Core.DependencyInjection;
using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Services;
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

        Assert.Equal(new[] { "Build", "test", "preprod", "prod" }, cut.FindAll(".flow li strong").Select(e => e.TextContent));
        var needs = cut.Find(".needs-list").TextContent;
        Assert.Contains("Environment with your servers registered", needs);
        Assert.Contains("with an approval", needs);
        Assert.Empty(cut.FindAll(".needs-list li[data-need='Variable']"));
        Assert.Contains("Pipelines → Environments", cut.Find(".needs-list li.where").TextContent);
    }

    [Fact]
    public void TheFlowIsTestPreprodProdAndPreprodCanBeSkipped()
    {
        var cut = Render<Home>();
        GoTo(cut, WizardStep.Target);
        Assert.Equal(new[] { "Build", "test", "preprod", "prod" }, cut.FindAll(".stages .flow li strong").Select(e => e.TextContent));

        cut.Find("#skip-preprod").Change(true);
        Assert.Equal(new[] { "Build", "test", "prod" }, cut.FindAll(".stages .flow li strong").Select(e => e.TextContent));
        Assert.Equal("test, prod", cut.Find("#environments").GetAttribute("value"));
        GoTo(cut, WizardStep.Result);
        Assert.DoesNotContain("preprod", cut.Find(".yaml-preview").TextContent);

        GoTo(cut, WizardStep.Target);
        cut.Find("#skip-preprod").Change(false);
        GoTo(cut, WizardStep.Result);
        Assert.Contains("  - 'test'\n  - 'preprod'\n  - 'prod'", cut.Find(".yaml-preview").TextContent.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void ChoosingATemplateKeepsTheFlow()
    {
        var cut = Render<Home>();
        GoTo(cut, WizardStep.Target);
        cut.Find("#skip-preprod").Change(true);
        GoTo(cut, WizardStep.Start);

        cut.Find("button[data-template='linux-service']").Click();

        Assert.Equal(new[] { "Build", "test", "prod" }, cut.FindAll(".flow li strong").Select(e => e.TextContent));
    }

    [Fact]
    public void AProblemUnderMoreSettingsOpensTheSection()
    {
        var cut = Render<Home>();
        GoTo(cut, WizardStep.Target);
        Assert.False(cut.Find("details.more").HasAttribute("open"));

        cut.Find("#environments").Change("Bad Name");

        Assert.True(cut.Find("details.more").HasAttribute("open"));
    }

    /// <summary>A label (or aria-label) is what a screen reader reads out, and what a click on the text focuses.</summary>
    [Theory]
    [MemberData(nameof(TemplateIds))]
    public void EveryInputHasALabel(string templateId)
    {
        var cut = Render<Home>();
        cut.Find($"button[data-template='{templateId}']").Click();

        foreach (var step in Enum.GetValues<WizardStep>())
        {
            GoTo(cut, step);
            if (step == WizardStep.Target)
            {
                cut.Find("#add-variable-group").Click();
                cut.Find("#self-hosted").Change(true);
                cut.Find("#keyvault-enabled").Change(true);
            }
            if (step == WizardStep.Safety)
            {
                cut.Find("#add-health-check").Click();
                cut.Find("#add-notification").Click();
            }

            foreach (var input in cut.FindAll(".wizard-content input, .wizard-content select, .wizard-content textarea"))
            {
                var id = input.GetAttribute("id");
                var labelled = input.HasAttribute("aria-label")
                    || input.Closest("label") != null
                    || (id != null && cut.FindAll($"label[for='{id}']").Count > 0);
                Assert.True(labelled, $"An input on the {step} step has no label: {input.OuterHtml}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllSteps))]
    public void ThePipelineIsInViewOnEveryStep(WizardStep step)
    {
        var cut = Render<Home>();

        GoTo(cut, step);

        Assert.Equal(new[] { "Build", "test", "preprod", "prod" }, cut.FindAll(".stages .flow li strong").Select(e => e.TextContent));
        Assert.NotEmpty(cut.FindAll(".wizard-side .needs-list li[data-need='Environment']"));
    }

    [Fact]
    public void ThePipelinePanelFollowsTheSafetyChoices()
    {
        var cut = Render<Home>();
        GoTo(cut, WizardStep.Safety);
        Assert.Contains("A failed deployment is rolled back", cut.Find(".stages").TextContent);
        Assert.Contains("Approval", cut.Find(".stages .flow .pill").TextContent);
        Assert.Contains("Only from main", cut.Find(".stages .flow").TextContent);

        cut.Find("#rollback-enabled").Change(false);
        cut.Find("#add-health-check").Click();
        cut.Find(".card input[placeholder^='https://']").Change("https://my-app.contoso.com/health");

        Assert.DoesNotContain("rolled back", cut.Find(".stages").TextContent);
        Assert.Contains("Deploy, then health check", cut.Find(".stages .flow").TextContent);
    }

    [Fact]
    public void ThePipelinePanelSaysWhatToDoWhenTheSettingsAreInvalid()
    {
        var cut = Render<Home>();
        GoTo(cut, WizardStep.Target);

        cut.Find("#environments").Change("Bad Name");

        Assert.Empty(cut.FindAll(".stages .flow"));
        Assert.Contains("Fix the settings marked with !", cut.Find(".stages").TextContent);
        Assert.Empty(cut.FindAll(".wizard-side .needs-list"));
    }

    [Fact]
    public void TemplatesAreGroupedByWhereTheAppRuns()
    {
        var cut = Render<Home>();

        Assert.Equal(new[] { "Windows servers", "Linux servers", "Containers", "Your own tooling" },
            cut.FindAll("h3.group").Select(e => e.TextContent));
        Assert.Equal(TemplateCatalogue.LoadBuiltIn().Count, cut.FindAll("button[data-template]").Count);
        Assert.Equal(new[] { "iis-onprem", "windows-service-onprem", "windows-files" },
            cut.FindAll(".template-grid")[0].QuerySelectorAll("button").Select(b => b.GetAttribute("data-template")));
    }

    [Fact]
    public void EveryTemplateHasAnIconAndTheChosenOneIsMarked()
    {
        var cut = Render<Home>();

        Assert.Equal(TemplateCatalogue.LoadBuiltIn().Count, cut.FindAll("button[data-template] .icon svg").Count);
        Assert.Equal("iis-onprem", Assert.Single(cut.FindAll("button.template.applied")).GetAttribute("data-template"));
    }

    [Fact]
    public void ThePipelinePanelSaysWhetherThePipelineIsReady()
    {
        var cut = Render<Home>();
        Assert.Equal("Ready", cut.Find(".stages .chip").TextContent);

        GoTo(cut, WizardStep.Target);
        cut.Find("#environments").Change("Bad Name");

        Assert.Equal("Not ready", cut.Find(".stages .chip").TextContent);
    }

    [Fact]
    public void AChoiceShowsAllItsOptionsAndMarksTheChosenOne()
    {
        var cut = Render<Home>();
        GoTo(cut, WizardStep.Target);

        Assert.Equal(new[] { "run-from-Server", "run-from-Agent" }, cut.FindAll("#run-from input[type=radio]").Select(e => e.Id));
        Assert.True(cut.Find("#run-from-Server").HasAttribute("checked"));

        cut.Find("#run-from-Agent").Change(true);

        Assert.True(cut.Find("#run-from-Agent").HasAttribute("checked"));
        Assert.False(cut.Find("#run-from-Server").HasAttribute("checked"));
    }

    [Fact]
    public void TheNextButtonSaysWhereItLeads()
    {
        var cut = Render<Home>();
        Assert.Equal("Next: Where", cut.Find("#next").TextContent);
        Assert.True(cut.Find("#back").HasAttribute("disabled"));

        cut.Find("#next").Click();
        cut.Find("#next").Click();
        Assert.Equal("Next: Result", cut.Find("#next").TextContent);

        cut.Find("#next").Click();
        Assert.True(cut.Find("#next").HasAttribute("disabled"));
        cut.Find("#back").Click();
        Assert.Contains("active", cut.Find($"button[data-step='{WizardStep.Safety}']").ClassList);
    }

    [Fact]
    public void TheYamlIsShownLineByLineWithItsCommentsSetApart()
    {
        var cut = Render<Home>();
        GoTo(cut, WizardStep.Result);

        var lines = cut.FindAll(".yaml-preview .line");
        Assert.StartsWith("# Made with PipelineBuilder", lines[0].TextContent);
        Assert.Contains("comment", lines[0].ClassList);
        Assert.Contains(lines, line => line.TextContent == "trigger:" && !line.ClassList.Contains("comment"));

        // The text is the file itself: nothing added, nothing lost.
        cut.Find("#copy-yaml").Click();
        var copied = (string)JSInterop.VerifyInvoke("pipelineBuilder.copyText").Arguments[0]!;
        Assert.Equal(copied.ReplaceLineEndings("\n"), cut.Find(".yaml-preview").TextContent);
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
        Assert.Contains("Copied", cut.Find("[role=status]").TextContent);
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
        Assert.Contains("  - 'dev'\n  - 'prod'", yaml.ReplaceLineEndings("\n"));
        Assert.DoesNotContain("  - 'test'", yaml);
    }

    [Fact]
    public void KeyVaultCanBeTurnedOnAndOff()
    {
        var cut = Render<Home>();
        GoTo(cut, WizardStep.Target);

        cut.Find("#keyvault-enabled").Change(true);
        cut.Find("#kv-name").Change("kv-orders");
        // With no names the pipeline would get every secret in the vault, and the wizard says so.
        Assert.Contains("every secret in the Key Vault", cut.Find(".issue-list").TextContent);

        cut.Find("#kv-secrets").Change("DbPassword, ApiKey");
        Assert.DoesNotContain("every secret in the Key Vault", cut.Find(".wizard-content").TextContent);
        Assert.Equal("DbPassword, ApiKey", cut.Find("#kv-secrets").GetAttribute("value"));
        GoTo(cut, WizardStep.Result);
        Assert.Contains("AzureKeyVault@2", cut.Find(".yaml-preview").TextContent);
        Assert.Contains("SecretsFilter: 'DbPassword,ApiKey'", cut.Find(".yaml-preview").TextContent);
        Assert.Contains("DbPassword", cut.Find(".needs-list").TextContent);

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

        Assert.Contains("is not a valid name", cut.Find(".generation-error").TextContent);
    }

    [Fact]
    public void StepShowsItsOwnErrorsAndTheNavigationFlagsIt()
    {
        var cut = Render<Home>();
        GoTo(cut, WizardStep.Target);

        cut.Find("#environments").Change("Bad Name");

        Assert.Contains("is not a valid name", cut.Find(".issue-list").TextContent);
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
        Assert.Contains("  - 'qa'", cut.Find(".yaml-preview").TextContent);
    }

    [Fact]
    public void OpeningAnInvalidFileShowsAnError()
    {
        var cut = Render<Home>();

        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("{ nope", "settings.json"));

        Assert.Contains("Could not open the file", cut.Markup);
        Assert.Equal("my-app", cut.Find("#pipeline-name").GetAttribute("value"));
    }

    public static IEnumerable<object[]> TemplateIds() => TemplateCatalogue.LoadBuiltIn().Select(t => new object[] { t.Id });

    [Theory]
    [MemberData(nameof(TemplateIds))]
    public void EveryTemplateWorksThroughAllSteps(string templateId)
    {
        var cut = Render<Home>();

        cut.Find($"button[data-template='{templateId}']").Click();

        foreach (var step in Enum.GetValues<WizardStep>())
        {
            GoTo(cut, step);
            Assert.DoesNotContain("generation-error", cut.Markup);
        }
        Assert.Contains("stages:", cut.Find(".yaml-preview").TextContent);
    }

    [Theory]
    [InlineData("own-script", "#custom-script")]
    [InlineData("windows-files", "#share")]
    public void FilesAndOwnScriptCanBeChosen(string templateId, string field)
    {
        var cut = Render<Home>();

        cut.Find($"button[data-template='{templateId}']").Click();
        GoTo(cut, WizardStep.Target);

        Assert.NotNull(cut.Find(field));
    }

    [Fact]
    public void AnEmptyOwnScriptSaysWhereToWriteIt()
    {
        var cut = Render<Home>();
        cut.Find("button[data-template='own-script']").Click();
        GoTo(cut, WizardStep.Target);
        Assert.Contains("Deploy script", cut.Find(".issue-list").TextContent);

        cut.Find("#custom-script").Change("Write-Host 'deploying'");

        Assert.Empty(cut.FindAll(".issue-list"));
    }

    [Fact]
    public void ACustomScriptChoosesItsOwnRollbackTarget()
    {
        var saved = WizardState.CreateDefault();
        saved.Deployment = new DeploymentConfig { Kind = DeploymentKind.Custom, ServerOs = ServerOs.Linux, CustomScript = "./deploy.sh" };
        var cut = Render<Home>();
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText(PipelineDefinitionSerializer.ToJson(saved), "settings.json"));

        GoTo(cut, WizardStep.Safety);
        cut.Find("#rollback-target").Change(RollbackTarget.LinuxService.ToString());
        GoTo(cut, WizardStep.Result);

        var yaml = cut.Find(".yaml-preview").TextContent;
        Assert.Contains("./deploy.sh", yaml);
        Assert.Contains("Roll back Linux service", yaml);
    }

    [Fact]
    public void RollingUpdatesTheServersInBatches()
    {
        var cut = Render<Home>();
        GoTo(cut, WizardStep.Target);

        cut.Find("#rolling").Change(true);
        cut.Find("#batch").Change("2");
        GoTo(cut, WizardStep.Result);

        var yaml = cut.Find(".yaml-preview").TextContent;
        Assert.Contains("rolling:", yaml);
        Assert.Contains("maxParallel: 2", yaml);
    }

    [Fact]
    public void ASelfHostedPoolNeedsAName()
    {
        var cut = Render<Home>();
        GoTo(cut, WizardStep.Target);

        cut.Find("#self-hosted").Change(true);
        Assert.Contains("Type the name of your agent pool", cut.Find(".issue-list").TextContent);

        cut.Find("#pool-name").Change("OnPremAgents");
        GoTo(cut, WizardStep.Result);
        Assert.Contains("name: 'OnPremAgents'", cut.Find(".yaml-preview").TextContent);
    }

    [Fact]
    public void AVariableGroupCanBeLimitedToOneEnvironmentAndRemoved()
    {
        var cut = Render<Home>();
        GoTo(cut, WizardStep.Target);
        cut.Find("#add-variable-group").Click();

        cut.Find(".card.row select").Change("prod");
        GoTo(cut, WizardStep.Result);
        var yaml = cut.Find(".yaml-preview").TextContent;
        Assert.DoesNotContain("vg-my-app", yaml.Split("- stage: Deploy_")[0]);
        Assert.Contains("${{ if eq(environment, 'prod') }}:\n      variables:\n        - group: 'vg-my-app'", yaml.ReplaceLineEndings("\n"));

        GoTo(cut, WizardStep.Target);
        cut.Find(".card.row select").Change("");
        cut.Find(".card.row .btn-link").Click();
        GoTo(cut, WizardStep.Result);
        Assert.DoesNotContain("vg-my-app", cut.Find(".yaml-preview").TextContent);
    }

    [Fact]
    public void HealthChecksAndNotificationsCanBeAddedAndRemoved()
    {
        var cut = Render<Home>();
        GoTo(cut, WizardStep.Safety);

        cut.Find("#add-health-check").Click();
        Assert.Contains("needs a full address", cut.Find(".issue-list").TextContent);
        cut.Find(".card input[placeholder^='https://']").Change("https://my-app-{environment}.contoso.com/health");
        cut.Find("#add-notification").Click();
        GoTo(cut, WizardStep.Result);

        Assert.Contains("$uri = 'https://my-app-${{ environment }}.contoso.com/health'", cut.Find(".yaml-preview").TextContent);
        Assert.Contains("Notify", cut.FindAll(".flow li strong").Select(e => e.TextContent));
        Assert.Contains("Teams", cut.Find(".flow").TextContent);
        Assert.Contains("secret", cut.Find(".needs-list").TextContent);

        GoTo(cut, WizardStep.Safety);
        cut.Find(".card .btn-link").Click();
        cut.Find(".card .btn-link").Click();
        GoTo(cut, WizardStep.Result);
        Assert.DoesNotContain("health check", cut.Find(".yaml-preview").TextContent);
        Assert.DoesNotContain("Notify", cut.Find(".flow").TextContent);
    }

    [Fact]
    public void EachHealthCheckTypeShowsItsOwnFields()
    {
        var cut = Render<Home>();
        GoTo(cut, WizardStep.Safety);
        cut.Find("#add-health-check").Click();
        const string type = "select[aria-label='Health check type']";

        cut.Find(type).Change(HealthCheckType.PortCheck.ToString());
        Assert.NotEmpty(cut.FindAll(".card input[placeholder='80']"));

        cut.Find(type).Change(HealthCheckType.WindowsService.ToString());
        Assert.NotEmpty(cut.FindAll(".card input[placeholder='W3SVC']"));

        cut.Find(type).Change(HealthCheckType.IisAppPool.ToString());
        Assert.NotEmpty(cut.FindAll(".card input[placeholder='DefaultAppPool']"));

        cut.Find(type).Change(HealthCheckType.CustomPowerShell.ToString());
        cut.Find(".card textarea").Change("exit 0");
        GoTo(cut, WizardStep.Result);
        Assert.Contains("Custom health check", cut.Find(".yaml-preview").TextContent);
    }

    [Fact]
    public void EachNotificationTypeShowsItsOwnFields()
    {
        var cut = Render<Home>();
        GoTo(cut, WizardStep.Safety);
        cut.Find("#add-notification").Click();
        const string type = "select[aria-label='Notification type']";

        cut.Find(type).Change(NotificationType.CustomWebhook.ToString());
        Assert.NotEmpty(cut.FindAll(".card input[placeholder='CUSTOM_WEBHOOK_URL']"));

        cut.Find(type).Change(NotificationType.Email.ToString());
        cut.Find(".card input.form-control").Change("ops@contoso.com, dev@contoso.com");
        GoTo(cut, WizardStep.Result);

        Assert.Contains("Email", cut.Find(".flow").TextContent);
        Assert.Contains("'ops@contoso.com', 'dev@contoso.com'", cut.Find(".yaml-preview").TextContent);
        Assert.Contains("SMTP_HOST", cut.Find(".needs-list").TextContent);
    }

    [Fact]
    public void RollbackCanBeTurnedOff()
    {
        var cut = Render<Home>();
        GoTo(cut, WizardStep.Safety);
        Assert.NotEmpty(cut.FindAll("#backup-path"));

        cut.Find("#rollback-enabled").Change(false);

        Assert.Empty(cut.FindAll("#backup-path"));
        GoTo(cut, WizardStep.Result);
        Assert.DoesNotContain("Back up", cut.Find(".yaml-preview").TextContent);
    }

    [Fact]
    public void DockerAndNodeTemplatesShowTheirBuildFields()
    {
        var cut = Render<Home>();

        cut.Find("button[data-template='node-linux-service']").Click();
        Assert.NotEmpty(cut.FindAll("#node-version"));
        Assert.Empty(cut.FindAll("#project-path"));

        cut.Find("button[data-template='hybrid-dotnet-docker']").Click();
        Assert.NotEmpty(cut.FindAll("#dockerfile"));
        Assert.NotEmpty(cut.FindAll("#project-path"));

        GoTo(cut, WizardStep.Target);
        cut.Find("#container-ports").Change("8080:80");
        cut.Find("#container-env").Change("ASPNETCORE_ENVIRONMENT=Production");
        cut.Find("#server-os-Windows").Change(true);
        GoTo(cut, WizardStep.Result);
        Assert.Contains("-p '8080:80' -e 'ASPNETCORE_ENVIRONMENT=Production' $image", cut.Find(".yaml-preview").TextContent);
    }

    private static void GoTo(IRenderedComponent<Home> cut, WizardStep step) =>
        cut.Find($"button[data-step='{step}']").Click();

    // The steps are tabs, as in the Azure portal: the current one is marked, and the ones before it are done.
    [Fact]
    public void TheStepsAreTabsThatShowWhereYouAre()
    {
        var cut = Render<Home>();
        Assert.Equal(new[] { "What", "Where", "Safety", "Result" }, cut.FindAll(".wizard-nav .step-title").Select(e => e.TextContent));

        GoTo(cut, WizardStep.Safety);

        Assert.Equal(new[] { "done", "done", "active", "" }, cut.FindAll(".wizard-nav .step-btn").Select(tab =>
            tab.ClassList.Contains("active") ? "active" : tab.ClassList.Contains("done") ? "done" : ""));
        Assert.Equal("step", cut.Find($"button[data-step='{WizardStep.Safety}']").GetAttribute("aria-current"));
    }

    // Previous and Next sit in a bar below the step; on the first and last step the one that leads nowhere is off.
    [Fact]
    public void PreviousAndNextSayWhereTheyLead()
    {
        var cut = Render<Home>();
        Assert.True(cut.Find(".wizard-footer #back").HasAttribute("disabled"));
        Assert.Equal("Next: Where", cut.Find(".wizard-footer #next").TextContent);

        cut.Find("#next").Click();
        Assert.Equal("Previous", cut.Find("#back").TextContent);
        Assert.False(cut.Find("#back").HasAttribute("disabled"));
        Assert.Equal("Next: Safety", cut.Find("#next").TextContent);

        GoTo(cut, WizardStep.Result);
        Assert.True(cut.Find("#next").HasAttribute("disabled"));
    }

    // The pipeline is drawn as stage boxes, each with a small drawing of what it does.
    [Fact]
    public void EveryStageInThePipelineHasItsDrawing()
    {
        var cut = Render<Home>();
        GoTo(cut, WizardStep.Safety);
        cut.Find("#add-notification").Click();

        var stages = cut.FindAll(".stages .flow li:not(.gate)");

        Assert.Equal(new[] { "Build", "test", "preprod", "prod", "Notify" }, stages.Select(stage => stage.QuerySelector("strong")!.TextContent));
        Assert.All(stages, stage => Assert.NotNull(stage.QuerySelector(".icon svg")));
    }

    // The pane beside the form can show the file itself, and marks what a setting just changed in it.
    [Fact]
    public void TheFileCanBeShownBesideTheFormAndMarksWhatASettingChanged()
    {
        var cut = Render<Home>();
        GoTo(cut, WizardStep.Target);
        Assert.Empty(cut.FindAll(".yaml-live"));
        Assert.Equal("true", cut.Find(".side-tab[data-tab='overview']").GetAttribute("aria-selected"));

        cut.Find(".side-tab[data-tab='file']").Click();

        Assert.Equal("true", cut.Find(".side-tab[data-tab='file']").GetAttribute("aria-selected"));
        Assert.Empty(cut.FindAll(".wizard-side .needs-list"));
        Assert.Contains("stages:", cut.Find(".yaml-live").TextContent);
        Assert.Empty(cut.FindAll(".yaml-live .line.changed")); // opening the file is not a change

        cut.Find("#approve-preprod").Change(true);

        var changed = cut.FindAll(".yaml-live .line.changed").Select(line => line.TextContent.Trim()).ToList();
        Assert.Contains("- job: Approve", changed);
        Assert.Contains("dependsOn: Approve", changed);
        Assert.DoesNotContain("stages:", changed);

        Assert.Empty(cut.FindAll(".wizard-side h3")); // the tab already names the list

        // The Result step shows the file itself, so there the pane goes back to the list.
        GoTo(cut, WizardStep.Result);
        Assert.Equal("Needs in Azure DevOps", cut.Find(".wizard-side h3").TextContent);
        Assert.Empty(cut.FindAll(".side-tab"));
        Assert.Empty(cut.FindAll(".yaml-live"));
        Assert.NotEmpty(cut.FindAll(".wizard-side .needs-list"));

        GoTo(cut, WizardStep.Target);
        cut.Find(".side-tab[data-tab='overview']").Click();
        Assert.NotEmpty(cut.FindAll(".wizard-side .needs-list"));
    }

    [Fact]
    public void AnApprovalIsShownAsAGateBeforeItsStageAndSaysWhereItIsSet()
    {
        var cut = Render<Home>();
        GoTo(cut, WizardStep.Target);
        cut.Find("#approve-preprod").Change(true);

        var rows = cut.FindAll(".stages .flow li").Select(row => (Gate: row.ClassList.Contains("gate"), Text: row.TextContent)).ToList();

        Assert.Equal(new[] { false, false, true, false, true, false }, rows.Select(row => row.Gate)); // Build, test, gate, preprod, gate, prod
        Assert.Contains("In the file", rows[2].Text);
        Assert.Contains("In Azure DevOps", rows[4].Text);

        cut.Find("#approve-prod").Change(true);
        Assert.Contains("In the file and in Azure DevOps", cut.FindAll(".stages .flow li.gate")[1].TextContent);
    }

    [Fact]
    public void TheCopyButtonSaysCopiedUntilTheFileChanges()
    {
        var cut = Render<Home>();
        GoTo(cut, WizardStep.Result);
        Assert.Equal("Copy", cut.Find("#copy-yaml").TextContent);

        cut.Find("#copy-yaml").Click();
        Assert.Equal("Copied", cut.Find("#copy-yaml").TextContent);

        GoTo(cut, WizardStep.Start);
        GoTo(cut, WizardStep.Result);
        Assert.Equal("Copy", cut.Find("#copy-yaml").TextContent);
    }

    [Fact]
    public void TheFileOnTheResultStepHasItsKeysAndVariablesSetApart()
    {
        var cut = Render<Home>();
        GoTo(cut, WizardStep.Result);

        Assert.Contains(cut.FindAll(".yaml-preview .k"), key => key.TextContent == "trigger");
        Assert.Contains(cut.FindAll(".yaml-preview .x"), expression => expression.TextContent == "${{ environment }}");
        Assert.Empty(cut.FindAll(".yaml-preview .changed"));
    }
}
