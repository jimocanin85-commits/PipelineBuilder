using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;

namespace PipelineBuilder.Core.Yaml;

/// <summary>
/// A script that runs on each target server.
/// <list type="bullet">
/// <item>With an agent on the server (<see cref="DeployFrom.Server"/>) it is an ordinary script step.</item>
/// <item>When the build agent deploys (<see cref="DeployFrom.Agent"/>) the same script is sent to every
/// server by a PowerShell step on the agent: over PowerShell remoting (WinRM) to Windows servers and
/// over SSH to Linux servers. On the server it runs as a script file, exactly as the agent would run
/// it, so the scripts do not need to know how they got there.</item>
/// </list>
/// </summary>
public static class ServerScript
{
    /// <summary>
    /// Job variable holding the servers of the environment being deployed to, comma-separated. The deploy
    /// job sets it from <c>SERVERS_TEST</c>, <c>SERVERS_PROD</c> and so on.
    /// </summary>
    public const string ServersVariable = "DeployServers";

    /// <summary>Prefix of the pipeline variable that names an environment's servers, e.g. <c>SERVERS_PROD</c>.</summary>
    public const string ServersVariablePrefix = "SERVERS_";

    /// <summary>The script as a step: run directly on the server, or sent there from the build agent.</summary>
    public static string Step(DeploymentConfig deployment, ScriptShell shell, string script, string displayName)
    {
        if (deployment.RunFrom != DeployFrom.Agent)
            return YamlBuilder.ShellStep(shell, script, displayName);

        var transport = shell == ScriptShell.Bash ? OverSsh(deployment, script) : OverWinRm(script);
        return YamlBuilder.PowerShellStep(transport, displayName);
    }

    /// <summary>The login account's home folder in bash, and its own application data folder in PowerShell.</summary>
    private const string BashHome = "$HOME/";
    private const string PowerShellHome = @"$env:LOCALAPPDATA\";

    /// <summary>Folder in the login account's home folder on a Linux server, for the package and the step scripts.</summary>
    private const string LinuxWorkFolder = ".pipelinebuilder";

    /// <summary>
    /// The folder the package is copied to on each server, when the build agent deploys. It is replaced
    /// on every run, so nothing piles up.
    /// <para>
    /// It lies in the home folder of the account that logs in, not in a shared place such as
    /// <c>/tmp</c> or <c>C:\ProgramData</c>. Any account on the server can create files in those, and
    /// could swap the package for its own before it is installed.
    /// </para>
    /// </summary>
    public static string RemotePackageFolder(ScriptShell shell) =>
        shell == ScriptShell.Bash
            ? BashHome + LinuxWorkFolder + "/$(System.DefinitionId)"
            : PowerShellHome + @"PipelineBuilder\$(System.DefinitionId)";

