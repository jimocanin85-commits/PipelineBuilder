<#
.SYNOPSIS
    Installs (or updates) PipelineBuilder as an IIS website with Windows login.

.DESCRIPTION
    Run as Administrator on the Windows server that will host PipelineBuilder.
    The script:
      1. checks the IIS features it needs: the web server, WebSockets (required by Blazor) and Windows
         Authentication, and installs missing ones (asked first, or always with -InstallMissingFeatures);
      2. checks the ASP.NET Core Hosting Bundle for .NET 10, and downloads and installs it from Microsoft
         when it is missing (asked first, or always with -InstallMissingFeatures);
      3. asks for the website name, the environment, the install folder (picked in a window), HTTPS, the SQL Server
         and the AD groups, unless they are given as parameters.
         For HTTPS you pick a certificate on the server, import a .pfx file, type a thumbprint, or skip it;
      4. copies the app into -PhysicalPath (app pool stopped meanwhile), from the folder this download was
         extracted to: the ready-built app, or the source code, built when the .NET 10 SDK is installed;
      5. creates or updates the app pool (No Managed Code), the website and its binding (HTTPS with a certificate);
      6. turns on Windows Authentication and turns off anonymous access for the site;
      7. writes the environment (e.g. Test, Preprod or Prod) in web.config as ASPNETCORE_ENVIRONMENT, and keeps
         that environment's settings in appsettings.<environment>.json, with the AD groups that may use the app;
      8. with -SqlServer (or when asked): makes the database PipelineBuild (Prod), PipelineBuild.PP (Preprod) or
         PipelineBuild.<environment> with collation Danish_Norwegian_CI_AS, its tables, and a Windows login for the
         app pool, and writes the connection string in appsettings.<environment>.json.

    Download the ready-built app, extract it and run this script from there:
        https://github.com/jimocanin85-commits/PipelineBuilder/releases/latest/download/PipelineBuilder-iis.zip

.EXAMPLE
    .\deploy\Install-PipelineBuilder.ps1
    Asks for the website name, the install folder, the certificate for HTTPS and the AD groups.

