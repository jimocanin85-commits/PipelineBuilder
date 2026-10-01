# Installing PipelineBuilder

There are two ways to run PipelineBuilder:

| | Who | Login | Section |
|---|---|---|---|
| **Locally** | A developer trying it out or working on the code | None | [Run locally](#1-run-locally) |
| **On an IIS server** | The team, in the browser | Windows login (Kerberos/NTLM), optionally limited to AD groups | [Install on IIS](#2-install-on-an-iis-server) |

Using the pipelines PipelineBuilder generates is a separate setup in Azure DevOps. See [section 3](#3-before-the-first-generated-pipeline-runs).

---

## 1. Run locally

### Prerequisites

- **.NET 10 SDK 10.0.401 or later**. It's pinned in `global.json`, and older SDKs (including 10.0.1xx) are rejected. Download it from <https://dotnet.microsoft.com/download/dotnet/10.0>, then check with `dotnet --version`.
- **Git**.
- Windows, macOS or Linux.
- Internet access to nuget.org the first time you build.
- Free ports 5150 (http) and 7150 (https).

### Steps

```bash
git clone https://github.com/jimocanin85-commits/SimlifiezYaml.git
cd SimlifiezYaml
dotnet run --project src/PipelineBuilder.Web
```

Open <http://localhost:5150>. There's no login locally, because Windows login is off in the Development environment.

- To use <https://localhost:7150>, trust the development certificate once: `dotnet dev-certs https --trust`.
- To reload on code changes: `dotnet watch --project src/PipelineBuilder.Web`.
- To run all tests: `dotnet test`.
- If a port is taken, change it in `src/PipelineBuilder.Web/Properties/launchSettings.json`.

---

## 2. Install on an IIS server

The install script `deploy/Install-PipelineBuilder.ps1` does the IIS work: it adds the features, app pool, website, Windows login and file permissions. The same script also updates an existing installation. The [IIS workflow](../.github/workflows/iis.yml) runs it on every change to the web app and checks the login, so the steps below are tested.

### 2.1 Checklist

**Build machine** (your PC, or any machine with the code):
- [ ] .NET 10 SDK 10.0.401 or later

**Server:**
- [ ] Windows Server 2019 or later (Windows 10/11 also works for a test)
- [ ] **Joined to the domain**, which Windows login needs
- [ ] Administrator access
- [ ] **ASP.NET Core Hosting Bundle for .NET 10**, installed *after* IIS (see 2.3)
- [ ] IIS features: Web Server, **WebSockets** and **Windows Authentication**. The script can install them with `-InstallMissingFeatures`.
- [ ] A free port: 443 for HTTPS (recommended) or 80, open in the firewall

**For HTTPS and a friendly address (recommended):**
- [ ] A DNS name, e.g. `pipelines.contoso.local`, pointing to the server
- [ ] A certificate for that name in the server's *Local Computer → Personal* store, and its thumbprint
- [ ] An SPN for the name, so Kerberos works (see 2.6)

**To limit who can use it (optional):**
- [ ] One or more AD groups, e.g. `CONTOSO\Platform-Team`

### 2.2 Build the app

On the build machine, from the repository folder:

```powershell
dotnet publish src/PipelineBuilder.Web -c Release -o .\publish
```

Copy two things to the server, e.g. to `C:\Install\PipelineBuilder`:
- the **`publish`** folder
- the **`deploy`** folder (it holds the install script)

### 2.3 Install the Hosting Bundle

Download the **Hosting Bundle** for .NET 10 from <https://dotnet.microsoft.com/download/dotnet/10.0> (under *ASP.NET Core Runtime*, Windows), run it on the server, and restart IIS:

```powershell
net stop was /y
net start w3svc
```

> If IIS isn't installed yet, run the install script with `-InstallMissingFeatures` first (step 2.4), then install the Hosting Bundle, then run the script again. If the Hosting Bundle was installed **before** IIS, run its installer again and choose **Repair**. Otherwise IIS doesn't know the ASP.NET Core module, and you get error 500.19.

The script checks for the Hosting Bundle and stops with a clear message if it's missing.

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

What the script does:
1. Checks that it runs as Administrator, checks the IIS features and the Hosting Bundle.
2. Creates or updates the app pool: *No Managed Code*, always running.
3. Copies the app into `-PhysicalPath`, stopping the app pool while copying, and gives the app pool read access.
4. With `-AllowedGroups`, writes `appsettings.Production.json` with the groups.
5. Creates or updates the website and its binding (HTTP, or HTTPS with the certificate).
6. Turns **Windows Authentication on** and **anonymous access off** for the site.
7. Starts the site and prints its address.

### 2.5 Open the firewall and check

If users connect from other machines, open the port:

```powershell
New-NetFirewallRule -DisplayName 'PipelineBuilder' -Direction Inbound -Protocol TCP -LocalPort 443 -Action Allow
```

Then open the address in a browser on a domain PC. You should see the wizard, with **"Signed in as DOMAIN\you"** at the top.

### 2.6 Automatic login in the browser (Kerberos)

Users are only signed in automatically when the browser trusts the site as an intranet site. Otherwise they get a login prompt.

- **Edge and Chrome** use the Windows *Local intranet* zone. Add `https://pipelines.contoso.local` there, ideally for everyone through Group Policy: *Site to Zone Assignment List*, value `1`.
- **Firefox:** set `network.negotiate-auth.trusted-uris` to `pipelines.contoso.local`.

With a DNS name other than the server's own name, register an **SPN** so Kerberos works and doesn't fall back to NTLM. Run this once as a domain admin. The app pool's identity on the network is the server's computer account:

```powershell
setspn -S HTTP/pipelines.contoso.local CONTOSO\WEBSERVER01$
```

### 2.7 Update to a new version

Build a new `publish` folder (step 2.2), copy it to the server, and run the same script again with the same parameters. It replaces the files and keeps `appsettings.Production.json`, so your group restriction and other settings stay.

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
| `Authentication:Mode` | Empty (default) means Windows login everywhere except Development. `Windows` means always. `None` turns login off, so only use it behind another access control. |
| `Authentication:AllowedGroups` | AD groups (`DOMAIN\Group`) allowed to use the app. When empty, any signed-in domain user can. |
| `PipelineBuilder:TemplatesFile` | Optional JSON file with your own templates (see README → Templates) |

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

---

## 3. Before the first generated pipeline runs

The YAML that PipelineBuilder generates expects some setup in Azure DevOps: environments with approvals, registered servers for on-premises deployments, service connections, and the pipeline variables your settings use. The README section [Prerequisites → To use the generated pipeline in Azure DevOps](../README.md#to-use-the-generated-pipeline-in-azure-devops) lists them. The wizard's **Validation** step shows what your particular pipeline still needs.
