using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using PipelineBuilder.Core.Abstractions;
using PipelineBuilder.Core.DependencyInjection;
using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using Xunit;

namespace PipelineBuilder.Tests.RealScripts;

/// <summary>
/// Stands in for an Azure DevOps agent: takes the script steps of a generated pipeline, fills in the
/// variables the way the agent does, and runs them in order with the real shell (Windows PowerShell
/// on Windows, bash on Linux). The tests then look at what the scripts did to the machine.
/// </summary>
/// <remarks>
/// Task steps (downloading the artifact, logging in to a registry) are Azure's own and are not run;
/// the test puts the package where the download would have put it.
/// </remarks>
internal sealed class ScriptHost
{
    /// <summary>Set to <c>linux</c> or <c>windows</c> on a machine that is prepared for these tests (see the Real scripts workflow).</summary>
    public const string Switch = "PIPELINEBUILDER_REAL_SCRIPTS";

    private static readonly IPipelineGeneratorService Generator =
        new ServiceCollection().AddPipelineBuilderCore().BuildServiceProvider().GetRequiredService<IPipelineGeneratorService>();

    private static readonly Regex Macro = new(@"\$\(([A-Za-z_][A-Za-z0-9_.]*)\)", RegexOptions.Compiled);

    private readonly Dictionary<string, string> _variables = new(StringComparer.OrdinalIgnoreCase);

    public ScriptHost()
    {
        Workspace = Directory.CreateTempSubdirectory("pb-workspace-").FullName;
        TempDirectory = Directory.CreateTempSubdirectory("pb-temp-").FullName;
        Set("Pipeline.Workspace", Workspace);
        Set("System.DefinitionId", "42");
        Set("Build.BuildNumber", "1");
        Set("Build.BuildId", "1");
    }

    public static bool EnabledFor(string os) =>
        string.Equals(Environment.GetEnvironmentVariable(Switch), os, StringComparison.OrdinalIgnoreCase);

    /// <summary>What <c>$(Pipeline.Workspace)</c> points at: where the artifact is downloaded to.</summary>
    public string Workspace { get; }

    /// <summary>What the agent calls its temp directory.</summary>
    public string TempDirectory { get; }

    /// <summary>The environment the deploy stage runs for.</summary>
    public string EnvironmentName { get; set; } = "test";

    /// <summary>Sets a pipeline variable, e.g. <c>Build.BuildId</c> or <c>DOCKER_REGISTRY</c>.</summary>
    public void Set(string name, string value) => _variables[name] = value;

    /// <summary>Starts a new run: a new build number, and the package the Build stage would have produced.</summary>
    public void NewBuild(string buildId, string artifactName, IReadOnlyDictionary<string, string> files)
    {
        Set("Build.BuildId", buildId);
        var folder = Path.Combine(Workspace, artifactName);
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(folder);
        foreach (var (name, content) in files)
            File.WriteAllText(Path.Combine(folder, name), content.ReplaceLineEndings("\n"));
    }

    /// <summary>Runs the deploy job's steps: backup, deploy, health checks. Stops at the first step that fails.</summary>
    public IReadOnlyList<StepResult> Deploy(PipelineDefinition definition) => Run(Steps(definition, onFailure: false));

    /// <summary>Runs the steps of the <c>on: failure</c> hook: the rollback.</summary>
    public IReadOnlyList<StepResult> RollBack(PipelineDefinition definition) => Run(Steps(definition, onFailure: true));

    /// <summary>
    /// The last step stopped because its folder variable is not set, and nothing was copied to a
    /// folder named after the missing variable.
    /// </summary>
    public void AssertStoppedBecauseTheFolderIsNotSet(IReadOnlyList<StepResult> results)
    {
        var deploy = results[^1];
        Assert.NotEqual(0, deploy.ExitCode);
        Assert.Contains("The folder to copy to is not set", deploy.Output);
        Assert.False(Directory.Exists(Path.Combine(Workspace, "$(DEPLOY_PATH)")), "Nothing is copied to a folder named after the missing variable.");
    }

    public static void AssertSucceeded(IReadOnlyList<StepResult> results)
    {
        Assert.NotEmpty(results);
        var failed = results.FirstOrDefault(r => r.ExitCode != 0);
        Assert.True(failed == null, failed == null ? "" : $"Step '{failed.Name}' failed with exit code {failed.ExitCode}:\n{Tail(failed.Output)}");
    }

