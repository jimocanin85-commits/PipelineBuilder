# PipelineBuilder

A wizard that builds an `azure-pipelines.yml` for Azure DevOps: build, deploy to each environment, roll back on failure.

## Run it

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (10.0.401 or later).

```bash
git clone https://github.com/jimocanin85-commits/PipelineBuilder.git
cd PipelineBuilder
dotnet run --project src/PipelineBuilder.Web
```

Open <http://localhost:5150>.

## Use it

1. **What**: pick what you deploy.
2. **Where**: the environments, and the servers or cluster.
3. **Safety**: rollback, health checks, notifications.
4. **Result**: see what the pipeline needs in Azure DevOps, and download `azure-pipelines.yml`.

On the Result step you can also save your settings, and open them later to change the pipeline.

## What it generates

| | |
|---|---|
| Build | .NET, Node.js |
| Deploy to | Windows servers (IIS, Windows service, file share), Linux servers (systemd service), Docker on either, Kubernetes, or your own script |
| Strategy | Standard, rolling |
| Safety | Backup and rollback, health checks, secret scan |
| Extras | Key Vault, notifications by Teams, email or webhook |

## Install on a server

On a Windows server with IIS, as Administrator:

```powershell
dotnet publish src/PipelineBuilder.Web -c Release -o .\publish
.\deploy\Install-PipelineBuilder.ps1 -PublishFolder .\publish -InstallMissingFeatures
```

Users sign in with their Windows account. Details: [docs/INSTALL.md](docs/INSTALL.md).

## More

- [What to set up in Azure DevOps](docs/AZURE-DEVOPS.md), including the variables
- [Install guide](docs/INSTALL.md)
- [How the code is organised](docs/ARCHITECTURE.md)
- Run the tests: `dotnet test`