.EXAMPLE
    .\deploy\Install-PipelineBuilder.ps1 -SiteName PipelineBuilder -PhysicalPath D:\Apps\PipelineBuilder `
        -HostName pipelines.contoso.local -Port 443 -CertificateThumbprint 0123456789ABCDEF... -AllowedGroups 'CONTOSO\Platform-Team'
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    # Folder with the built app. Empty: the folder this download was extracted to; the app is in its
    # 'publish' folder, or built from the source code there. Asked for when none is found.
    [string] $PublishFolder,
    [string] $SiteName = 'PipelineBuilder',
    [string] $AppPoolName = 'PipelineBuilder',
    [string] $PhysicalPath = 'C:\inetpub\PipelineBuilder',
    # The environment this server is, e.g. Test, Preprod or Prod. Written in web.config; the app then also reads
    # appsettings.<environment>.json. Empty: the one already installed, else Prod.
    [string] $Environment = '',
    [string] $HostName = '',
    [int] $Port = 80,
    # Thumbprint of a certificate in LocalMachine\My. When set, the binding uses HTTPS.
    [string] $CertificateThumbprint = '',
    # A .pfx file to import into LocalMachine\My and use for HTTPS, instead of a thumbprint.
    [string] $CertificateFile = '',
    # The .pfx file's password. Asked for when it is missing.
    [securestring] $CertificatePassword,
    # Only members of these groups may use the app (e.g. 'CONTOSO\Platform-Team').
    [string[]] $AllowedGroups = @(),
    # Install missing IIS features and the Hosting Bundle without asking.
    [switch] $InstallMissingFeatures,
    # SQL Server (name, name\instance or alias) for the team's saved pipelines and the activity log. The script
    # makes the database PipelineBuild, PipelineBuild.PP or PipelineBuild.<environment> (Danish_Norwegian_CI_AS)
    # and lets the app pool in with Windows login. Empty: the one already set up, or none.
    [string] $SqlServer = '',
    # Another database name than the one made from the environment.
    [string] $DatabaseName = '',
    # Connect even when the SQL Server's certificate is not trusted by this server (it is still encrypted).
    [switch] $TrustSqlServerCertificate,
    # Never ask; use the parameters and their defaults. Questions are also skipped in a pipeline.
    [switch] $NoPrompt
)

$ErrorActionPreference = 'Stop'

# Imports a .pfx file into Local Computer > Personal and returns its thumbprint.
function Import-HttpsCertificate([string] $File, [securestring] $Password) {
    if (-not (Test-Path $File -PathType Leaf)) { throw "The certificate file '$File' was not found." }
    if (-not $Password) { $Password = Read-Host "Password for $(Split-Path $File -Leaf)" -AsSecureString }
    # A .pfx can hold the issuer's certificates too; the one with the private key is the site's.
    $imported = @(Import-PfxCertificate -FilePath $File -CertStoreLocation Cert:\LocalMachine\My -Password $Password) | Where-Object HasPrivateKey | Select-Object -First 1
    if (-not $imported) { throw "'$File' has no private key, so it cannot be used for HTTPS." }
    Write-Host "Imported the certificate for $($imported.Subject), valid until $($imported.NotAfter.ToString('yyyy-MM-dd'))"
    $imported.Thumbprint
}

# A certificate file is imported first, in whichever PowerShell runs this, and used by its thumbprint.
if ($CertificateFile) {
    $CertificateThumbprint = Import-HttpsCertificate $CertificateFile $CertificatePassword
    $PSBoundParameters['CertificateThumbprint'] = $CertificateThumbprint
    [void] $PSBoundParameters.Remove('CertificateFile')
    [void] $PSBoundParameters.Remove('CertificatePassword')
}

# The IIS cmdlets (WebAdministration, IIS: drive) only work in Windows PowerShell 5.1.
# When started from PowerShell 7, run this same script in Windows PowerShell with the same parameters.
# -File keeps the same window, so the questions below can be answered there.
if ($PSVersionTable.PSEdition -eq 'Core') {
    $forward = @()
    foreach ($entry in $PSBoundParameters.GetEnumerator()) {
        if ($entry.Value -is [switch]) { if ($entry.Value.IsPresent) { $forward += "-$($entry.Key)" } }
        elseif ($entry.Value -is [array]) { $forward += "-$($entry.Key)", ($entry.Value -join ',') }
        else { $forward += "-$($entry.Key)", "$($entry.Value)" }
    }
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath @forward
    if ($LASTEXITCODE -ne 0) { throw "Install-PipelineBuilder failed in Windows PowerShell (exit code $LASTEXITCODE). See the output above." }
    return
}

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
            if (-not (Confirm-Install "IIS is missing $($missing -join ', ')")) { throw "Missing IIS features: $($missing -join ', '). Run again with -InstallMissingFeatures, or answer Y." }
            Write-Host "Installing IIS features: $($missing -join ', ')"
            Install-WindowsFeature -Name $missing -IncludeManagementTools | Out-Null
        }
    }
    else {
        $features = 'IIS-WebServerRole', 'IIS-WebServer', 'IIS-WebSockets', 'IIS-WindowsAuthentication', 'IIS-ManagementConsole'
        $missing = $features | Where-Object { (Get-WindowsOptionalFeature -Online -FeatureName $_).State -ne 'Enabled' }
        if ($missing) {
            if (-not (Confirm-Install "IIS is missing $($missing -join ', ')")) { throw "Missing IIS features: $($missing -join ', '). Run again with -InstallMissingFeatures, or answer Y." }
            Write-Host "Installing IIS features: $($missing -join ', ')"
            Enable-WindowsOptionalFeature -Online -FeatureName $missing -All -NoRestart | Out-Null
        }
    }
}

function Test-HostingBundle {
    $module = 'HKLM:\SOFTWARE\Microsoft\IIS Extensions\IIS AspNetCore Module V2'
    $runtimes = Join-Path $env:ProgramFiles 'dotnet\shared\Microsoft.AspNetCore.App'
    (Test-Path $module) -and (Test-Path $runtimes) -and [bool](Get-ChildItem $runtimes -Directory | Where-Object Name -like '10.*')
}

# Downloads the newest Hosting Bundle for .NET 10 from Microsoft, checks it, installs it and restarts IIS.
function Install-HostingBundle {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    $ProgressPreference = 'SilentlyContinue'
    Write-Host 'Downloading the ASP.NET Core Hosting Bundle for .NET 10 from Microsoft'
    $releases = Invoke-RestMethod 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json' -UseBasicParsing
    $latest = $releases.releases | Where-Object { $_.'release-version' -eq $releases.'latest-release' } | Select-Object -First 1
    $bundle = $latest.'aspnetcore-runtime'.files | Where-Object name -eq 'dotnet-hosting-win.exe' | Select-Object -First 1
    if (-not $bundle) { throw 'The Hosting Bundle was not found in Microsoft''s list of .NET 10 releases.' }
    $file = Join-Path $env:TEMP 'dotnet-hosting-win.exe'
    Invoke-WebRequest $bundle.url -OutFile $file -UseBasicParsing
    if ((Get-FileHash $file -Algorithm SHA512).Hash -ne $bundle.hash) {
        Remove-Item $file
        throw 'The downloaded Hosting Bundle does not match Microsoft''s checksum, so it was not installed.'
    }
    Write-Host "Installing the Hosting Bundle $($latest.'aspnetcore-runtime'.version)"
    $process = Start-Process $file -ArgumentList '/install', '/quiet', '/norestart' -Wait -PassThru
    Remove-Item $file
    if ($process.ExitCode -notin 0, 3010) { throw "The Hosting Bundle installer failed (exit code $($process.ExitCode))." }
    Write-Host 'Restarting IIS'
    net stop was /y | Out-Null
    net start w3svc | Out-Null
}

function Assert-HostingBundle {
    if (Test-HostingBundle) { return }
    $manual = 'Download it from https://dotnet.microsoft.com/download/dotnet/10.0 (ASP.NET Core Runtime, Windows, Hosting Bundle), install it, then run this script again.'
    if (-not (Confirm-Install 'The ASP.NET Core Hosting Bundle for .NET 10 is missing')) {
        throw "The ASP.NET Core Hosting Bundle for .NET 10 is not installed. $manual"
    }
    try { Install-HostingBundle }
    catch { throw "The Hosting Bundle could not be installed: $($_.Exception.Message) $manual" }
    if (-not (Test-HostingBundle)) { throw "The Hosting Bundle is still missing after installing it. $manual" }
}

# Yes with -InstallMissingFeatures; otherwise asked, when someone is there to answer.
function Confirm-Install([string] $What) {
    if ($InstallMissingFeatures) { return $true }
    if (-not $script:CanAsk) { return $false }
    (Read-Answer "$What. Install it now? (Y/N)" 'Y') -match '^(y|yes|j|ja)$'
}

# Asks, showing the default in brackets. Enter keeps the default.
function Read-Answer([string] $Question, [string] $Default) {
    $answer = Read-Host $(if ($Default) { "$Question [$Default]" } else { $Question })
    if ([string]::IsNullOrWhiteSpace($answer)) { $Default } else { $answer.Trim() }
}

# Lets the folder be picked in a window; typed instead when there is no desktop or the answers are piped in.
function Select-Folder([string] $Question, [string] $Default) {
    if (-not [Console]::IsInputRedirected) {
        try {
            Add-Type -AssemblyName System.Windows.Forms
            $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
            $dialog.Description = "$Question. A folder with other files in it gets a new folder for the app."
            $dialog.ShowNewFolderButton = $true
            $start = $Default
            while ($start -and -not (Test-Path $start)) { $start = Split-Path $start -Parent }
            if ($start) { $dialog.SelectedPath = $start }
            $owner = New-Object System.Windows.Forms.Form -Property @{ TopMost = $true }
            Write-Host "$Question`: pick it in the window that opened."
            $result = $dialog.ShowDialog($owner)
            $owner.Dispose()
            if ($result -eq [System.Windows.Forms.DialogResult]::OK) {
                Write-Host "  $($dialog.SelectedPath)"
                return $dialog.SelectedPath
            }
            Write-Host 'No folder picked in the window, so type it instead.'
        }
        catch {
            # No desktop, e.g. Server Core.
        }
    }
    Read-Answer $Question $Default
}

