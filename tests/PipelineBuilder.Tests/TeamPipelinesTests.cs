using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using PipelineBuilder.Core.DependencyInjection;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Web.Components.Pages;
using PipelineBuilder.Web.State;
using PipelineBuilder.Web.Storage;
using Xunit;

namespace PipelineBuilder.Tests;

/// <summary>With a database, the team saves pipelines on the Result step and opens them on the first step; all is logged.</summary>
public class TeamPipelinesTests : BunitContext
{
    private const string Anna = @"CONTOSO\anna";
    private readonly InMemoryPipelineStore _store = new();

    public TeamPipelinesTests()
    {
        Services.AddPipelineBuilderCore();
        Services.AddScoped<WizardState>();
        Services.AddSingleton<IPipelineStore>(_store);
        Services.AddSingleton<AuthenticationStateProvider>(new SignedIn(Anna));
        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.SetupVoid("pipelineBuilder.downloadText", _ => true).SetVoidResult();
        JSInterop.Setup<bool>("pipelineBuilder.copyText", _ => true).SetResult(true);
    }

    private static string Settings(string name)
    {
        var definition = WizardState.CreateDefault();
        definition.Name = name;
        return PipelineDefinitionSerializer.ToJson(definition);
    }

    private static void GoTo(IRenderedComponent<Home> cut, WizardStep step) => cut.Find($"button[data-step='{step}']").Click();

    [Fact]
    public void APipelineIsSavedForTheTeamAndLogged()
    {
        var cut = Render<Home>();
        cut.Find("#pipeline-name").Change("orders-api");
        GoTo(cut, WizardStep.Result);

        cut.Find("#save-team").Click();

        var saved = Assert.Single(_store.Saved);
        Assert.Equal(("orders-api", Anna), (saved.Name, saved.SavedBy));
        Assert.Equal("orders-api", PipelineDefinitionSerializer.FromJson(saved.Settings).Name);
        Assert.Contains("Saved 'orders-api' for the team", cut.Markup);
        Assert.Equal((Anna, ActivityKind.Saved, "orders-api"), (_store.Log[0].UserName, _store.Log[0].Action, _store.Log[0].Pipeline));
    }

    [Fact]
    public void SavingOverAColleaguesPipelineSaysWhoseItReplaced()
    {
        _store.Add("my-app", Settings("my-app"), @"CONTOSO\bob");
        var cut = Render<Home>();
        GoTo(cut, WizardStep.Result);

        cut.Find("#save-team").Click();

        Assert.Contains(@"replacing the one CONTOSO\bob saved", cut.Markup);
        Assert.Equal(Anna, Assert.Single(_store.Saved).SavedBy);
    }

    [Fact]
    public void TheTeamsPipelinesAreListedAndOpenedOnTheFirstStep()
    {
        _store.Add("billing", Settings("billing"), @"CONTOSO\bob");
        _store.Add("orders-api", Settings("orders-api"), Anna);
        var cut = Render<Home>();

        var rows = cut.FindAll(".team-list li");
        Assert.Equal(new[] { "billing", "orders-api" }, rows.Select(r => r.QuerySelector("strong")!.TextContent));
        Assert.Contains(@"Saved by CONTOSO\bob", rows[0].TextContent);
        Assert.Empty(rows[0].QuerySelectorAll(".delete-saved")); // only the one who saved it may delete it
        Assert.Single(rows[1].QuerySelectorAll(".delete-saved"));

        cut.Find(".team-list li[data-saved='1'] .open-saved").Click();

        Assert.Equal("billing", cut.Find("#pipeline-name").GetAttribute("value"));
        Assert.Contains(@"Opened 'billing', saved by CONTOSO\bob.", cut.Markup);
        Assert.Equal(ActivityKind.Opened, _store.Log[0].Action);
        Assert.Contains("opened", cut.Find(".activity-list").TextContent);
    }

    [Fact]
    public void OwnPipelinesCanBeDeleted()
    {
        _store.Add("orders-api", Settings("orders-api"), Anna);
        var cut = Render<Home>();

        cut.Find(".delete-saved").Click();

        Assert.Empty(_store.Saved);
        Assert.Contains("Deleted 'orders-api'.", cut.Markup);
        Assert.Contains("None saved yet", cut.Markup);
        Assert.Equal(ActivityKind.Deleted, _store.Log[0].Action);
    }

    [Fact]
    public void APipelineDeletedMeanwhileOrBrokenIsNotOpened()
    {
        var gone = _store.Add("gone", Settings("gone"), @"CONTOSO\bob");
        _store.Add("broken", "{ nope", @"CONTOSO\bob");
        var cut = Render<Home>();
        _store.Remove(gone);

        cut.Find($".team-list li[data-saved='{gone}'] .open-saved").Click();
        Assert.Contains("'gone' is no longer saved.", cut.Markup);

        cut.Find(".team-list li .open-saved").Click();
        Assert.Contains("Could not open 'broken'", cut.Markup);
        Assert.Equal("my-app", cut.Find("#pipeline-name").GetAttribute("value"));
    }

    [Fact]
    public void DownloadingAndCopyingTheFileAreLogged()
    {
        var cut = Render<Home>();
        GoTo(cut, WizardStep.Result);

        cut.Find("#download-yaml").Click();
        cut.Find("#copy-yaml").Click();
        cut.Find("#download-settings").Click(); // the settings file is not the pipeline

        Assert.Equal(new[] { ActivityKind.Copied, ActivityKind.Downloaded }, _store.Log.Select(l => l.Action));
    }

    [Fact]
    public void ALostDatabaseIsSaidButNothingElseStops()
    {
        _store.Broken = true;
        var cut = Render<Home>();
        Assert.Contains("The team's pipelines could not be loaded", cut.Markup);

        GoTo(cut, WizardStep.Result);
        cut.Find("#save-team").Click();
        Assert.Contains("Not saved: the database could not be reached", cut.Markup);

        cut.Find("#download-yaml").Click();
        Assert.Contains("Downloaded azure-pipelines.yml.", cut.Markup);
    }
}

/// <summary>Without a database nothing of it is shown, and the pages need nothing registered for it.</summary>
public class NoDatabaseTests : BunitContext
{
    [Fact]
    public void NothingOfTheTeamIsShown()
    {
        Services.AddPipelineBuilderCore();
        Services.AddScoped<WizardState>();
        Services.AddSingleton<IPipelineStore>(new NoPipelineStore());
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<Home>();
        Assert.Empty(cut.FindAll("#team-title"));
        cut.Find("button[data-step='Result']").Click();
        Assert.Empty(cut.FindAll("#save-team"));
    }

    [Fact]
    public async Task NoPipelineStoreHasNothing()
    {
        var store = new NoPipelineStore();
        Assert.False(store.IsAvailable);
        Assert.Empty(await store.ListAsync());
        Assert.Null(await store.LoadAsync(1));
        Assert.False(await store.DeleteAsync(1, "x"));
        Assert.Empty(await store.RecentActivityAsync(5));
        await store.SaveAsync("a", "{}", "x");
        await store.LogAsync("x", ActivityKind.Saved, "a");
    }

    [Fact]
    public async Task WithoutASignInTheUserIsUnknown()
    {
        var team = new Team(new ServiceCollection().BuildServiceProvider());
        Assert.False(team.IsAvailable);
        Assert.Equal(Team.Unknown, await team.UserNameAsync());
        await team.LogAsync(ActivityKind.Saved, "a"); // nothing to log to, and no error
    }
}
