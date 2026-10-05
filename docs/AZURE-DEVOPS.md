# What to set up in Azure DevOps

The wizard's **Result** step lists what your pipeline needs. This page is the full reference.

## Always

- A project and a Git repository. Commit the file as `azure-pipelines.yml` and create a pipeline from it.
- An **environment** for each step of the flow: `test`, `preprod` and `prod`, or `test` and `prod` if you have no preprod. Add approvals to production here; approvals are not set in YAML.

## Depending on your choices

| You chose | You need |
|---|---|
| Anything on your own servers (Windows or Linux) | The servers registered as **Virtual machine resources** in each environment |
| A Linux service | The agent's user may run `systemctl` with `sudo` without a password, and owns the install folder |
| Key Vault | An **Azure Resource Manager service connection** |
| Docker | A **Docker registry service connection**; the servers use it to log in and pull the image |
| Kubernetes | A **Kubernetes service connection**, and your manifests in the repository |
| Self-hosted build agent | The agent pool |

## Variables

A field you leave empty in the wizard becomes a pipeline variable. Define the ones your pipeline uses on the pipeline itself (Edit → Variables), or in a variable group you add in the wizard. Mark passwords and webhook URLs as secret.

| Variable | Used for |
|---|---|
| `DEPLOY_PATH`, `SERVICE_NAME` | Windows service, Linux service and file share |
| `CONTAINER_NAME`, `DOCKER_REGISTRY` | Docker |
| `K8S_SERVICE_CONNECTION`, `K8S_NAMESPACE`, `K8S_DEPLOYMENT` | Kubernetes |
| `AZURE_SERVICE_CONNECTION`, `DOCKER_SERVICE_CONNECTION` | Service connection names |
| `TEAMS_WEBHOOK_URL`, `CUSTOM_WEBHOOK_URL` | Notifications |
| `SMTP_HOST`, `SMTP_PORT`, `SMTP_USERNAME`, `SMTP_PASSWORD`, `EMAIL_FROM` | Email |

## How the pipeline runs

**Build → one deploy stage per environment → notify.**

Each deploy stage backs up the current version, deploys, runs the health checks, and rolls back if a step fails. Production only deploys from the release branch (`main` unless you change it).