    /// <summary>Where a package downloaded to <paramref name="localPackage"/> on the agent ends up on the server.</summary>
    public static string RemotePackagePath(ScriptShell shell, string localPackage)
    {
        var name = localPackage[(localPackage.LastIndexOfAny(new[] { '/', '\\' }) + 1)..];
        return RemotePackageFolder(shell) + (shell == ScriptShell.Bash ? "/" : @"\") + name;
    }

    /// <summary>
    /// A path as a value in a script. A path under the account's home folder (see
    /// <see cref="RemotePackageFolder"/>) keeps the part that the shell must fill in; everything else
    /// is quoted, so nothing in it is run as code.
    /// </summary>
    public static string PathLiteral(ScriptShell shell, string path)
    {
        if (shell == ScriptShell.Bash)
        {
            return path.StartsWith(BashHome, StringComparison.Ordinal)
                ? "\"$HOME\"" + YamlBuilder.BashLiteral(path[(BashHome.Length - 1)..])
                : YamlBuilder.BashLiteral(path);
        }

        return path.StartsWith(PowerShellHome, StringComparison.Ordinal)
            ? $"(Join-Path $env:LOCALAPPDATA {YamlBuilder.PsLiteral(path[PowerShellHome.Length..])})"
            : YamlBuilder.PsLiteral(path);
    }

    /// <summary>A step on the build agent that copies the downloaded package to every server.</summary>
    public static string CopyPackageStep(DeploymentConfig deployment, ScriptShell shell, string localPackage)
    {
        var folder = RemotePackageFolder(shell);
        var script = shell == ScriptShell.Bash
            ? $$"""
{{Servers}}
{{SshLogin(deployment)}}
$package = {{YamlBuilder.PsLiteral(localPackage)}}
# In the home folder of the account that logs in. No other account on the server can change it.
$folder = {{YamlBuilder.PsLiteral(folder[BashHome.Length..])}}
foreach ($server in $servers) {
  Write-Host "--- $server"
  $login = $user + '@' + $server
  ssh -o BatchMode=yes $login "{{PrivateWorkFolder}} && rm -rf '$folder' && mkdir '$folder'"
  if ($LASTEXITCODE -ne 0) { throw "Could not prepare $folder on $server. Check that the agent can log in there with SSH." }
  scp -q -r -o BatchMode=yes $package "${login}:$folder"
  if ($LASTEXITCODE -ne 0) { throw "Could not copy the package to $server." }
}
"""
            : $$"""
{{Servers}}
$package = {{YamlBuilder.PsLiteral(localPackage)}}
foreach ($server in $servers) {
  Write-Host "--- $server"
  $session = New-PSSession -ComputerName $server
  try {
    # In the profile of the account that logs in. No other account on the server can change it.
    $folder = Invoke-Command -Session $session -ScriptBlock {
      $folder = Join-Path $env:LOCALAPPDATA {{YamlBuilder.PsLiteral(folder[PowerShellHome.Length..])}}
      if (Test-Path $folder) { Remove-Item -Path $folder -Recurse -Force }
      New-Item -ItemType Directory -Force -Path $folder | Out-Null
      $folder
    }
    Copy-Item -Path $package -Destination $folder -ToSession $session -Recurse -Force
  } finally {
    Remove-PSSession $session
  }
}
""";
        return YamlBuilder.PowerShellStep(script, "Copy the package to the servers");
    }

    /// <summary>Creates the work folder on a Linux server, open to the login account only.</summary>
    private const string PrivateWorkFolder = "mkdir -p " + LinuxWorkFolder + " && chmod 700 " + LinuxWorkFolder;

    /// <summary>Reads the server list, and stops with a clear message when the variable is not defined.</summary>
    private const string Servers = """
$ErrorActionPreference = 'Stop'
$servers = @('$(DeployServers)'.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($servers.Count -eq 0 -or $servers[0].StartsWith('$(')) {
  throw 'No servers to deploy to. Define a pipeline variable per environment, e.g. SERVERS_PROD = web01, web02.'
}
""";

    private static string SshLogin(DeploymentConfig deployment) => $$"""
$user = {{YamlBuilder.PsLiteral(deployment.SshUserOrDefault)}}
if ($user.StartsWith('$(')) { throw 'Define the pipeline variable SSH_USER: the account the agent logs in as on the servers.' }
""";

    /// <summary>
    /// Runs a PowerShell script on each Windows server. The script is written to a file there and run
    /// with <c>powershell.exe</c>, so exit codes and errors behave as they do in an ordinary step.
    /// </summary>
    private static string OverWinRm(string script) => $$"""
{{Servers}}
$script = @'
$ErrorActionPreference = 'Stop'
{{script.TrimEnd()}}
'@
foreach ($server in $servers) {
  Write-Host "--- $server"
  Invoke-Command -ComputerName $server -ArgumentList $script -ErrorAction Stop -ScriptBlock {
    param($text)
    $ErrorActionPreference = 'Continue'
    $file = Join-Path $env:TEMP ('pipelinebuilder-' + [guid]::NewGuid().ToString('N') + '.ps1')
    Set-Content -Path $file -Value $text -Encoding UTF8
    try {
      powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $file 2>&1 | ForEach-Object { "$_" }
      $code = $LASTEXITCODE
    } finally {
      Remove-Item -Path $file -Force -ErrorAction SilentlyContinue
    }
    if ($code -ne 0) { throw "The step failed on $env:COMPUTERNAME (exit code $code)." }
  }
}
""";

    /// <summary>
    /// Runs a bash script on each Linux server: copies it there with <c>scp</c> and runs it with
    /// <c>ssh</c>. The agent logs in with its own SSH key; <c>BatchMode</c> makes a missing key fail
    /// at once instead of waiting for a password, and refuses a server whose host key the agent does
    /// not know already.
    /// </summary>
    private static string OverSsh(DeploymentConfig deployment, string script) => $$"""
{{Servers}}
{{SshLogin(deployment)}}
$script = @'
{{script.TrimEnd()}}
'@
$file = Join-Path $env:AGENT_TEMPDIRECTORY 'pipelinebuilder-step.sh'
[IO.File]::WriteAllText($file, $script.Replace("`r`n", "`n") + "`n")
# The script may hold secret values, so on the server it lies in a folder only this account can read.
$remote = '{{LinuxWorkFolder}}/step-$(Build.BuildId).sh'
foreach ($server in $servers) {
  Write-Host "--- $server"
  $login = $user + '@' + $server
  ssh -o BatchMode=yes $login "{{PrivateWorkFolder}}"
  if ($LASTEXITCODE -ne 0) { throw "Could not reach $server. Check that the agent can log in there with SSH." }
  scp -q -o BatchMode=yes $file "${login}:$remote"
  if ($LASTEXITCODE -ne 0) { throw "Could not copy the script to $server." }
  ssh -o BatchMode=yes $login "bash $remote; code=`$?; rm -f $remote; exit `$code"
  if ($LASTEXITCODE -ne 0) { throw "The step failed on $server (exit code $LASTEXITCODE)." }
}
""";
}
