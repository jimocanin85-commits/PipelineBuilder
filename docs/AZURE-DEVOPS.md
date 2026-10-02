# What to set up in Azure DevOps

The wizard's **Validation** step lists what your pipeline needs. This page is the full reference.

## Always

- A project and a Git repository. Commit the file as `azure-pipelines.yml` and create a pipeline from it.
- An **environment** for each name in the wizard (e.g. `test`, `preprod`, `prod`). Add approvals to production here; approvals are not set in YAML.

## Depending on your choices

| You chose | You need |
|---|---|
| IIS, Windows service, file share or Docker on your own servers | The servers registered as **Virtual machine resources** in each environment |
| App Service, Key Vault, Bicep, ARM or Terraform | An **Azure Resource Manager service connection** |
| Docker image | A **Docker registry service connection** |
| Kubernetes | A **Kubernetes service connection**, and your manifests in the repository |
| Terraform | The Terraform extension from the Marketplace, and a storage account for the state |
| Self-hosted build agent | The agent pool |

## Variables

A field you leave empty in the wizard becomes a pipeline variable. Define the ones your pipeline uses in a variable group, and mark passwords and webhook URLs as secret.

| Variable | Used for |
|---|---|
| `DEPLOY_PATH`, `SERVICE_NAME` | Windows service and file share |
| `WEBAPP_NAME`, `RESOURCE_GROUP` | App Service, slot swap, Bicep, ARM |
| `CONTAINER_NAME`, `DOCKER_REGISTRY` | Docker |
| `K8S_SERVICE_CONNECTION`, `K8S_NAMESPACE`, `K8S_DEPLOYMENT` | Kubernetes |
| `AZURE_SERVICE_CONNECTION`, `DOCKER_SERVICE_CONNECTION` | Service connection names |
| `TF_STATE_RG`, `TF_STATE_STORAGE`, `TF_STATE_CONTAINER` | Terraform state |
| `AZURE_SUBSCRIPTION_ID`, `AZURE_LOCATION` | ARM |
| `NUGET_FEED` | NuGet packages |
| `TEAMS_WEBHOOK_URL`, `CUSTOM_WEBHOOK_URL` | Notifications |
| `SMTP_HOST`, `SMTP_PORT`, `SMTP_USERNAME`, `SMTP_PASSWORD`, `EMAIL_FROM` | Email |

## How the pipeline runs

**Build → one deploy stage per environment → notify.**

Each deploy stage backs up the current version, deploys, runs the health checks, and rolls back if a step fails. Production only deploys from the release branch (`main` unless you change it).
