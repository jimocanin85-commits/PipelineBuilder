# PipelineBuilder

A wizard that writes your `azure-pipelines.yml` for Azure DevOps. The pipeline builds the app once, deploys the same package to test, preprod and prod, and rolls back if a deployment fails.

## Run it

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (any 10.0 version).

```bash
git clone https://github.com/jimocanin85-commits/PipelineBuilder.git
cd PipelineBuilder
dotnet run --project src/PipelineBuilder.Web
```

Open <http://localhost:5150>.

## Use it

1. **What**: pick what you deploy.
2. **Where**: your servers or cluster, and the settings each environment gets. Tick a box if you have no preprod.
3. **Safety**: approvals, rollback, health checks, notifications.
4. **Result**: download `azure-pipelines.yml`, and tick off what you create in Azure DevOps.

Above every step the pipeline is drawn the way Azure DevOps draws a run: a box per stage, with the approvals between them. Beside the step is a list of what the pipeline needs in Azure DevOps. Switch it to **YAML file** to see `azure-pipelines.yml` change as you fill in the form.

What you fill in is kept in your browser until you press **Start over**. To keep it elsewhere, save your settings on the Result step and open them later. With a team database (SQL Server, set up by the install script), **Save for the team** puts it in a list everyone opens on the first step, and every save, download and copy is logged.

## What it generates

| | |
|---|---|
| Build | .NET, Node.js |
| Deploy to | Windows servers (IIS, Windows service, file share), Linux servers (systemd service), Docker on either, Kubernetes, your own Ansible playbook, or your own script |
| Run by | An agent on each server, or one build agent over the network (WinRM to Windows, SSH to Linux) |
| Update | All servers at once, or a few at a time |
| Safety | Approval between environments, backup and rollback, health checks, a check for passwords in the file |
| Extras | Key Vault, notifications by Teams, email or webhook |

## More

- [Install it on a server for the team](docs/INSTALL.md): IIS, with Windows login
- [What to set up in Azure DevOps](docs/AZURE-DEVOPS.md)
- [How the code is organized](docs/ARCHITECTURE.md)
- Run the tests: `dotnet test`
