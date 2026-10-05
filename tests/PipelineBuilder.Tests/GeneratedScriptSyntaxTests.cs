using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
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
/// The generated scripts cannot be run here, but they can be parsed: every PowerShell script by
/// PowerShell's own parser and every bash script by <c>bash -n</c>. PowerShell scripts must also
/// stay within Windows PowerShell 5.1, which is what Windows servers and agents run.
/// </summary>
/// <remarks>Needs <c>pwsh</c> and <c>bash</c>. They are required on CI and skipped where they are not installed.</remarks>
public class GeneratedScriptSyntaxTests
{
    private const string PowerShellCheck = """
$failed = $false
foreach ($path in $args) {
  $errors = $null
  $ast = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$null, [ref]$errors)
  foreach ($e in $errors) {
    $failed = $true
    Write-Output "$path line $($e.Extent.StartLineNumber): $($e.Message)"
  }
  $tooNew = $ast.FindAll({ param($node)
    $node -is [System.Management.Automation.Language.PipelineChainAst] -or
    $node -is [System.Management.Automation.Language.TernaryExpressionAst] }, $true)
  foreach ($node in $tooNew) {
    $failed = $true
    Write-Output "$path line $($node.Extent.StartLineNumber): '$($node.Extent.Text)' needs PowerShell 7; Windows PowerShell 5.1 cannot run it."
  }
}
if ($failed) { exit 1 }
""";

    private static readonly IPipelineGeneratorService Generator =
        new ServiceCollection().AddPipelineBuilderCore().BuildServiceProvider().GetRequiredService<IPipelineGeneratorService>();

    private static readonly Regex TemplateExpression = new(@"\$\{\{.*?\}\}", RegexOptions.Compiled);

    /// <summary>A script the build agent sends to the servers travels as text inside the step that sends it.</summary>
    private static readonly Regex SentScript = new(@"\$script = @'\n(.*?)\n'@", RegexOptions.Compiled | RegexOptions.Singleline);

    public static IEnumerable<object[]> Cases() =>
        from template in TemplateCatalogue.LoadBuiltIn()
        from runFrom in Enum.GetValues<DeployFrom>()
        select new object[] { template.Id, runFrom };

    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryGeneratedScriptParses(string templateId, DeployFrom runFrom)
    {
        var scripts = Scripts(Generator.Generate(Definition(templateId, runFrom)).Yaml);
        Assert.NotEmpty(scripts);

        var folder = Path.Combine(Path.GetTempPath(), "pipelinebuilder-scripts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var files = scripts.Select((script, i) =>
            {
                var path = Path.Combine(folder, $"step{i}.{(script.Shell == ScriptShell.Bash ? "sh" : "ps1")}");
                File.WriteAllText(path, script.Text + "\n");
                return (script.Shell, Path: path);
            }).ToList();

            var check = Path.Combine(folder, "check.ps1");
            File.WriteAllText(check, PowerShellCheck);
            var powerShell = files.Where(f => f.Shell == ScriptShell.PowerShell).Select(f => f.Path).ToArray();
            if (powerShell.Length > 0)
                AssertSucceeds("pwsh", new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", check }.Concat(powerShell).ToArray());

            // Windows has a bash.exe that only starts WSL, so bash is checked where it is the real shell.
            if (!OperatingSystem.IsWindows())
            {
                foreach (var file in files.Where(f => f.Shell == ScriptShell.Bash))
                    AssertSucceeds("bash", "-n", file.Path);
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>The template with everything that adds a script turned on.</summary>
    private static PipelineDefinition Definition(string templateId, DeployFrom runFrom)
    {
        var definition = WizardState.CreateDefault();
        Assert.True(new TemplateCatalogue().ApplyTo(templateId, definition));
        definition.Deployment.RunFrom = runFrom;
        definition.BuildAgent = BuildAgentType.SelfHosted;
        definition.PoolName = "OnPrem";
        definition.HealthChecks = Enum.GetValues<HealthCheckType>()
            .Select(type => new HealthCheckConfig { Enabled = true, HealthCheckType = type, Url = "https://my-app-{environment}.contoso.com/health" })
            .ToList();
        definition.Notifications = Enum.GetValues<NotificationType>()
            .Select(type => new NotificationConfig { NotificationType = type, NotifyOnSuccess = true, NotifyOnFailure = true, EmailRecipients = new[] { "ops@contoso.com" } })
            .ToList();
        return definition;
    }

    /// <summary>Every script step in the pipeline, and every script a step sends to the servers.</summary>
    private static List<(ScriptShell Shell, string Text)> Scripts(string yaml)
    {
        // Azure DevOps replaces ${{ ... }} before a script runs.
        var withoutExpressions = TemplateExpression.Replace(yaml.ReplaceLineEndings("\n"), "x");
        var scripts = new List<(ScriptShell, string)>();
        Collect(PipelineYaml.Parse(withoutExpressions), scripts);

        foreach (var (_, text) in scripts.ToList())
        {
            foreach (Match sent in SentScript.Matches(text))
                scripts.Add((text.Contains("scp ", StringComparison.Ordinal) ? ScriptShell.Bash : ScriptShell.PowerShell, sent.Groups[1].Value));
        }
        return scripts;
    }

    private static void Collect(object? node, List<(ScriptShell, string)> scripts)
    {
        switch (node)
        {
            case Dictionary<object, object> map:
                foreach (var (key, value) in map)
                {
                    if (key is "powershell" && value is string powerShell)
                        scripts.Add((ScriptShell.PowerShell, powerShell));
                    else if (key is "bash" && value is string bash)
                        scripts.Add((ScriptShell.Bash, bash));
                    else
                        Collect(value, scripts);
                }
                break;
            case List<object> list:
                foreach (var item in list)
                    Collect(item, scripts);
                break;
        }
    }

    private static void AssertSucceeds(string tool, params string[] arguments)
    {
        try
        {
            var start = new ProcessStartInfo(tool) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var argument in arguments)
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, $"{tool} found a problem in a generated script:\n{output}");
        }
        catch (Win32Exception) when (Environment.GetEnvironmentVariable("CI") != "true")
        {
            // The tool is not installed on this machine; CI has it and runs the check.
        }
    }
}
