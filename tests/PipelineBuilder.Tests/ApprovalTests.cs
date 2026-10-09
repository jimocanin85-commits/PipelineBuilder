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
/// An approval written in the pipeline file: before deploying to a chosen environment, the pipeline
/// waits until a person presses Resume.
/// </summary>
public class ApprovalTests : BunitContext
{
    private readonly IPipelineGeneratorService _generator =
        new ServiceCollection().AddPipelineBuilderCore().BuildServiceProvider().GetRequiredService<IPipelineGeneratorService>();

    public ApprovalTests()
    {
        Services.AddPipelineBuilderCore();
        Services.AddScoped<WizardState>();
        // The page keeps the wizard in the browser's storage; here nothing is stored, and nothing is there.
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static PipelineDefinition WithApproval(params string[] environments)
    {
        var definition = WizardState.CreateDefault();
        definition.Approval.Environments = environments;
        return definition;
    }

    private string Yaml(PipelineDefinition definition) => _generator.Generate(definition).Yaml.ReplaceLineEndings("\n");

    [Fact]
    public void WithoutApprovalsNothingIsAdded()
    {
        var yaml = Yaml(WizardState.CreateDefault());

        Assert.DoesNotContain("ManualValidation", yaml);
        Assert.DoesNotContain("pool: server", yaml);
        Assert.DoesNotContain("dependsOn: Approve", yaml);
    }

    [Fact]
    public void TheStageWaitsForApprovalBeforeItDeploys()
    {
        var yaml = Yaml(WithApproval("preprod"));

        Assert.Contains("    # A person must approve before this stage deploys to preprod.\n" +
                        "    - ${{ if in(environment, 'preprod') }}:\n" +
                        "      - job: Approve\n" +
                        "        displayName: 'Wait for approval'\n" +
                        "        pool: server\n" +
                        "        timeoutInMinutes: 1445\n", yaml);
        Assert.Contains("        - task: ManualValidation@0\n" +
                        "          displayName: 'Approve the deployment to ${{ environment }}'\n" +
                        "          timeoutInMinutes: 1440\n" +
                        "          inputs:\n" +
                        "            notifyUsers: ''\n" +
                        "            instructions: 'Resume to deploy build $(Build.BuildNumber) to ${{ environment }}. Reject to stop here.'\n" +
                        "            onTimeout: 'reject'\n", yaml);
        Assert.Contains("    - deployment: Deploy\n" +
                        "      displayName: 'Deploy to ${{ environment }}'\n" +
                        "      ${{ if in(environment, 'preprod') }}:\n" +
                        "        dependsOn: Approve\n", yaml);
        Assert.DoesNotContain("approvers:", yaml);
    }

    [Fact]
    public void TheApprovalJobSitsInTheDeployStageNextToTheDeployJob()
    {
        var root = PipelineYaml.Parse(Yaml(WithApproval("preprod")));

        var jobs = PipelineYaml.Jobs(PipelineYaml.DeployStage(root));

        Assert.Equal(2, jobs.Count);
        var conditional = Assert.IsType<List<object>>(jobs[0]["${{ if in(environment, 'preprod') }}"]);
        var approve = Assert.IsType<Dictionary<object, object>>(Assert.Single(conditional));
        Assert.Equal("Approve", approve["job"]);
        Assert.Equal("server", approve["pool"]);
        Assert.True(jobs[1].ContainsKey("deployment"));
    }

    [Fact]
    public void NamedApproversAndTheEmailAreWrittenInTheFile()
    {
        var definition = WithApproval("preprod");
        definition.Approval.Approvers = @" [Orders]\Release approvers ";
        definition.Approval.NotifyUsers = "lead@contoso.com, ops@contoso.com";
        definition.Approval.WaitHours = 2;

        var yaml = Yaml(definition);

        Assert.Contains("- task: ManualValidation@1", yaml); // naming approvers needs version 1 of the task
        Assert.Contains(@"approvers: '[Orders]\Release approvers'", yaml);
        Assert.Contains("notifyUsers: 'lead@contoso.com, ops@contoso.com'", yaml);
        Assert.Contains("timeoutInMinutes: 120\n", yaml);
        Assert.Contains("timeoutInMinutes: 125\n", yaml);
    }

    [Fact]
    public void SeveralEnvironmentsAreListedInDeploymentOrder()
    {
        var yaml = Yaml(WithApproval("PROD", "preprod"));

        Assert.Contains("${{ if in(environment, 'preprod', 'prod') }}:", yaml);
        Assert.Contains("deploys to preprod or prod.", yaml);
    }

    [Fact]
    public void AnApprovalForAnEnvironmentThatIsNotDeployedToIsIgnored()
    {
        var definition = WithApproval("preprod");
        definition.Environments = new[] { "test", "prod" };

        Assert.DoesNotContain("ManualValidation", Yaml(definition));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(673)]
    public void TheWaitingTimeHasLimits(int hours)
    {
        var definition = WithApproval("preprod");
        definition.Approval.WaitHours = hours;

        var ex = Assert.Throws<ArgumentException>(() => _generator.Generate(definition));
        Assert.Contains("between 1 and 672 hours", ex.Message);

        // Without an approval the number is not used, so it is not checked either.
        definition.Approval.Environments = Array.Empty<string>();
        Assert.DoesNotContain("ManualValidation", Yaml(definition));
    }

    public static IEnumerable<object[]> TemplateIds() => TemplateCatalogue.LoadBuiltIn().Select(t => new object[] { t.Id });

    [Theory]
    [MemberData(nameof(TemplateIds))]
    public void EveryTemplateStillProducesAValidPipeline(string templateId)
    {
        var definition = WithApproval("preprod", "prod");
        Assert.True(new TemplateCatalogue().ApplyTo(templateId, definition));

        var yaml = _generator.Generate(definition).Yaml; // throws when the YAML is invalid

        Assert.Contains("dependsOn: Approve", yaml);
    }

    [Fact]
    public void ApprovalSettingsAreSavedAndOpenedAgain()
    {
        var definition = WithApproval("preprod");
        definition.Approval.Approvers = "release-team";

        var opened = PipelineDefinitionSerializer.FromJson(PipelineDefinitionSerializer.ToJson(definition));

        Assert.Equal(new[] { "preprod" }, opened.Approval.Environments);
        Assert.Equal("release-team", opened.Approval.Approvers);
        Assert.Equal(24, opened.Approval.WaitHours);
    }

    [Theory]
    [InlineData("stages:\n- stage: A\n  jobs:\n  - job: X\n    ${{ if eq(1, 1) }}:\n      dependsOn: Y", "unknown job 'Y'")]
    [InlineData("stages:\n- stage: A\n  jobs:\n  - ${{ if eq(1, 1) }}:\n    - job: bad-name", "Invalid job name 'bad-name'")]
    [InlineData("stages:\n- stage: A\n  jobs:\n  - ${{ if eq(1, 1) }}:\n    - job: X\n  - job: X", "Job 'X' is defined more than once")]
    public void TheSafetyNetAlsoChecksConditionalJobs(string yaml, string expected)
    {
        Assert.Contains(GeneratedYamlValidator.Validate(yaml), problem => problem.Contains(expected));
    }

    [Fact]
    public void TheWizardOffersAnApprovalBetweenEachPairOfEnvironments()
    {
        var cut = Render<Home>();
        cut.Find($"button[data-step='{WizardStep.Safety}']").Click();

        Assert.Empty(cut.FindAll("#approve-test")); // nothing comes before the first environment
        Assert.Contains("between test and preprod", cut.Find("#approve-preprod").ParentElement!.TextContent);
        Assert.Contains("between preprod and prod", cut.Find("#approve-prod").ParentElement!.TextContent);
        Assert.Empty(cut.FindAll("#approvers"));

        cut.Find("#approve-preprod").Change(true);

        Assert.NotNull(cut.Find("#approvers"));
        Assert.Equal(new[] { "Approval", "Approval" }, cut.FindAll(".stages .flow .pill").Select(e => e.TextContent)); // preprod, and prod as before
        cut.Find("#approval-notify").Change("lead@contoso.com");
        cut.Find($"button[data-step='{WizardStep.Result}']").Click();
        var yaml = cut.Find(".yaml-preview").TextContent;
        Assert.Contains("- task: ManualValidation@0", yaml);
        Assert.Contains("notifyUsers: 'lead@contoso.com'", yaml);

        cut.Find($"button[data-step='{WizardStep.Safety}']").Click();
        cut.Find("#approve-preprod").Change(false);
        Assert.Empty(cut.FindAll("#approvers"));
        cut.Find($"button[data-step='{WizardStep.Result}']").Click();
        Assert.DoesNotContain("ManualValidation", cut.Find(".yaml-preview").TextContent);
    }

    [Fact]
    public void WithoutPreprodTheApprovalIsBetweenTestAndProd()
    {
        var cut = Render<Home>();
        cut.Find($"button[data-step='{WizardStep.Target}']").Click();
        cut.Find("#skip-preprod").Change(true);

        cut.Find($"button[data-step='{WizardStep.Safety}']").Click();

        Assert.Empty(cut.FindAll("#approve-preprod"));
        Assert.Contains("between test and prod", cut.Find("#approve-prod").ParentElement!.TextContent);
    }

    [Fact]
    public void AWaitingTimeOutsideTheLimitsIsShownOnTheStep()
    {
        var cut = Render<Home>();
        cut.Find($"button[data-step='{WizardStep.Safety}']").Click();
        cut.Find("#approve-preprod").Change(true);

        cut.Find("#approval-wait").Change("0");

        Assert.Contains("between 1 and 672 hours", cut.Find(".issue-list").TextContent);
        Assert.NotNull(cut.Find($"button[data-step='{WizardStep.Safety}'] .badge"));
    }
}
