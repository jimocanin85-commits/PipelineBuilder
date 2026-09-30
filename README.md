# SimlifiezYaml

Enterprise-ready Azure DevOps Pipeline Builder for cloud, on-prem, and hybrid environments.

## Features

- **Variable groups** — pipeline, stage, and environment scope
- **Azure Key Vault** — `AzureKeyVault@2` pre-job integration
- **Environment approvals** — `environment:` references with UI guidance
- **Artifacts** — pipeline, build, zip, Docker, NuGet publish/download
- **Rollback** — IIS, Windows Service, file share, App Service slots, Docker
- **Health checks** — HTTP, IIS app pool, Windows service, port, custom PowerShell
- **Notifications** — Teams, email placeholder, custom webhook (via variables only)
- **IaC** — Terraform, Bicep, ARM, PowerShell
- **Deployment strategies** — standard, rolling, blue-green, canary, slot swap
- **Governance validation** — approvals, secrets, health checks, naming, forbidden tasks
- **Repo scanning** — detect stack and suggest templates
- **Pipeline explainability** — human-readable task descriptions
- **Agent diagnostics** — self-hosted agent PowerShell checks
- **Secrets governance** — plaintext secret detection
- **Template marketplace** — internal catalog by category

## Solution structure

```
src/SimlifiezYaml.Core/     Models, services, modular stage generators
src/SimlifiezYaml.Web/       Blazor Server 15-step wizard UI
tests/SimlifiezYaml.Core.Tests/
```

## Requirements

- .NET 10 SDK (10.0.401 or later; pinned in `global.json`)

## Run

```bash
dotnet build
dotnet test
dotnet run --project src/SimlifiezYaml.Web
```

Open `https://localhost:5xxx` and use the wizard to generate `azure-pipelines.yml`.

## Architecture

Modular generators (not a monolithic YAML builder):

- `BuildStageGenerator`, `TestStageGenerator`, `ArtifactStageGenerator`, `DeploymentStageGenerator`
- `RollbackStepGenerator`, `HealthCheckStepGenerator`, `NotificationStepGenerator`
- `GovernanceValidator`

Pipeline flow: **Build → Test → Artifact → Deploy_{env} (one stage per environment) → Notify_Success / Notify_Failure**

Each `Deploy_{env}` stage contains:

1. An optional `Infrastructure` job (Terraform/Bicep/ARM/PowerShell) for that environment, which runs first.
2. A `deployment` job targeting the Azure DevOps environment, so its approvals and checks apply. It:
   - downloads the artifact and loads Key Vault secrets
   - backs up the current version to `{BackupPath}\{env}\{BuildId}`, keeping the last N backups
   - deploys according to the deployment kind (IIS, Windows service, file share, App Service, Docker, or a custom script)
   - runs the health checks (`{environment}` in a URL is replaced per environment)
   - rolls back from that backup in its `on: failure` hook if any step fails

On-premises deployments run on the servers registered in each environment (Virtual machine resources), and the Rolling strategy uses Azure DevOps' native `rolling` strategy. Settings you leave empty become pipeline variables such as `$(DEPLOY_PATH)` or `$(WEBAPP_NAME)`, which you define in a variable group.
