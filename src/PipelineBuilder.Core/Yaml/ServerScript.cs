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

    /// <summary>
    /// The folder the package is copied to on each server, when the build agent deploys. It is replaced
    /// on every run, so nothing piles up.
    /// </summary>
    public static string RemotePackageFolder(ScriptShell shell) =>
        shell == ScriptShell.Bash ? "/tmp/pipelinebuilder-$(System.DefinitionId)" : @"C:\ProgramData\PipelineBuilder\$(System.DefinitionId)";

    /// <summary>Where a package downloaded to <paramref name="localPackage"/> on the agent ends up on the server.</summary>
    public static string RemotePackagePath(ScriptShell shell, string localPackage)
    {
        var name = localPackage[(localPackage.LastIndexOfAny(new[] { '/', '\\' }) + 1)..];
        return RemotePackageFolder(shell) + (shell == ScriptShell.Bash ? "/" : @"\") + name;
    }

    /// <summary>A step on the build agent that copies the downloaded package to every server.</summary>
    public static string CopyPackageStep(DeploymentConfig deployment, ScriptShell shell, string localPackage)
    {
        var script = shell == ScriptShell.Bash
            ? $$"""
{{Servers}}
{{SshLogin(deployment)}}
$package = {{YamlBuilder.PsLiteral(localPackage)}}
$folder = {{YamlBuilder.PsLiteral(RemotePackageFolder(shell))}}
foreach ($server in $servers) {
  Write-Host "--- $server"
  $login = $user + '@' + $server
  ssh -o BatchMode=yes $login "rm -rf '$folder' && mkdir -p '$folder'"
  if ($LASTEXITCODE -ne 0) { throw "Could not prepare $folder on $server. Check that the agent can log in there with SSH." }
  scp -q -r -o BatchMode=yes $package "${login}:$folder"
  if ($LASTEXITCODE -ne 0) { throw "Could not copy the package to $server." }
}
"""
            : $$"""
{{Servers}}
$package = {{YamlBuilder.PsLiteral(localPackage)}}
$folder = {{YamlBuilder.PsLiteral(RemotePackageFolder(shell))}}
foreach ($server in $servers) {
  Write-Host "--- $server"
  $session = New-PSSession -ComputerName $server
  try {
    Invoke-Command -Session $session -ArgumentList $folder -ScriptBlock {
      param($folder)
      if (Test-Path $folder) { Remove-Item -Path $folder -Recurse -Force }
      New-Item -ItemType Directory -Force -Path $folder | Out-Null
    }
    Copy-Item -Path $package -Destination $folder -ToSession $session -Recurse -Force
  } finally {
    Remove-PSSession $session
  }
}
""";
        return YamlBuilder.PowerShellStep(script, "Copy the package to the servers");
    }

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
    /// at once instead of waiting for a password.
    /// </summary>
    private static string OverSsh(DeploymentConfig deployment, string script) => $$"""
{{Servers}}
{{SshLogin(deployment)}}
$script = @'
{{script.TrimEnd()}}
'@
$file = Join-Path $env:AGENT_TEMPDIRECTORY 'pipelinebuilder-step.sh'
[IO.File]::WriteAllText($file, $script.Replace("`r`n", "`n") + "`n")
$remote = '/tmp/pipelinebuilder-step-$(Build.BuildId).sh'
foreach ($server in $servers) {
  Write-Host "--- $server"
  $login = $user + '@' + $server
  scp -q -o BatchMode=yes $file "${login}:$remote"
  if ($LASTEXITCODE -ne 0) { throw "Could not reach $server. Check that the agent can log in there with SSH." }
  ssh -o BatchMode=yes $login "bash $remote; code=`$?; rm -f $remote; exit `$code"
  if ($LASTEXITCODE -ne 0) { throw "The step failed on $server (exit code $LASTEXITCODE)." }
}
""";
}