    /// <summary>Runs a command with the machine's shell, to prepare the machine or to look at it. Fails the test when the command fails.</summary>
    public static string Shell(string script)
    {
        var temp = Directory.CreateTempSubdirectory("pb-shell-").FullName;
        var result = Execute(OperatingSystem.IsWindows() ? ScriptShell.PowerShell : ScriptShell.Bash, script, "shell", temp, temp);
        Assert.True(result.ExitCode == 0, $"The command failed with exit code {result.ExitCode}:\n{script}\n---\n{Tail(result.Output)}");
        return result.Output.Trim();
    }

    private List<(ScriptShell Shell, string Script, string Name)> Steps(PipelineDefinition definition, bool onFailure)
    {
        var job = PipelineYaml.DeployJob(PipelineYaml.Parse(Generator.Generate(definition).Yaml));
        var strategy = (Dictionary<object, object>)job["strategy"];
        var lifecycle = (Dictionary<object, object>)strategy.Values.Single(); // runOnce or rolling

        object? steps;
        if (onFailure)
        {
            var hooks = (Dictionary<object, object>)lifecycle["on"];
            steps = ((Dictionary<object, object>)hooks["failure"])["steps"];
        }
        else
        {
            steps = ((Dictionary<object, object>)lifecycle["deploy"])["steps"];
        }

        return ((List<object>)steps).Cast<Dictionary<object, object>>()
            .Where(step => step.ContainsKey("powershell") || step.ContainsKey("bash"))
            .Select(step => (
                step.ContainsKey("bash") ? ScriptShell.Bash : ScriptShell.PowerShell,
                (string)(step.ContainsKey("bash") ? step["bash"] : step["powershell"]),
                (string)step.GetValueOrDefault("displayName", "script")))
            .ToList();
    }

    private IReadOnlyList<StepResult> Run(List<(ScriptShell Shell, string Script, string Name)> steps)
    {
        var results = new List<StepResult>();
        foreach (var (shell, script, name) in steps)
        {
            var result = Execute(shell, Resolve(script), Resolve(name), Workspace, TempDirectory);
            results.Add(result);
            if (result.ExitCode != 0)
                break; // the agent skips the remaining steps of a failed job
        }
        return results;
    }

    /// <summary>What Azure DevOps does before a script runs: template expressions first, then <c>$(variables)</c>. Unknown variables stay as they are.</summary>
    private string Resolve(string text) =>
        Macro.Replace(text.Replace("${{ environment }}", EnvironmentName, StringComparison.Ordinal),
            match => _variables.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value);

    private static StepResult Execute(ScriptShell shell, string script, string name, string workingDirectory, string tempDirectory)
    {
        var bash = shell == ScriptShell.Bash;
        var file = Path.Combine(tempDirectory, $"step-{Guid.NewGuid():N}.{(bash ? "sh" : "ps1")}");
        var text = script.ReplaceLineEndings("\n");
        if (!bash)
        {
            // The agent's PowerShell task stops at the first error and reports the last exit code.
            text = "$ErrorActionPreference = 'Stop'\n" + text + "\nif ((Test-Path -LiteralPath variable:\\LASTEXITCODE)) { exit $LASTEXITCODE }\n";
        }
        File.WriteAllText(file, text);

        var start = new ProcessStartInfo
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory
        };
        if (bash)
        {
            start.FileName = "bash";
            start.ArgumentList.Add("--noprofile");
            start.ArgumentList.Add("--norc");
        }
        else
        {
            // Windows servers and agents run Windows PowerShell 5.1; a Linux agent has PowerShell 7.
            start.FileName = OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh";
            foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File" })
                start.ArgumentList.Add(argument);
        }
        start.ArgumentList.Add(file);
        start.Environment["AGENT_TEMPDIRECTORY"] = tempDirectory;

        // Output and errors are collected line by line, in the order they arrive, as the agent's log does.
        var log = new StringBuilder();
        using var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, e) => Append(log, e.Data);
        process.ErrorDataReceived += (_, e) => Append(log, e.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (!process.WaitForExit(TimeSpan.FromMinutes(5)))
        {
            process.Kill(entireProcessTree: true);
            return new StepResult(name, -1, "The step did not finish within five minutes.\n" + log);
        }
        process.WaitForExit(); // lets the output handlers finish
        lock (log)
            return new StepResult(name, process.ExitCode, log.ToString());
    }

    private static void Append(StringBuilder log, string? line)
    {
        if (line == null) return;
        lock (log)
            log.Append(line).Append('\n');
    }

    private static string Tail(string output) => output.Length <= 3000 ? output : "..." + output[^3000..];
}

/// <summary>The outcome of one script step.</summary>
internal sealed record StepResult(string Name, int ExitCode, string Output);
