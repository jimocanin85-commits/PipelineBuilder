# What to set up in Azure DevOps

The wizard lists what your pipeline needs under "Needs in Azure DevOps". On the Result step you can tick each one off as you create it. This page explains the list.

## How the pipeline runs

**Build → one deploy stage per environment → notify.**

The app is built once, with its tests. The test results and the code coverage are shown on the run's **Tests** and **Code Coverage** tabs. NuGet and npm packages are kept between runs, so they are not downloaded every time. Every environment gets that same package or image. Each deploy stage backs up the current version, deploys, runs the health checks, and rolls back if a step fails.

## Always

- A project and a Git repository. Commit the file as `azure-pipelines.yml` and create a pipeline from it.
- An **environment** for each step of the flow: `test`, `preprod` and `prod`, or `test` and `prod` if you have no preprod.

## Depending on your choices

| You chose | You need |
|---|---|
| Anything on your own servers (Windows or Linux) | The servers registered as **Virtual machine resources** in each environment |
| A Linux service | The agent's user may run `systemctl` with `sudo` without a password, and owns the install folder |
| Key Vault | An **Azure Resource Manager service connection** that may read secrets in the vault. Name the secrets in the wizard, so the pipeline gets only those |
| Docker | A **Docker registry service connection**; the servers use it to log in and pull the image |
| Kubernetes | A **Kubernetes service connection**, and your manifests in the repository |
| Ansible | A Linux agent with Ansible installed that can reach your servers; your playbook and inventory in the repository. Optionally the SSH key as a **secure file** |
| Self-hosted build agent | The agent pool |
| Each environment its own settings | A **variable group** per environment, e.g. `vg-orders-api-prod`, with a variable for each setting to replace, named like the setting: `ConnectionStrings.Default` replaces `ConnectionStrings:Default` in `appsettings.json`. The wizard adds the groups for you |

## Two kinds of approval

**On the environment.** Set in Azure DevOps under Pipelines → Environments → the environment → Approvals and checks. It is not in the file, so nobody can remove it by changing the file.

**In the pipeline file.** On the Safety step, tick "Wait for approval between test and preprod" (or any two environments). The pipeline stops there, sends an email and continues when someone presses Resume. You choose who may approve, who gets the email and how long it waits. Nothing has to be set up in Azure DevOps. Naming who may approve needs a recent Azure DevOps. With no names, anyone who can start the pipeline can approve.

## Two ways to reach your servers

On the Where step you choose who runs the deployment.

**An agent on each server.** Every server has its own Azure DevOps agent and is registered in the environment as a Virtual machine resource. Windows servers run the steps in Windows PowerShell, Linux servers in bash. Nothing has to be opened between the servers.

**The build agent, over the network.** One self-hosted agent deploys to all servers, one at a time. You need:

- your own agent pool (Microsoft's agents cannot reach servers inside your network);
- a variable per environment that names its servers: `SERVERS_TEST`, `SERVERS_PREPROD`, `SERVERS_PROD`, e.g. `web01, web02`;
- for Windows servers: a Windows agent, PowerShell remoting (WinRM) enabled on the servers, and the agent's account in their Administrators group;
- for Linux servers: `ssh` and `scp` on the agent (built into Windows Server 2019 and later), the agent account's public SSH key on the servers, and the `SSH_USER` variable;
- for Linux servers, also: log in once from the agent to each server with `ssh`, so the agent knows the server. A server the agent does not know is refused;
- for Docker: each server logged in to your registry once with `docker login`.

## What the pipeline does to stay safe

- **A pull request is built and tested, but never deployed.** Its code has not been reviewed yet, so it does not reach a server or a stage that holds secrets.
- **Only the release branch is deployed to prod.** That is `main`, unless you change it in the wizard.
- **Secrets are never written in the file.** They are secret variables or come from Key Vault, and a script gets them through its environment.
- **Text from the run is never run as code.** The build number and the pipeline's name are read as plain text.
- **On your servers, the package lies in the login account's own folder**, not in a shared one such as `/tmp` or `C:\ProgramData`, where another account could change it before it is installed.
- **A variable that is not set stops the deployment.** Nothing is copied to, or deleted from, the wrong folder.

Three things only you can set, in Azure DevOps:

1. An **approval** on the prod environment (see "Two kinds of approval").
2. A **branch control** check on each environment, so only your protected branches can deploy to it.
3. Who may **start the pipeline by hand**. They can pick which environments a run deploys to. The approval on prod still applies.

## Variables

A field you leave empty in the wizard becomes a variable. The wizard lists each one your pipeline uses, with what it is for. Set them on the pipeline (Edit → Variables), or in a variable group you add in the wizard. Mark passwords and webhook addresses as secret.
