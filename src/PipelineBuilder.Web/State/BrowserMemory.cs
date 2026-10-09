using System.Text.Json;
using Microsoft.JSInterop;
using PipelineBuilder.Core.Models;

namespace PipelineBuilder.Web.State;

/// <summary>
/// Keeps the wizard in the browser's own storage, so a reload or a closed tab does not lose the work.
/// It is a convenience: when the browser refuses, nothing breaks and nothing is said.
/// </summary>
public static class BrowserMemory
{
    /// <summary>What is kept: the settings, as in a saved settings file, and what has been ticked off.</summary>
    private sealed record SavedState(string Settings, string[] Done);

    public static async Task SaveAsync(IJSRuntime js, WizardState wizard)
    {
        var state = new SavedState(PipelineDefinitionSerializer.ToJson(wizard.Definition), wizard.DoneNeeds.ToArray());
        try
        {
            await js.InvokeVoidAsync("pipelineBuilder.saveState", JsonSerializer.Serialize(state));
        }
        catch (Exception ex) when (IsBrowserGone(ex))
        {
            // The page was closed or the browser refused: there is nothing to keep it for.
        }
    }

    /// <summary>Puts back what the browser kept from last time. False when there is nothing, or nothing usable.</summary>
    public static async Task<bool> RestoreAsync(IJSRuntime js, WizardState wizard)
    {
        string? json;
        try
        {
            json = await js.InvokeAsync<string?>("pipelineBuilder.loadState");
        }
        catch (Exception ex) when (IsBrowserGone(ex))
        {
            return false;
        }
        if (string.IsNullOrEmpty(json) || json.Length > PipelineDefinitionSerializer.MaxFileSize)
            return false;

        try
        {
            // The same checks as a settings file someone opens: the browser's storage is not trusted either.
            var state = JsonSerializer.Deserialize<SavedState>(json);
            if (state?.Settings == null)
                return false;
            var definition = PipelineDefinitionSerializer.FromJson(state.Settings);
            wizard.Load(definition);
            wizard.DoneNeeds.Clear();
            wizard.DoneNeeds.UnionWith(state.Done ?? Array.Empty<string>());
            wizard.SettingsMessage = ("Your settings from last time are back.", false);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsBrowserGone(Exception ex) =>
        ex is JSException or JSDisconnectedException or TaskCanceledException or InvalidOperationException;
}