# The environment written in the web.config in $Folder, or ''.
function Get-AppEnvironment([string] $Folder) {
    $path = Join-Path $Folder 'web.config'
    if (-not (Test-Path $path)) { return '' }
    $variable = ([xml](Get-Content $path -Raw)).SelectSingleNode("//aspNetCore/environmentVariables/environmentVariable[@name='ASPNETCORE_ENVIRONMENT']")
    if ($variable) { $variable.GetAttribute('value') } else { '' }
}

# Writes the environment in the web.config in $Folder, so the app runs as that environment.
function Set-AppEnvironment([string] $Folder, [string] $Name) {
    $path = Join-Path $Folder 'web.config'
    [xml] $config = Get-Content $path -Raw
    $aspNetCore = $config.SelectSingleNode('//aspNetCore')
    if (-not $aspNetCore) { throw "The web.config in $Folder has no aspNetCore section." }
    $variables = $aspNetCore.SelectSingleNode('environmentVariables')
    if (-not $variables) { $variables = $aspNetCore.AppendChild($config.CreateElement('environmentVariables')) }
    $variable = $variables.SelectSingleNode("environmentVariable[@name='ASPNETCORE_ENVIRONMENT']")
    if (-not $variable) {
        $variable = $variables.AppendChild($config.CreateElement('environmentVariable'))
        $variable.SetAttribute('name', 'ASPNETCORE_ENVIRONMENT')
    }
    $variable.SetAttribute('value', $Name)
    $config.Save($path)
}

