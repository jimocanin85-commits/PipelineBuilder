# PipelineBuilder

Enterprise Azure DevOps pipeline builder for cloud, on-premises and hybrid deployments. A Blazor wizard collects your settings and generates a ready-to-use `azure-pipelines.yml`.

## Prerequisites

### To run PipelineBuilder

| What | Details |
|---|---|
| **.NET 10 SDK, 10.0.401 or later** | Required. Pinned in [`global.json`](global.json); earlier SDKs (including 10.0.1xx) are rejected. Download from <https://dotnet.microsoft.com/download/dotnet/10.0>. Check with `dotnet --version`. |
| **Operating system** | Windows, macOS or Linux (anything .NET 10 supports). |
| **Internet access to nuget.org** | Needed the first time you build, to restore packages. |
| **HTTPS development certificate** | Only for the `https://localhost:7150` address: run `dotnet dev-certs https --trust` once. The `http://localhost:5150` address works without it. |
| **Free ports 5150 and 7150** | Set in `src/PipelineBuilder.Web/Properties/launchSettings.json`; change them there if they are taken. |
| **A modern browser** | Edge, Chrome, Firefox or Safari with WebSockets enabled (Blazor Server keeps a live connection). |
| **An editor (optional)** | Visual Studio 2026 (18.9 or later, which includes the .NET 10.0.401 SDK), VS Code with C# Dev Kit, or JetBrains Rider. The command line alone is enough. |

