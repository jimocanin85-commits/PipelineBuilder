<#
.SYNOPSIS
    Installs (or updates) PipelineBuilder as an IIS website with Windows login.

.DESCRIPTION
    Run as Administrator on the Windows server that will host PipelineBuilder.
    The script:
      1. checks (and with -InstallMissingFeatures installs) the IIS features it needs:
         the web server, WebSockets (required by Blazor) and Windows Authentication;
      2. checks that the ASP.NET Core Hosting Bundle for .NET 10 is installed;
      3. copies the published app into -PhysicalPath (app pool stopped meanwhile);
      4. creates or updates the app pool (No Managed Code) and the website;
      5. turns on Windows Authentication and turns off anonymous access for the site;
      6. optionally limits access to Active Directory groups (appsettings.Production.json).

    Publish the app first:
        dotnet publish src/PipelineBuilder.Web -c Release -o .\publish

.EXAMPLE
    .\deploy\Install-PipelineBuilder.ps1 -PublishFolder .\publish -InstallMissingFeatures

.EXAMPLE
    .\deploy\Install-PipelineBuilder.ps1 -PublishFolder .\publish -HostName pipelines.contoso.local `
        -Port 443 -CertificateThumbprint 0123456789ABCDEF... -AllowedGroups 'CONTOSO\Platform-Team'
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    # Folder with the output of 'dotnet publish'. Omit to only (re)configure IIS.
    [string] $PublishFolder,
    [string] $SiteName = 'PipelineBuilder',
    [string] $AppPoolName = 'PipelineBuilder',
    [string] $PhysicalPath = 'C:\inetpub\PipelineBuilder',
    [string] $HostName = '',
    [int] $Port = 80,
    # Thumbprint of a certificate in LocalMachine\My. When set, the binding uses HTTPS.
    [string] $CertificateThumbprint = '',
    # Only members of these groups may use the app (e.g. 'CONTOSO\Platform-Team').
    [string[]] $AllowedGroups = @(),
    [switch] $InstallMissingFeatures
)

$ErrorActionPreference = 'Stop'

function Assert-Administrator {
    $principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Run this script as Administrator.'
    }
}

function Test-IisFeatures {
    # Windows Server uses Get-WindowsFeature; Windows 10/11 uses optional features.
    if (Get-Command Get-WindowsFeature -ErrorAction SilentlyContinue) {
        $features = 'Web-Server', 'Web-WebSockets', 'Web-Windows-Auth'
        $missing = $features | Where-Object { -not (Get-WindowsFeature -Name $_).Installed }
        if ($missing) {
            if (-not $InstallMissingFeatures) { throw "Missing IIS features: $($missing -join ', '). Re-run with -InstallMissingFeatures." }
            Write-Host "Installing IIS features: $($missing -join ', ')"
            Install-WindowsFeature -Name $missing -IncludeManagementTools | Out-Null
        }
    }
    else {
        $features = 'IIS-WebServerRole', 'IIS-WebServer', 'IIS-WebSockets', 'IIS-WindowsAuthentication', 'IIS-ManagementConsole'
        $missing = $features | Where-Object { (Get-WindowsOptionalFeature -Online -FeatureName $_).State -ne 'Enabled' }
        if ($missing) {
            if (-not $InstallMissingFeatures) { throw "Missing IIS features: $($missing -join ', '). Re-run with -InstallMissingFeatures." }
            Write-Host "Installing IIS features: $($missing -join ', ')"
            Enable-WindowsOptionalFeature -Online -FeatureName $missing -All -NoRestart | Out-Null
        }
    }
}

function Assert-HostingBundle {
    $module = 'HKLM:\SOFTWARE\Microsoft\IIS Extensions\IIS AspNetCore Module V2'
    $runtimes = Join-Path $env:ProgramFiles 'dotnet\shared\Microsoft.AspNetCore.App'
    $hasNet10 = (Test-Path $runtimes) -and (Get-ChildItem $runtimes -Directory | Where-Object Name -like '10.*')
    if (-not (Test-Path $module) -or -not $hasNet10) {
        throw 'The ASP.NET Core Hosting Bundle for .NET 10 is not installed. Download it from https://dotnet.microsoft.com/download/dotnet/10.0 (Hosting Bundle), install it, then run this script again.'
    }
}

Assert-Administrator
Test-IisFeatures
Assert-HostingBundle
Import-Module WebAdministration

# App pool: ASP.NET Core runs out of the CLR-less pool ("No Managed Code").
if (-not (Test-Path "IIS:\AppPools\$AppPoolName")) {
    Write-Host "Creating app pool $AppPoolName"
    New-WebAppPool -Name $AppPoolName | Out-Null
}
Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name managedRuntimeVersion -Value ''
Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name startMode -Value 'AlwaysRunning'

# Files: stop the pool while copying so no files are locked.
New-Item -ItemType Directory -Force -Path $PhysicalPath | Out-Null
if ($PublishFolder) {
    if (-not (Test-Path (Join-Path $PublishFolder 'PipelineBuilder.Web.dll'))) {
        throw "'$PublishFolder' does not look like the output of 'dotnet publish src/PipelineBuilder.Web'."
    }
    if ((Get-WebAppPoolState -Name $AppPoolName).Value -eq 'Started') { Stop-WebAppPool -Name $AppPoolName }
    Write-Host "Copying $PublishFolder to $PhysicalPath"
    robocopy $PublishFolder $PhysicalPath /MIR /XF appsettings.Production.json /NFL /NDL /NP | Out-Host
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE" }
    $global:LASTEXITCODE = 0
}

# The app pool identity needs to read the files.
$acl = Get-Acl $PhysicalPath
$rule = New-Object Security.AccessControl.FileSystemAccessRule("IIS AppPool\$AppPoolName", 'ReadAndExecute', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
$acl.SetAccessRule($rule)
Set-Acl $PhysicalPath $acl

# Optional group restriction, read by the app from appsettings.Production.json.
if ($AllowedGroups.Count -gt 0) {
    $settings = @{ Authentication = @{ Mode = 'Windows'; AllowedGroups = $AllowedGroups } } | ConvertTo-Json -Depth 4
    Set-Content -Path (Join-Path $PhysicalPath 'appsettings.Production.json') -Value $settings -Encoding UTF8
    Write-Host "Access limited to: $($AllowedGroups -join ', ')"
}

# Website and binding.
$protocol = if ($CertificateThumbprint) { 'https' } else { 'http' }
if (-not (Get-Website -Name $SiteName)) {
    Write-Host "Creating website $SiteName ($protocol, port $Port)"
    New-Website -Name $SiteName -PhysicalPath $PhysicalPath -ApplicationPool $AppPoolName -Port $Port -HostHeader $HostName -Ssl:([bool]$CertificateThumbprint) | Out-Null
}
else {
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath -Value $PhysicalPath
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool -Value $AppPoolName
}
if ($CertificateThumbprint) {
    $binding = Get-WebBinding -Name $SiteName -Protocol https -Port $Port
    $binding.AddSslCertificate($CertificateThumbprint, 'My')
}

# Windows login on, anonymous access off (stored for this site in applicationHost.config).
Set-WebConfigurationProperty -PSPath 'IIS:\' -Location $SiteName -Filter 'system.webServer/security/authentication/windowsAuthentication' -Name enabled -Value $true
Set-WebConfigurationProperty -PSPath 'IIS:\' -Location $SiteName -Filter 'system.webServer/security/authentication/anonymousAuthentication' -Name enabled -Value $false

Start-WebAppPool -Name $AppPoolName -ErrorAction SilentlyContinue
Start-Website -Name $SiteName -ErrorAction SilentlyContinue

$url = '{0}://{1}:{2}/' -f $protocol, ($(if ($HostName) { $HostName } else { 'localhost' })), $Port
Write-Host "PipelineBuilder is running at $url (Windows login)." -ForegroundColor Green
