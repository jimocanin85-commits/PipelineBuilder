# SimlifiezYaml

Enterprise Azure DevOps pipeline builder for cloud, on-premises and hybrid deployments. A Blazor wizard collects your settings and generates a ready-to-use `azure-pipelines.yml`.

## Prerequisites

### To run SimlifiezYaml

| What | Details |
|---|---|
| **.NET 10 SDK, 10.0.401 or later** | Required. Pinned in [`global.json`](global.json); earlier SDKs (including 10.0.1xx) are rejected. Download from <https://dotnet.microsoft.com/download/dotnet/10.0>. Check with `dotnet --version`. |
| **Operating system** | Windows, macOS or Linux (anything .NET 10 supports). |
| **Internet access to nuget.org** | Needed the first time you build, to restore packages. |
| **HTTPS development certificate** | Only for the `https://localhost:7150` address: run `dotnet dev-certs https --trust` once. The `http://localhost:5150` address works without it. |
| **Free ports 5150 and 7150** | Set in `src/SimlifiezYaml.Web/Properties/launchSettings.json`; change them there if they are taken. |
| **A modern browser** | Edge, Chrome, Firefox or Safari with WebSockets enabled (Blazor Server keeps a live connection). |
| **An editor (optional)** | Visual Studio 2026 (18.9 or later, which includes the .NET 10.0.401 SDK), VS Code with C# Dev Kit, or JetBrains Rider. The command line alone is enough. |

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
| An agent pool | Microsoft-hosted (`windows-latest`) by default, or your own self-hosted pool. |
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
| `K8S_NAMESPACE` | The AKS template's placeholder deploy script |

The wizard's **Validation** step lists what your particular pipeline still needs.

## Run

```bash
dotnet run --project src/SimlifiezYaml.Web
```

Open <http://localhost:5150> (or <https://localhost:7150>), go through the wizard and download `azure-pipelines.yml` on the **Export** step. Use `dotnet watch --project src/SimlifiezYaml.Web` to reload on code changes.

If the app doesn't start because a port is in use, stop the other process or change the port in `launchSettings.json`.

## Test

```bash
dotnet test
```

The build treats warnings as errors (`Directory.Build.props`), package versions live in one place (`Directory.Packages.props`), and Dependabot opens weekly update pull requests for NuGet packages and GitHub Actions.

Runs the unit tests, the generated-YAML checks (every strategy, artifact type, deployment kind and target), the wizard component tests (bUnit) and an in-memory smoke test of the web app. CI runs the same on every pull request and push to `main` (`.github/workflows/ci.yml`) and publishes a coverage report (job summary and the `coverage-report` artifact).

## Features

- **Variable groups** at pipeline or environment scope, and **Azure Key Vault** secrets loaded in each deploy job
- **Artifacts**: pipeline or build artifact, zip, Docker image, NuGet package
- **Deployments**: IIS, Windows service, file share, Azure App Service, Docker container, or a custom script
- **Strategies**: standard, rolling (native, on registered servers), slot swap, plus blue-green and canary placeholders
- **Backups and automatic rollback** on failure, per environment
- **Health checks**: HTTP (per-environment URL), IIS app pool, Windows service, port, custom PowerShell
- **Notifications**: Teams, custom webhook, email placeholder, on success and/or failure
- **Infrastructure as code**: Terraform, Bicep, ARM, PowerShell, per environment
- **Governance validation**: approvals reminder, secrets scanning, health check and rollback requirements, naming, forbidden and required tasks
- **Repository scan and templates**, **task explanations**, and an **agent diagnostics** script

## Solution structure

```
src/SimlifiezYaml.Core/          Models, services and stage generators (no UI)
src/SimlifiezYaml.Web/           Blazor Server wizard (one component per step)
tests/SimlifiezYaml.Core.Tests/  Unit, generated-YAML, bUnit and web smoke tests
```

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

Deployments to `prod`/`production` only run from the `main` branch.
