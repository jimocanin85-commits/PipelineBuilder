# Installing PipelineBuilder

- **On your own machine**, to try it or work on the code: [section 1](#1-run-locally). No login.
- **On an IIS server**, for the team: [section 2](#2-install-on-an-iis-server). Users sign in with their Windows account.

What the generated pipelines need in Azure DevOps is in [AZURE-DEVOPS.md](AZURE-DEVOPS.md).

---

## 1. Run locally

The three commands to start it are in the [README](../README.md#run-it). There is no login when you run it this way.

- Older SDKs than 10.0.401 are rejected. Check yours with `dotnet --version`.
- The first build needs internet access to nuget.org.
- For <https://localhost:7150>, first run `dotnet dev-certs https --trust` once.
- To reload on code changes: `dotnet watch --project src/PipelineBuilder.Web`.
- If a port is taken, change it in `src/PipelineBuilder.Web/Properties/launchSettings.json`.

---

## 2. Install on an IIS server

The script `deploy/Install-PipelineBuilder.ps1` sets up IIS: the features, the app pool, the website, the Windows login and the file permissions. Run it again to update. It is tested on every change by the [IIS workflow](../.github/workflows/iis.yml).

### 2.1 What you need

On the machine you build on: the .NET 10 SDK, 10.0.401 or later.

On the server:

- Windows Server 2019 or later, **joined to the domain** (Windows login needs it), and Administrator access.
- The **ASP.NET Core Hosting Bundle for .NET 10**, installed after IIS (see 2.3).
- A free port, open in the firewall: 443 for HTTPS, or 80.

For HTTPS, which we recommend:

- A DNS name that points to the server, e.g. `pipelines.contoso.local`.
- A certificate for that name in the server's *Local Computer → Personal* store. You need its thumbprint.
- An SPN for the name (see 2.6).

To limit who can use it: one or more AD groups, e.g. `CONTOSO\Platform-Team`.

### 2.2 Build the app

On the build machine, from the repository folder:

```powershell
dotnet publish src/PipelineBuilder.Web -c Release -o .\publish
```

Copy the `publish` folder and the `deploy` folder to the server, e.g. to `C:\Install\PipelineBuilder`.

### 2.3 Install the Hosting Bundle

Download the **Hosting Bundle** for .NET 10 from <https://dotnet.microsoft.com/download/dotnet/10.0> (under *ASP.NET Core Runtime*, Windows), run it on the server, and restart IIS:

```powershell
net stop was /y
net start w3svc
```

If IIS is not installed yet, do it in this order: run the install script with `-InstallMissingFeatures` (2.4), install the Hosting Bundle, run the script again.

### 2.4 Run the install script

Open **PowerShell as Administrator** on the server. Windows PowerShell and PowerShell 7 both work.

**Quick test over HTTP**, reachable as `http://<server>:8080/`:

```powershell
cd C:\Install\PipelineBuilder
.\deploy\Install-PipelineBuilder.ps1 -PublishFolder .\publish -Port 8080 -InstallMissingFeatures
```

**Production over HTTPS**, with a DNS name and limited to an AD group:

```powershell
.\deploy\Install-PipelineBuilder.ps1 -PublishFolder .\publish -InstallMissingFeatures `
    -HostName pipelines.contoso.local -Port 443 `
    -CertificateThumbprint 0123456789ABCDEF0123456789ABCDEF01234567 `
    -AllowedGroups 'CONTOSO\Platform-Team', 'CONTOSO\Release-Managers'
```

| Parameter | Default | Meaning |
|---|---|---|
| `-PublishFolder` | none | The `publish` folder from step 2.2. Leave it out to only reconfigure IIS. |
| `-SiteName` | `PipelineBuilder` | IIS website name |
| `-AppPoolName` | `PipelineBuilder` | IIS app pool name |
| `-PhysicalPath` | `C:\inetpub\PipelineBuilder` | Where the app is installed |
| `-HostName` | none | DNS name in the binding, e.g. `pipelines.contoso.local` |
| `-Port` | `80` | Port of the binding |
| `-CertificateThumbprint` | none | Certificate in *LocalMachine\My*. When set, the binding uses HTTPS. |
| `-AllowedGroups` | none | Only members of these AD groups may use the app |
| `-InstallMissingFeatures` | off | Install IIS, WebSockets and Windows Authentication if they're missing |

The script turns Windows login on and anonymous access off, starts the site and prints its address.

### 2.5 Open the firewall and check

If users connect from other machines, open the port:

```powershell
New-NetFirewallRule -DisplayName 'PipelineBuilder' -Direction Inbound -Protocol TCP -LocalPort 443 -Action Allow
```

Then open the address in a browser on a domain PC. You should see the wizard, with **"Signed in as DOMAIN\you"** at the top.

### 2.6 Automatic login in the browser (Kerberos)

Users are signed in automatically only when the browser treats the site as an intranet site. Otherwise they are asked to log in.

- **Edge and Chrome** use the Windows *Local intranet* zone. Add `https://pipelines.contoso.local` there, ideally for everyone through Group Policy: *Site to Zone Assignment List*, value `1`.
- **Firefox:** set `network.negotiate-auth.trusted-uris` to `pipelines.contoso.local`.

If the DNS name is not the server's own name, register an **SPN** for it, so Kerberos works. Run this once as a domain admin, with the server's computer account:

```powershell
setspn -S HTTP/pipelines.contoso.local CONTOSO\WEBSERVER01$
```

### 2.7 Update to a new version

Build a new `publish` folder (2.2), copy it to the server and run the script again with the same parameters. Your settings in `appsettings.Production.json` are kept.

### 2.8 Configuration

Settings go in `appsettings.Production.json` in the install folder (`C:\inetpub\PipelineBuilder`), or in environment variables on the app pool. Restart the app pool after changing them: `Restart-WebAppPool PipelineBuilder`.

```json
{
  "Authentication": {
    "Mode": "Windows",
    "AllowedGroups": [ "CONTOSO\\Platform-Team" ]
  },
  "PipelineBuilder": {
    "TemplatesFile": "C:\\PipelineBuilder\\our-templates.json"
  }
}
```

| Setting | Meaning |
|---|---|
| `Authentication:Mode` | Empty means Windows login, except when you run it locally. `Windows` means always. `None` turns login off; use it only when something else controls access. |
| `Authentication:AllowedGroups` | AD groups (`DOMAIN\Group`) allowed to use the app. When empty, any signed-in domain user can. |
| `PipelineBuilder:TemplatesFile` | Optional JSON file with your own templates, in the same format as `src/PipelineBuilder.Core/Templates/templates.json`. A template with the same `id` replaces the built-in one. |

As environment variables, use `__` instead of `:`, e.g. `Authentication__Mode`.

### 2.9 Uninstall

```powershell
Import-Module WebAdministration
Remove-Website -Name PipelineBuilder
Remove-WebAppPool -Name PipelineBuilder
Remove-Item C:\inetpub\PipelineBuilder -Recurse -Force
```

### 2.10 Troubleshooting

| What you see | Likely cause | Fix |
|---|---|---|
| **HTTP Error 500.19**, code `0x8007000d` | Hosting Bundle missing, or installed before IIS | Install the Hosting Bundle, or run its installer again and choose *Repair*; then `net stop was /y` and `net start w3svc` |
| **HTTP Error 500.30** or **502.5** | The app fails at startup | See *Event Viewer → Windows Logs → Application* (source *IIS AspNetCore Module V2*). For more detail, set `stdoutLogEnabled="true"` in `web.config` in the install folder, create a `logs` folder, and check `logs\stdout_*.log`. |
| The page loads but buttons do nothing, or it keeps saying it's reconnecting | The IIS **WebSockets** feature is missing | Run the script with `-InstallMissingFeatures`, or add *WebSocket Protocol* in Server Manager |
| A login prompt appears every time | The site isn't in the intranet zone, the SPN is missing, or users browse by IP address | See 2.6. Always use the DNS name, not the IP address. |
| **HTTP Error 401.2** | Windows Authentication is off for the site | Run the script again. It turns Windows Authentication on and anonymous access off. |
| **403 Forbidden** after signing in | The user isn't in one of the `AllowedGroups` | Add the user to the group. The user must sign out of Windows and back in to get the new group membership. Group names must be written `DOMAIN\Group`. |
| Script: *Missing IIS features* | IIS or one of its features isn't installed | Run it with `-InstallMissingFeatures` |
| Script: *Run this script as Administrator* | PowerShell wasn't started as Administrator | Right-click PowerShell → *Run as administrator* |