# Test, Preprod and Prod written the usual way; any other name as typed.
function Format-Environment([string] $Name) {
    $known = 'Test', 'Preprod', 'Prod' | Where-Object { $_ -eq $Name.Trim() }
    if ($known) { return $known }
    if ($Name.Trim() -notmatch '^[A-Za-z][A-Za-z0-9]*$') { throw "'$Name' is not an environment name: use letters and digits, e.g. Test, Preprod or Prod." }
    $Name.Trim()
}

# The team's database for an environment: PipelineBuild for prod, PipelineBuild.PP for preprod, else PipelineBuild.<environment>.
function Get-DatabaseName([string] $Name) {
    switch ($Name) {
        'Prod' { 'PipelineBuild' }
        'Production' { 'PipelineBuild' }
        'Preprod' { 'PipelineBuild.PP' }
        default { "PipelineBuild.$Name" }
    }
}

# The SQL Server in the app's connection string in $SettingsFile, or ''.
function Get-SqlServer([string] $SettingsFile) {
    if (-not (Test-Path $SettingsFile)) { return '' }
    $connection = (Get-Content $SettingsFile -Raw | ConvertFrom-Json).ConnectionStrings.PipelineBuilder
    if (-not $connection) { return '' }
    (New-Object System.Data.SqlClient.SqlConnectionStringBuilder $connection).DataSource
}

# Windows login, encrypted; the certificate checked unless $TrustCertificate.
function New-SqlConnectionString([string] $Server, [string] $Database, [bool] $TrustCertificate) {
    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $builder['Data Source'] = $Server
    $builder['Initial Catalog'] = $Database
    $builder['Integrated Security'] = $true
    $builder['Encrypt'] = $true
    $builder['TrustServerCertificate'] = $TrustCertificate
    $builder['Application Name'] = 'PipelineBuilder'
    $builder.ConnectionString
}

# Runs $Query with its values as parameters and returns the first value.
function Invoke-Sql([string] $ConnectionString, [string] $Query, [hashtable] $Parameters = @{}) {
    $connection = New-Object System.Data.SqlClient.SqlConnection $ConnectionString
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = $Query
        $command.CommandTimeout = 120
        foreach ($name in $Parameters.Keys) { [void] $command.Parameters.AddWithValue($name, $Parameters[$name]) }
        $command.ExecuteScalar()
    }
    finally {
        $connection.Dispose()
    }
}

# Makes the database, its tables and the app pool's login, and returns the app's connection string.
function Install-Database([string] $Server, [string] $Database) {
    $trust = [bool] $TrustSqlServerCertificate
    $master = New-SqlConnectionString $Server 'master' $trust
    try { $machine = Invoke-Sql $master "SELECT CAST(SERVERPROPERTY('MachineName') AS nvarchar(128))" }
    catch {
        $message = $_.Exception.GetBaseException().Message
        if ($trust -or $message -notmatch 'certificate') {
            throw "Could not connect to SQL Server '$Server' as $env:USERDOMAIN\$env:USERNAME (it must be allowed to create databases and logins): $message"
        }
        Write-Host "SQL Server '$Server' has a certificate this server does not trust. The connection is still encrypted." -ForegroundColor Yellow
        if (-not ($script:CanAsk -and (Read-Answer 'Connect anyway? (Y/N)' 'N') -match '^(y|yes|j|ja)$')) {
            throw "SQL Server '$Server' has a certificate this server does not trust. Give it a trusted certificate, or run again with -TrustSqlServerCertificate."
        }
        $trust = $true
        $master = New-SqlConnectionString $Server 'master' $trust
        $machine = Invoke-Sql $master "SELECT CAST(SERVERPROPERTY('MachineName') AS nvarchar(128))"
    }

    Write-Host "Database $Database on $Server"
    Invoke-Sql $master @'
IF DB_ID(@database) IS NULL
BEGIN
    DECLARE @create nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(@database) + N' COLLATE Danish_Norwegian_CI_AS';
    EXEC (@create);
END
'@ @{ '@database' = $Database } | Out-Null
    $collation = Invoke-Sql $master "SELECT CAST(DATABASEPROPERTYEX(@database, 'Collation') AS nvarchar(128))" @{ '@database' = $Database }
    if ($collation -ne 'Danish_Norwegian_CI_AS') { Write-Warning "$Database already existed with collation $collation. It is left as it is." }

    # On the same machine the app pool's own identity logs in; from another machine, this server's computer account.
    if ($machine -eq $env:COMPUTERNAME) { $login = "IIS APPPOOL\$AppPoolName" }
    else {
        try { $login = (New-Object Security.Principal.NTAccount "$env:COMPUTERNAME`$").Translate([Security.Principal.SecurityIdentifier]).Translate([Security.Principal.NTAccount]).Value }
        catch { throw "SQL Server '$Server' is on another machine, so this server must be in the domain: the app logs in there as this server's computer account." }
    }
    Write-Host "The app logs in to SQL Server as $login"
    Invoke-Sql $master @'
IF SUSER_ID(@login) IS NULL
BEGIN
    DECLARE @create nvarchar(max) = N'CREATE LOGIN ' + QUOTENAME(@login) + N' FROM WINDOWS';
    EXEC (@create);
END
'@ @{ '@login' = $login } | Out-Null

    # Reading and writing rows is all the app may do; the tables are made here.
    $connection = New-SqlConnectionString $Server $Database $trust
    Invoke-Sql $connection @'
IF DATABASE_PRINCIPAL_ID(@login) IS NULL
BEGIN
    DECLARE @create nvarchar(max) = N'CREATE USER ' + QUOTENAME(@login) + N' FOR LOGIN ' + QUOTENAME(@login);
    EXEC (@create);
END
DECLARE @roles nvarchar(max) = N'ALTER ROLE db_datareader ADD MEMBER ' + QUOTENAME(@login) + N'; ALTER ROLE db_datawriter ADD MEMBER ' + QUOTENAME(@login);
EXEC (@roles);
'@ @{ '@login' = $login } | Out-Null
    $schema = Join-Path $PhysicalPath 'Database\schema.sql'
    if (-not (Test-Path $schema)) { throw "$schema is missing, so the tables cannot be made." }
    Invoke-Sql $connection (Get-Content $schema -Raw) | Out-Null
    $connection
}

