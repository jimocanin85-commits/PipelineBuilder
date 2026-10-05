namespace PipelineBuilder.Core.Yaml;

/// <summary>
/// Reusable PowerShell fragments for generated deploy, backup and rollback scripts.
/// </summary>
public static class PowerShellSnippets
{
    /// <summary>
    /// Defines <c>Sync-Folder</c>: mirrors a folder with robocopy and treats exit codes below 8
    /// as success. Without the reset, robocopy's normal exit code 1 ("files copied") would make
    /// the pipeline step fail, because the task reports the script's last exit code.
    /// </summary>
    public const string SyncFolderFunction = """
function Sync-Folder([string]$Source, [string]$Destination, [switch]$Mirror) {
  # Stop if the folder is not set, so nothing is copied to or deleted from the wrong place.
  if (-not $Destination -or $Destination.StartsWith('$(') -or $Destination -match '^([A-Za-z]:)?[\\/]*$') {
    throw "The folder to copy to is not set: '$Destination'. Check the pipeline variables."
  }
  New-Item -ItemType Directory -Force -Path $Destination | Out-Null
  $mode = if ($Mirror) { '/MIR' } else { '/E' }
  robocopy $Source $Destination $mode /R:2 /W:5 /NFL /NDL /NP | Out-Host
  if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE" }
  $global:LASTEXITCODE = 0
}
""";

    /// <summary>
    /// Sets <c>$source</c> to a folder containing the package: the package itself when it is a
    /// folder, or an extracted copy when it is a zip file. Expects <c>$package</c> to be set.
    /// </summary>
    public const string ResolvePackageSource = """
if ($package -like '*.zip') {
  $source = Join-Path $env:AGENT_TEMPDIRECTORY 'package'
  Remove-Item -Path $source -Recurse -Force -ErrorAction SilentlyContinue
  Expand-Archive -Path $package -DestinationPath $source -Force
} else {
  $source = $package
}
""";

    /// <summary>
    /// Sets <c>$target</c> to the physical path of the IIS website named in <c>$site</c>,
    /// unless <c>$target</c> was already set.
    /// </summary>
    public const string ResolveIisSitePath = """
if (-not $target) {
  Import-Module WebAdministration -ErrorAction Stop
  $website = Get-Website -Name $site
  if (-not $website) { throw "IIS website '$site' was not found" }
  $target = [Environment]::ExpandEnvironmentVariables($website.PhysicalPath)
}
""";

    /// <summary>Throws if the last native command (docker, az, ...) failed.</summary>
    public const string ThrowOnNativeFailure = "if ($LASTEXITCODE -ne 0) { throw \"Command failed with exit code $LASTEXITCODE\" }";
}