To host it on a Windows server with IIS and Windows login, see the checklist in [docs/INSTALL.md](docs/INSTALL.md#21-checklist).

### To use the generated pipeline in Azure DevOps

Only what your chosen settings need:

| What | When |
|---|---|
| An Azure DevOps project and a Git repository for your app | Always. Commit the downloaded file as `azure-pipelines.yml`, then create a pipeline from it. |
| **Environments** named like the ones in the wizard (e.g. `test`, `preprod`, `prod`) | Always. Add **approvals and checks** to production here; approvals are not set in YAML. |
| **Servers registered as Virtual machine resources** in each environment, running the Azure Pipelines agent | On-premises or hybrid IIS, Windows service, file share or Docker deployments. The agent account needs rights to the target folders, IIS or the service. |
| Windows PowerShell 5.1 and `robocopy` on the servers | On-premises deployments (standard on Windows Server). |
| IIS with the **WebAdministration** module | IIS deployments and IIS health checks. |
| Docker Engine on the servers | Docker container deployments. |
| **Azure Resource Manager service connection** | Key Vault, infrastructure as code, App Service deployments and slot swaps. Enter its name on the wizard's Identity step. |
| **Docker registry service connection** | Docker image artifacts. |
| An agent pool | Microsoft-hosted by default (`ubuntu-latest` for the build, `windows-latest` for deployments and notifications), or your own self-hosted pool on the Build agent step. |
| **Kubernetes service connection** and an environment for each stage | Kubernetes deployments. Keep your manifests in the repository (default `manifests/*.yaml`). |
| **Terraform extension** (Microsoft DevLabs) from the Visual Studio Marketplace | Terraform infrastructure as code (`TerraformTaskV4@4`), plus a storage account for Terraform state. |
| An Azure Key Vault | If you enable Key Vault. |
| A variable group with the variables your settings use | See below. Mark secrets (webhook URLs) as secret. |

Settings you leave empty in the wizard become pipeline variables. Define the ones your pipeline uses:

| Variable | Used for |
|---|---|
| `DEPLOY_PATH`, `SERVICE_NAME` | Windows service and file share deployments |
| `WEBAPP_NAME`, `RESOURCE_GROUP` | App Service deployments, slot swaps, Bicep and ARM |
| `CONTAINER_NAME`, `DOCKER_REGISTRY` | Docker container deployments |
| `AZURE_SERVICE_CONNECTION`, `DOCKER_SERVICE_CONNECTION` | Only if you don't enter the service connection names in the wizard |
| `TF_STATE_RG`, `TF_STATE_STORAGE`, `TF_STATE_CONTAINER` | Terraform state backend |
| `AZURE_SUBSCRIPTION_ID`, `AZURE_LOCATION` | ARM template deployments |
| `NUGET_FEED` | NuGet package artifacts |
| `TEAMS_WEBHOOK_URL`, `CUSTOM_WEBHOOK_URL` | Notifications (names can be changed in the wizard) |
| `K8S_SERVICE_CONNECTION`, `K8S_NAMESPACE`, `K8S_DEPLOYMENT` | Kubernetes deployments and rollback (if not entered in the wizard) |
| `SMTP_HOST`, `SMTP_PORT`, `SMTP_USERNAME`, `SMTP_PASSWORD`, `EMAIL_FROM` | Email notifications (`SMTP_PASSWORD` as a secret; port defaults to 587) |

The wizard's **Validation** step lists what your particular pipeline still needs.

## Install and run

**Full guide: [docs/INSTALL.md](docs/INSTALL.md)**, covering running locally, installing on IIS with Windows login, updating, configuration and troubleshooting.

Quick start, locally:

```bash
git clone https://github.com/jimocanin85-commits/PipelineBuilder.git
cd PipelineBuilder
dotnet run --project src/PipelineBuilder.Web
```

Open <http://localhost:5150>, go through the wizard and download `azure-pipelines.yml` on the **Export** step.

On a Windows server with IIS, as Administrator:

```powershell
dotnet publish src/PipelineBuilder.Web -c Release -o .\publish
.\deploy\Install-PipelineBuilder.ps1 -PublishFolder .\publish -InstallMissingFeatures
```

Users then sign in automatically with their Windows account. Requirements, HTTPS, AD-group restriction and Kerberos are covered in the guide.

## Test

```bash
dotnet test
```

The build treats warnings as errors (`Directory.Build.props`), package versions live in one place (`Directory.Packages.props`), and Dependabot opens weekly update pull requests for NuGet packages and GitHub Actions.

Runs the unit tests, the generated-YAML checks (every strategy, artifact type, deployment kind and target), the wizard component tests (bUnit) and an in-memory smoke test of the web app, including the Windows login setup. It also runs the architecture tests (the layering rules) and compares every generated pipeline with its approved copy in `tests/PipelineBuilder.Tests/Golden/`; see [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md#golden-files) for how to update those after an intended change. CI runs the same on every pull request and push to `main` (`.github/workflows/ci.yml`) and publishes a coverage report (job summary and the `coverage-report` artifact), failing below 90 % line or 80 % branch coverage.

A second workflow (`.github/workflows/iis.yml`) runs on pull requests that touch the web app or the install script: on a Windows runner it installs PipelineBuilder in IIS with `deploy/Install-PipelineBuilder.ps1` and checks that anonymous requests get 401 and Windows-authenticated requests get the wizard.

## Templates

The template catalogue ships in `src/PipelineBuilder.Core/Templates/templates.json`. To add your own templates (or replace a built-in one with the same `id`), point the app at a JSON file in the same format:

```json
{ "PipelineBuilder": { "TemplatesFile": "C:\\config\\our-templates.json" } }
```

in `appsettings.json`, or set the environment variable `PipelineBuilder__TemplatesFile`. Each template has an `id`, `name`, `description`, `category`, `riskLevel` and a `settings` block with any of `projectType`, `deploymentTarget`, `environments`, `artifactType`, `deploymentKind`, `strategy`, `rollbackEnabled`, `iacTool`, `iacWorkingDirectory` and `customDeployScript`.

## Features

- **Guided wizard** with validation on every step, a **Validation** step that links each issue to the step that fixes it, a live YAML preview, and download or copy on the **Export** step
- **Save and reopen settings**: download your wizard settings as a JSON file on the Export step and open it again on the **Project type** step to change the pipeline later
- **Windows login when hosted on IIS** (Kerberos/NTLM), optionally limited to AD groups; off when running locally
- **Variable groups** at pipeline or environment scope, and **Azure Key Vault** secrets loaded in each deploy job
- **Projects**: .NET (restore, build, test, publish) and Node.js (npm ci, build, test)
- **Artifacts**: pipeline or build artifact, zip, Docker image, NuGet package
- **Deployments**: IIS, Windows service, file share, Azure App Service, Docker container, Kubernetes (manifests with rollout undo), or a custom script
- **Strategies**: standard, rolling (native, on registered servers), slot swap, plus blue-green and canary placeholders
- **Backups and automatic rollback** on failure, per environment
- **Health checks**: HTTP (per-environment URL), IIS app pool, Windows service, port, custom PowerShell
- **Notifications**: Teams, custom webhook, email over SMTP, on success and/or failure
- **Infrastructure as code**: Terraform, Bicep, ARM, PowerShell, per environment
- **Governance validation**: approvals reminder, secrets scanning, health check and rollback requirements, naming, forbidden and required tasks
- **Repository scan and templates**, **task explanations**, and an **agent diagnostics** script

## Solution structure

```
src/PipelineBuilder.Core/          Models, services and stage generators (no UI)
src/PipelineBuilder.Web/           Blazor Server wizard (one component per step)
tests/PipelineBuilder.Tests/       Unit, generated-YAML, golden-file, architecture, bUnit and web smoke tests
```

The layers, their rules and the architecture goals are described in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md), with the decisions behind them in [docs/adr](docs/adr/README.md).

## Generated pipeline

Flow: **Build → Deploy_{env} (one stage per environment) → Notify_Success / Notify_Failure**

The Build stage compiles once and then tests and packages the same output (`--no-build`) in a single job. Microsoft-hosted builds run on `ubuntu-latest`; deployments and notifications use `windows-latest` (or your self-hosted pool).

Each `Deploy_{env}` stage contains:

1. An optional `Infrastructure` job (Terraform/Bicep/ARM/PowerShell) for that environment, which runs first.
2. A `deployment` job targeting the Azure DevOps environment, so its approvals and checks apply. It:
   - downloads the artifact and loads Key Vault secrets
   - backs up the current version to `{BackupPath}\{env}\{BuildId}`, keeping the last N backups
   - deploys according to the deployment kind
   - runs the health checks (`{environment}` in a URL is replaced per environment)
   - rolls back from that backup in its `on: failure` hook if any step fails

Deployments to `prod`/`production` only run from the release branch, `main` by default; change it on the wizard's **Environments** step.