# Certificates that can serve HTTPS: in LocalMachine\My, with a private key, not expired.
function Get-HttpsCertificates {
    @(Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) } | Sort-Object NotAfter -Descending)
}

function Read-Settings {
    if (-not $script:Bound.ContainsKey('SiteName')) {
        $script:SiteName = Read-Answer 'Website name in IIS' $SiteName
    }
    if (-not $script:Bound.ContainsKey('AppPoolName')) { $script:AppPoolName = $script:SiteName }
    # When updating, the folder and the environment the site already has.
    $existing = Get-Website -Name $script:SiteName
    $folder = if ($existing) { [Environment]::ExpandEnvironmentVariables($existing.physicalPath) } else { "C:\inetpub\$($script:SiteName)" }
    if (-not $script:Bound.ContainsKey('Environment')) {
        $current = if ($existing) { Get-AppEnvironment $folder } else { '' }
        $script:Environment = Format-Environment (Read-Answer 'Environment: Test, Preprod or Prod' $(if ($current) { $current } else { 'Prod' }))
    }
    if (-not $script:Bound.ContainsKey('PhysicalPath')) {
        $script:PhysicalPath = Select-Folder 'Folder to install the app in' $folder
        $script:FolderWasAsked = $true
    }
    if (-not ($script:Bound.ContainsKey('CertificateThumbprint') -or $script:Bound.ContainsKey('Port') -or $script:Bound.ContainsKey('HostName'))) {
        $certificates = Get-HttpsCertificates
        Write-Host 'Certificate for HTTPS:'
        for ($i = 0; $i -lt $certificates.Count; $i++) {
            $names = ($certificates[$i].DnsNameList | ForEach-Object Unicode) -join ', '
            Write-Host ('  {0}. {1}  (valid until {2:yyyy-MM-dd})' -f ($i + 1), $(if ($names) { $names } else { $certificates[$i].Subject }), $certificates[$i].NotAfter)
        }
        Write-Host '  F. Import a certificate file (.pfx)'
        Write-Host '  T. Type a thumbprint'
        Write-Host '  S. Skip: use HTTP'
        $pick = (Read-Answer 'Choose' $(if ($certificates.Count -gt 0) { '1' } else { 'S' })).ToUpperInvariant()
        $number = 0
        $chosen = $null
        if ($pick -eq 'F') {
            $file = (Read-Answer 'Path to the .pfx file' '').Trim('"')
            $chosen = Get-Item "Cert:\LocalMachine\My\$(Import-HttpsCertificate $file $null)"
        }
        elseif ($pick -eq 'T') {
            $typed = (Read-Answer 'Thumbprint' '') -replace '[^0-9A-Fa-f]', ''
            $chosen = Get-Item "Cert:\LocalMachine\My\$typed" -ErrorAction SilentlyContinue
            if (-not $typed -or -not $chosen) { throw "No certificate with thumbprint '$typed' in Local Computer > Personal (Cert:\LocalMachine\My)." }
            if (-not $chosen.HasPrivateKey) { throw "The certificate $typed has no private key on this server, so it cannot be used for HTTPS." }
        }
        elseif ([int]::TryParse($pick, [ref] $number) -and $number -ge 1 -and $number -le $certificates.Count) {
            $chosen = $certificates[$number - 1]
        }
        elseif ($pick -ne 'S' -and $pick -ne '0') {
            throw "'$pick' is not one of the choices shown."
        }
        if ($chosen) {
            $script:CertificateThumbprint = $chosen.Thumbprint
            $script:HostName = Read-Answer 'Host name users type in the browser' $(@($chosen.DnsNameList | ForEach-Object Unicode)[0])
        }
        if (-not $script:CertificateThumbprint) { $script:HostName = Read-Answer 'Host name users type in the browser (Enter for any)' '' }
        $script:Port = [int](Read-Answer 'Port' $(if ($script:CertificateThumbprint) { '443' } else { '80' }))
    }
    if (-not $script:Bound.ContainsKey('SqlServer')) {
        $current = Get-SqlServer (Join-Path $script:PhysicalPath "appsettings.$(Format-Environment $script:Environment).json")
        $answer = Read-Answer "SQL Server or alias for the team's saved pipelines ($(if ($current) { 'none for no database' } else { 'Enter for none' }))" $current
        $script:SqlServer = if ($answer -eq 'none') { '' } else { $answer }
        $script:SqlServerAsked = $true
    }
    if (-not $script:Bound.ContainsKey('AllowedGroups')) {
        $groups = Read-Answer 'AD groups that may use it, separated by commas (Enter for every domain user)' ''
        $script:AllowedGroups = @($groups -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    }
}

# Ask only when someone is there to answer.
$script:Bound = $PSBoundParameters
$interactive = [Environment]::UserInteractive -and -not $env:CI -and -not $env:TF_BUILD -and
    -not ([Environment]::GetCommandLineArgs() | Where-Object { $_ -like '-noni*' })
$script:CanAsk = $interactive -and -not $NoPrompt

Assert-Administrator
Test-IisFeatures
Assert-HostingBundle
Import-Module WebAdministration

if ($script:CanAsk) { Read-Settings }

# Without an answer: the environment already installed, else Prod.
if (-not $Environment) { $Environment = Get-AppEnvironment $PhysicalPath }
$Environment = Format-Environment $(if ($Environment) { $Environment } else { 'Prod' })

# The app's folder is made an exact copy of the app, so a folder that holds other things is never used as it is:
# a picked folder gets a new folder for the app in it, and a given one stops the script.
$PhysicalPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($PhysicalPath)
$holdsOther = (Test-Path $PhysicalPath) -and -not (Test-Path (Join-Path $PhysicalPath 'PipelineBuilder.Web.dll')) -and
    [bool](Get-ChildItem $PhysicalPath -Force | Select-Object -First 1)
if ($holdsOther) {
    if (-not $script:FolderWasAsked) { throw "$PhysicalPath already holds other files, which installing would delete. Choose an empty folder or the app's own folder." }
    $PhysicalPath = Join-Path $PhysicalPath $SiteName
    Write-Host "That folder holds other files, so the app goes in $PhysicalPath" -ForegroundColor Yellow
}
Write-Host "Installing as environment $Environment in $PhysicalPath"

# Values as one string (from PowerShell 7, or typed): 'A, B' is two groups. A copied thumbprint can hold spaces or hidden characters.
$AllowedGroups = @($AllowedGroups | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$CertificateThumbprint = ($CertificateThumbprint -replace '[^0-9A-Fa-f]', '').ToUpperInvariant()
if ($CertificateThumbprint -and -not (Test-Path "Cert:\LocalMachine\My\$CertificateThumbprint")) {
    throw "No certificate with thumbprint $CertificateThumbprint in Local Computer > Personal (Cert:\LocalMachine\My)."
}

# App pool: ASP.NET Core runs out of the CLR-less pool ("No Managed Code").
if (-not (Test-Path "IIS:\AppPools\$AppPoolName")) {
    Write-Host "Creating app pool $AppPoolName"
    New-WebAppPool -Name $AppPoolName | Out-Null
}
Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name managedRuntimeVersion -Value ''
Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name startMode -Value 'AlwaysRunning'

# The app to install. Looked for in the folder the download was extracted to (the one holding 'deploy'):
# the ready-built app in 'publish', or else the source code, which is built when the .NET 10 SDK is there.
$app = 'PipelineBuilder.Web.dll'
$release = 'https://github.com/jimocanin85-commits/PipelineBuilder/releases/latest/download/PipelineBuilder-iis.zip'
$installed = Test-Path (Join-Path $PhysicalPath $app)

# The folder with the built app inside $Folder, or $null. Builds it from the source code when it can.
function Find-App([string] $Folder) {
    foreach ($candidate in $Folder, (Join-Path $Folder 'publish')) {
        if (Test-Path (Join-Path $candidate $app)) { return (Resolve-Path $candidate).Path }
    }
    $project = Join-Path $Folder 'src\PipelineBuilder.Web\PipelineBuilder.Web.csproj'
    if (-not (Test-Path $project)) { return $null }
    $sdk = if (Get-Command dotnet -ErrorAction SilentlyContinue) { @(& dotnet --list-sdks 2>$null) -match '^10\.' } else { @() }
    if (-not $sdk) {
        throw "'$Folder' holds the source code, not the built app, and the .NET 10 SDK is not installed to build it. Download the ready-built app instead: $release (or the Releases page on GitHub), extract it and run this script from there."
    }
    $output = Join-Path $Folder 'publish'
    Write-Host "Building the app from the source code in $Folder"
    & dotnet publish $project -c Release -o $output | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Building the app failed (exit code $LASTEXITCODE). It needs internet access to nuget.org. See the output above, or download the ready-built app: $release" }
    $global:LASTEXITCODE = 0
    (Resolve-Path $output).Path
}

if ($PublishFolder) {
    $found = Find-App $PublishFolder
    if (-not $found) { throw "'$PublishFolder' has neither the built app ($app) nor its source code." }
    $PublishFolder = $found
}
else {
    $PublishFolder = Find-App (Split-Path $PSScriptRoot -Parent)
    if (-not $PublishFolder -and -not $installed) {
        if (-not $script:CanAsk) {
            throw "There is no app to install. Download $release, extract it and run this script from there, or give -PublishFolder."
        }
        $typed = (Read-Answer 'Folder with the app (the extracted download)' '').Trim('"')
        $PublishFolder = if ($typed) { Find-App $typed }
        if (-not $PublishFolder) { throw "'$typed' has neither the built app ($app) nor its source code. Download $release and extract it." }
    }
}
if ($PublishFolder) { Write-Host "Installing the app from $PublishFolder" }

# Files: stop the pool while copying so no files are locked. The environments' own settings are kept.
New-Item -ItemType Directory -Force -Path $PhysicalPath | Out-Null
if ($PublishFolder -and $PublishFolder.TrimEnd('\') -eq $PhysicalPath.TrimEnd('\')) {
    Write-Host "The app is already in $PhysicalPath, so nothing is copied."
    $PublishFolder = $null
}
if ($PublishFolder) {
    # The extracted app says its environment too, so its web.config matches the installed one.
    Set-AppEnvironment $PublishFolder $Environment
    if ((Get-WebAppPoolState -Name $AppPoolName).Value -eq 'Started') { Stop-WebAppPool -Name $AppPoolName }
    Write-Host "Copying $PublishFolder to $PhysicalPath"
    robocopy $PublishFolder $PhysicalPath /MIR /XF 'appsettings.*.json' /NFL /NDL /NP | Out-Host
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed with exit code $LASTEXITCODE" }
    $global:LASTEXITCODE = 0
    if (-not (Test-Path (Join-Path $PhysicalPath $app))) { throw "The app was not copied to $PhysicalPath." }
}

# The app pool identity needs to read the files.
$acl = Get-Acl $PhysicalPath
$rule = New-Object Security.AccessControl.FileSystemAccessRule("IIS AppPool\$AppPoolName", 'ReadAndExecute', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
$acl.SetAccessRule($rule)
Set-Acl $PhysicalPath $acl

# The environment: in web.config, so the app runs as it, and its own settings file next to it.
Set-AppEnvironment $PhysicalPath $Environment
$settingsFile = Join-Path $PhysicalPath "appsettings.$Environment.json"
$earlier = Join-Path $PhysicalPath 'appsettings.Production.json'
if (-not (Test-Path $settingsFile) -and $Environment -ne 'Production' -and (Test-Path $earlier)) {
    # Installed before there were environments: those settings become this environment's.
    Move-Item $earlier $settingsFile
}
$settings = if (Test-Path $settingsFile) { Get-Content $settingsFile -Raw | ConvertFrom-Json } else { $null }
if (-not $settings) { $settings = New-Object psobject }
if (-not $settings.PSObject.Properties['Authentication']) { $settings | Add-Member -NotePropertyName Authentication -NotePropertyValue (New-Object psobject) }
$authentication = $settings.Authentication
# Typed groups replace the ones in the file; none typed keeps them.
$values = [ordered]@{ Mode = 'Windows' }
if ($AllowedGroups.Count -gt 0 -or -not $authentication.PSObject.Properties['AllowedGroups']) { $values.AllowedGroups = [string[]]$AllowedGroups }
foreach ($name in $values.Keys) {
    if ($authentication.PSObject.Properties[$name]) { $authentication.$name = $values[$name] }
    else { $authentication | Add-Member -NotePropertyName $name -NotePropertyValue $values[$name] }
}
# The team's database: made when a SQL Server is given, removed when 'none' was answered, else kept as it is.
if ($SqlServer) {
    $database = if ($DatabaseName) { $DatabaseName } else { Get-DatabaseName $Environment }
    $connectionString = Install-Database $SqlServer $database
    if (-not $settings.PSObject.Properties['ConnectionStrings']) { $settings | Add-Member -NotePropertyName ConnectionStrings -NotePropertyValue (New-Object psobject) }
    if ($settings.ConnectionStrings.PSObject.Properties['PipelineBuilder']) { $settings.ConnectionStrings.PipelineBuilder = $connectionString }
    else { $settings.ConnectionStrings | Add-Member -NotePropertyName PipelineBuilder -NotePropertyValue $connectionString }
}
elseif ($script:SqlServerAsked -and $settings.PSObject.Properties['ConnectionStrings'] -and $settings.ConnectionStrings.PSObject.Properties['PipelineBuilder']) {
    $settings.ConnectionStrings.PSObject.Properties.Remove('PipelineBuilder')
    Write-Host 'The team database is no longer used (it is not deleted).'
}
Set-Content -Path $settingsFile -Value ($settings | ConvertTo-Json -Depth 10) -Encoding UTF8
$groups = @($authentication.AllowedGroups)
Write-Host "Environment $Environment, settings in $settingsFile. $(if ($groups.Count) { "Access limited to: $($groups -join ', ')" } else { 'Every domain user may use it.' })"

# Website and binding.
# With a host name, HTTPS uses SNI, so other sites on the server can have their own certificate on 443.
$protocol = if ($CertificateThumbprint) { 'https' } else { 'http' }
$sslFlags = if ($CertificateThumbprint -and $HostName) { 1 } else { 0 }
if (-not (Get-Website -Name $SiteName)) {
    Write-Host "Creating website $SiteName ($protocol, port $Port)"
    New-Website -Name $SiteName -PhysicalPath $PhysicalPath -ApplicationPool $AppPoolName -Port $Port -HostHeader $HostName -Ssl:([bool]$CertificateThumbprint) -SslFlags $sslFlags | Out-Null
}
else {
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath -Value $PhysicalPath
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool -Value $AppPoolName
    if (-not (Get-WebBinding -Name $SiteName -Protocol $protocol -Port $Port -HostHeader $HostName)) {
        Write-Host "Adding $protocol on port $Port to website $SiteName"
        New-WebBinding -Name $SiteName -Protocol $protocol -Port $Port -HostHeader $HostName -SslFlags $sslFlags
    }
}
if ($CertificateThumbprint) {
    # The certificate is set on the port (or port and host name), not on the site. Replace another one there.
    $sslBinding = if ($sslFlags -eq 1) { "IIS:\SslBindings\!$Port!$HostName" } else { "IIS:\SslBindings\0.0.0.0!$Port" }
    $current = Get-Item $sslBinding -ErrorAction SilentlyContinue
    if ($current -and $current.Thumbprint -ne $CertificateThumbprint) { Remove-Item $sslBinding; $current = $null }
    if (-not $current) {
        Write-Host "Using certificate $CertificateThumbprint for HTTPS"
        (Get-WebBinding -Name $SiteName -Protocol https -Port $Port -HostHeader $HostName).AddSslCertificate($CertificateThumbprint, 'My')
    }
}

# Windows login on, anonymous access off (stored for this site in applicationHost.config).
Set-WebConfigurationProperty -PSPath 'IIS:\' -Location $SiteName -Filter 'system.webServer/security/authentication/windowsAuthentication' -Name enabled -Value $true
Set-WebConfigurationProperty -PSPath 'IIS:\' -Location $SiteName -Filter 'system.webServer/security/authentication/anonymousAuthentication' -Name enabled -Value $false

Start-WebAppPool -Name $AppPoolName -ErrorAction SilentlyContinue
Start-Website -Name $SiteName -ErrorAction SilentlyContinue

$url = '{0}://{1}:{2}/' -f $protocol, ($(if ($HostName) { $HostName } else { 'localhost' })), $Port
Write-Host "PipelineBuilder is running at $url (Windows login)." -ForegroundColor Green
