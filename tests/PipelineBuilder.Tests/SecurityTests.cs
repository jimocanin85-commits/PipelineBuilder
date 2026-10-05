using Microsoft.Extensions.DependencyInjection;
using PipelineBuilder.Core.Abstractions;
using PipelineBuilder.Core.DependencyInjection;
using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Services;
using PipelineBuilder.Web.State;
using Xunit;

namespace PipelineBuilder.Tests;

/// <summary>
/// What the generated pipeline must never do: deploy code nobody has reviewed, or run text from
/// outside as code in a stage that holds secrets.
/// </summary>
public class SecurityTests
{
    private readonly IPipelineGeneratorService _generator =
        new ServiceCollection().AddPipelineBuilderCore().BuildServiceProvider().GetRequiredService<IPipelineGeneratorService>();

    public static IEnumerable<object[]> TemplateIds() => TemplateCatalogue.LoadBuiltIn().Select(t => new object[] { t.Id });

    private static PipelineDefinition Template(string id)
    {
        var definition = WizardState.CreateDefault();
        Assert.True(new TemplateCatalogue().ApplyTo(id, definition));
        return definition;
    }

    private static readonly NotificationConfig[] EveryNotification =
    {
        new() { NotificationType = NotificationType.TeamsWebhook, NotifyOnSuccess = true, NotifyOnFailure = true },
        new() { NotificationType = NotificationType.CustomWebhook, WebhookUrlVariable = "OUR_HOOK", NotifyOnSuccess = true, NotifyOnFailure = true },
        new() { NotificationType = NotificationType.Email, EmailRecipients = new[] { "team@contoso.com" }, NotifyOnSuccess = true, NotifyOnFailure = true }
    };

    private const string NotPullRequest = "ne(variables['Build.Reason'], 'PullRequest')";

    [Theory]
    [MemberData(nameof(TemplateIds))]
    public void APullRequestIsNeverDeployed(string templateId)
    {
        var definition = Template(templateId);
        definition.Notifications = EveryNotification;
        var root = PipelineYaml.Parse(_generator.Generate(definition).Yaml);

        // Every environment: prod and the others each get their own condition, and both refuse a pull request.
        var deploy = PipelineYaml.DeployStage(root);
        var conditions = deploy.Where(entry => ((string)entry.Key).StartsWith("${{ if ", StringComparison.Ordinal))
            .Select(entry => entry.Value).OfType<Dictionary<object, object>>()
            .Where(block => block.ContainsKey("condition")).Select(block => (string)block["condition"]).ToList();
        Assert.Equal(2, conditions.Count);
        Assert.All(conditions, condition => Assert.Contains(NotPullRequest, condition));
        Assert.True(deploy.ContainsKey("${{ if notIn(environment, 'prod') }}"));
        Assert.True(deploy.ContainsKey("${{ if in(environment, 'prod') }}"));

        // The notify stages hold the webhook and mail secrets.
        foreach (var name in new[] { "Notify_Success", "Notify_Failure" })
            Assert.Contains(NotPullRequest, (string)PipelineYaml.Stage(root, name)["condition"]);
    }

    [Fact]
    public void WithoutAProdEnvironmentEveryStageStillRefusesAPullRequest()
    {
        var definition = WizardState.CreateDefault();
        definition.Environments = new[] { "dev", "staging" };

        var deploy = PipelineYaml.DeployStage(PipelineYaml.Parse(_generator.Generate(definition).Yaml));

        Assert.Equal($"and(succeeded(), {NotPullRequest})", (string)deploy["condition"]);
    }

    [Fact]
    public void NotificationScriptsDoNotPasteTextFromTheRunIntoTheScript()
    {
        var definition = WizardState.CreateDefault();
        definition.Notifications = EveryNotification;
        var yaml = _generator.Generate(definition).Yaml;
        var notify = yaml[yaml.IndexOf("- stage: Notify_Success", StringComparison.Ordinal)..];

        // A build can change its own number, and a pipeline can be renamed. Pasted into a script with
        // $(...), that text would run as code; read from the environment it is only ever text.
        foreach (var pasted in new[] { "$(Build.BuildNumber)", "$(Build.DefinitionName)", "$(System.TeamProject)", "$(Build.SourceBranch", "$(Build.SourceVersionMessage)", "$(Build.RequestedFor" })
            Assert.DoesNotContain(pasted, notify);
        Assert.Contains("$env:BUILD_BUILDNUMBER", notify);
        Assert.Contains("$env:BUILD_DEFINITIONNAME", notify);

        // Secrets reach a script through its environment, never as part of its text.
        Assert.Contains("$webhook = $env:WEBHOOK_URL", notify);
        Assert.Contains("WEBHOOK_URL: '$(TEAMS_WEBHOOK_URL)'", notify);
        Assert.Contains("WEBHOOK_URL: '$(OUR_HOOK)'", notify);
        Assert.Contains("SMTP_PASSWORD: '$(SMTP_PASSWORD)'", notify);
        Assert.DoesNotContain("$webhook = '$(", notify);
    }

    [Theory]
    [InlineData("drop'; rm -rf /; '")]
    [InlineData("my app")]
    [InlineData("../../etc")]
    [InlineData("a$(whoami)")]
    [InlineData("a`b")]
    public void AnArtifactNameWithUnsafeCharactersIsRefused(string name)
    {
        var definition = WizardState.CreateDefault();
        definition.Artifact.ArtifactName = name;

        var error = Assert.Throws<ArgumentException>(() => _generator.Generate(definition));

        Assert.Contains("can only have letters, digits", error.Message);
    }

    [Theory]
    [InlineData("drop")]
    [InlineData("orders-api")]
    [InlineData("team/orders.api_v2")]
    public void AnOrdinaryArtifactNameIsAccepted(string name)
    {
        var definition = WizardState.CreateDefault();
        definition.Artifact.ArtifactName = name;

        Assert.NotEmpty(_generator.Generate(definition).Yaml);
    }

    [Theory]
    [MemberData(nameof(TemplateIds))]
    public void ACopyNeverGoesToAFolderThatIsNotSet(string templateId)
    {
        var yaml = _generator.Generate(Template(templateId)).Yaml;

        // Every script that defines a copy function also guards it.
        var functions = System.Text.RegularExpressions.Regex.Matches(yaml, @"function Sync-Folder|sync_folder\(\) \{").Count;
        var guards = System.Text.RegularExpressions.Regex.Matches(yaml, "The folder to copy to is not set").Count;
        Assert.Equal(functions, guards);
    }
}
