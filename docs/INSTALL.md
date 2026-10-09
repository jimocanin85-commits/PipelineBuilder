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

One script, `deploy/Install-PipelineBuilder.ps1`, does everything: it installs what IIS is missing, installs the app, and sets up the app pool, the website, HTTPS, the Windows login and the file permissions. Run it again to update. It is tested on every change by the [IIS workflow](../.github/workflows/iis.yml), on a machine that starts without IIS.

### 2.1 What you need

- Windows Server 2019 or later, **joined to the domain** (Windows login needs it), and Administrator access.
- Internet access from the server, so the script can download the Hosting Bundle from Microsoft. Without it, install the [Hosting Bundle for .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0) yourself first.

For HTTPS, which we recommend:

- A DNS name that points to the server, e.g. `pipelines.contoso.local`.
- A certificate for that name: already in the server's *Local Computer → Personal* store, or as a `.pfx` file.

To limit who can use it: one or more AD groups, e.g. `CONTOSO\Platform-Team`.

### 2.2 Download

Download **[PipelineBuilder-iis.zip](https://github.com/jimocanin85-commits/PipelineBuilder/releases/latest/download/PipelineBuilder-iis.zip)**, the ready-built app with the install script. It is built from the newest version on every change.

Before extracting it, unblock it, so Windows lets the script run: right-click the zip → *Properties* → tick **Unblock** → *OK*. Or in PowerShell:

```powershell
Unblock-File .\PipelineBuilder-iis.zip
```

Extract it on the server, e.g. to `C:\Install\PipelineBuilder`.

The source code from GitHub (*Code → Download ZIP*) works too, but only where the .NET 10 SDK is installed: the script then builds the app first.

### 2.3 Run the install script

Open **PowerShell as Administrator** (Windows PowerShell or PowerShell 7) and run:

```powershell
C:\Install\PipelineBuilder\deploy\Install-PipelineBuilder.ps1
```

If IIS or the Hosting Bundle is missing, it says so and installs it when you answer Y. Then it asks how to install the app. Enter keeps the value in brackets:

```text
IIS is missing Web-Server, Web-WebSockets, Web-Windows-Auth. Install it now? (Y/N) [Y]:
The ASP.NET Core Hosting Bundle for .NET 10 is missing. Install it now? (Y/N) [Y]:
Website name in IIS [PipelineBuilder]:
Folder to install the app in [C:\inetpub\PipelineBuilder]: D:\Apps\PipelineBuilder
Certificate for HTTPS:
  1. pipelines.contoso.local  (valid until 2027-09-30)
  F. Import a certificate file (.pfx)
  T. Type a thumbprint
  S. Skip: use HTTP
Choose [1]:
Host name users type in the browser [pipelines.contoso.local]:
Port [443]:
AD groups that may use it, separated by commas (Enter for every domain user): CONTOSO\Platform-Team
...
PipelineBuilder is running at https://pipelines.contoso.local:443/ (Windows login).
```

For HTTPS, pick a certificate already on the server (those in *Local Computer → Personal* with a private key that have not expired), import a `.pfx` file (it asks for the path and the password), type a thumbprint, or press **S** to skip HTTPS and use HTTP. Run the script again later to add HTTPS. The app pool gets the website's name.

**Without questions**, e.g. in a script, give the values as parameters. Only what is missing is asked; `-NoPrompt` asks nothing and uses the defaults:

```powershell
.\deploy\Install-PipelineBuilder.ps1 -InstallMissingFeatures `
    -SiteName PipelineBuilder -PhysicalPath D:\Apps\PipelineBuilder `
    -HostName pipelines.contoso.local -Port 443 `
    -CertificateThumbprint 0123456789ABCDEF0123456789ABCDEF01234567 `
    -AllowedGroups 'CONTOSO\Platform-Team', 'CONTOSO\Release-Managers'
```

Instead of `-CertificateThumbprint`, `-CertificateFile .\pipelines.pfx` imports the file; the password is asked for, or given with `-CertificatePassword`.

| Parameter | Default | Meaning |
|---|---|---|
| `-PublishFolder` | the extracted download | A folder with the built app, or with the source code to build it |
| `-SiteName` | `PipelineBuilder` | IIS website name |
| `-AppPoolName` | `PipelineBuilder`, or the website's name when the script asks | IIS app pool name |
| `-PhysicalPath` | `C:\inetpub\PipelineBuilder` | Where the app is installed |
| `-HostName` | none | DNS name in the binding, e.g. `pipelines.contoso.local` |
| `-Port` | `80` | Port of the binding |
| `-CertificateThumbprint` | none | Certificate in *LocalMachine\My*. When set, the binding uses HTTPS. |
| `-CertificateFile` | none | A `.pfx` file to import into *LocalMachine\My* and use for HTTPS |
| `-CertificatePassword` | asked | The `.pfx` file's password, as a SecureString |
| `-AllowedGroups` | none | Only members of these AD groups may use the app |
| `-InstallMissingFeatures` | asked | Install missing IIS features and the Hosting Bundle without asking |
| `-NoPrompt` | off | Ask nothing; use the parameters and the defaults. Nothing is asked in a pipeline either. |

### 2.4 Open the firewall

If users connect from other machines, open the port:

```powershell
New-NetFirewallRule -DisplayName 'PipelineBuilder' -Direction Inbound -Protocol TCP -LocalPort 443 -Action Allow
```

### 2.5 Automatic login in the browser (Kerberos)

Users are signed in automatically only when the browser treats the site as an intranet site. Otherwise they are asked to log in.

- **Edge and Chrome** use the Windows *Local intranet* zone. Add `https://pipelines.contoso.local` there, ideally for everyone through Group Policy: *Site to Zone Assignment List*, value `1`.
- **Firefox:** set `network.negotiate-auth.trusted-uris` to `pipelines.contoso.local`.

If the DNS name is not the server's own name, register an **SPN** for it, so Kerberos works. Run this once as a domain admin, with the server's computer account:

```powershell
setspn -S HTTP/pipelines.contoso.local CONTOSO\WEBSERVER01$
```

### 2.6 Check that it works

Open the address in a browser on a domain PC. You should see the wizard, with **"Signed in as DOMAIN\you"** at the top.

### 2.7 Update to a new version

Download the zip again (2.2), extract it and run the script again (2.3). It suggests the folder the site already uses. Your settings in `appsettings.Production.json` are kept. To move a site from HTTP to HTTPS, run it again and pick the certificate.

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
| A login prompt appears every time | The site isn't in the intranet zone, the SPN is missing, or users browse by IP address | See 2.5. Always use the DNS name, not the IP address. |
| **HTTP Error 401.2** | Windows Authentication is off for the site | Run the script again. It turns Windows Authentication on and anonymous access off. |
| **403 Forbidden** after signing in | The user isn't in one of the `AllowedGroups` | Add the user to the group. The user must sign out of Windows and back in to get the new group membership. Group names must be written `DOMAIN\Group`. |
| Script: *Missing IIS features* | IIS or one of its features isn't installed, and you answered N | Run it again and answer Y, or add `-InstallMissingFeatures` |
| Script: *holds the source code, not the built app* | You downloaded the source code, and the .NET 10 SDK isn't on the server | Download `PipelineBuilder-iis.zip` instead (2.2) |
| Script: *cannot be loaded* or *is not digitally signed* | Windows blocks scripts from a downloaded zip | Unblock the zip before extracting it (2.2), or run `Get-ChildItem C:\Install\PipelineBuilder -Recurse \| Unblock-File` |
| Script: *Run this script as Administrator* | PowerShell wasn't started as Administrator | Right-click PowerShell → *Run as administrator* |
