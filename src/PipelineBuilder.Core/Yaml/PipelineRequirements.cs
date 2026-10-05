using System.Text.RegularExpressions;
using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;

namespace PipelineBuilder.Core.Yaml;

/// <summary>
/// Lists what a generated pipeline needs in Azure DevOps: its environments, service connections,
/// variable groups and every pipeline variable the YAML refers to. Each entry says what it is for
/// and where in Azure DevOps it is created.
/// </summary>
public static class PipelineRequirements
{
    // $(NAME) without a dot: predefined variables such as $(Build.BuildId) always contain one.
    private static readonly Regex Macro = new(@"\$\(([A-Za-z_][A-Za-z0-9_]*)\)", RegexOptions.Compiled);
    private static readonly Regex SecretName = new("PASSWORD|SECRET|TOKEN|WEBHOOK|APIKEY|API_KEY", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private const string VariablesPath = "Pipelines → your pipeline → Edit → Variables";

    /// <summary>Variables the pipeline defines itself.</summary>
    private static readonly HashSet<string> DefinedInYaml = new(StringComparer.OrdinalIgnoreCase) { "BuildConfiguration", ServerScript.ServersVariable };

    private static readonly Dictionary<string, string> Purposes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DEPLOY_PATH"] = "Folder the app is installed in",
        ["SERVICE_NAME"] = "Name of the service",
        ["CONTAINER_NAME"] = "Name of the container",
        ["DOCKER_REGISTRY"] = "Registry host, e.g. myregistry.azurecr.io",
        ["DOCKER_SERVICE_CONNECTION"] = "Name of the Docker registry service connection",
        ["K8S_SERVICE_CONNECTION"] = "Name of the Kubernetes service connection",
        ["K8S_NAMESPACE"] = "Kubernetes namespace",
        ["K8S_DEPLOYMENT"] = "Kubernetes deployment to roll back",
        ["AZURE_SERVICE_CONNECTION"] = "Name of the Azure service connection",
        ["SSH_USER"] = "Account the build agent logs in as on the servers",
        ["TEAMS_WEBHOOK_URL"] = "Teams webhook URL",
        ["CUSTOM_WEBHOOK_URL"] = "Webhook URL",
        ["SMTP_HOST"] = "Mail server",
        ["SMTP_PORT"] = "Mail server port (587 if not set)",
        ["SMTP_USERNAME"] = "Mail login, if the server needs one",
        ["SMTP_PASSWORD"] = "Mail password",
        ["EMAIL_FROM"] = "Sender address"
    };

    public static IReadOnlyList<PipelineRequirement> Find(PipelineDefinition definition, string yaml)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var onServers = yaml.Contains("resourceType: VirtualMachine", StringComparison.Ordinal);
        var needs = new List<PipelineRequirement>();

        foreach (var environment in definition.Environments.Where(e => !string.IsNullOrWhiteSpace(e)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            needs.Add(new PipelineRequirement
            {
                Kind = RequirementKind.Environment,
                Name = environment,
                Purpose = (onServers ? "Environment with your servers registered" : "Environment")
                          + (EnvironmentNames.IsProduction(environment) ? ", with an approval (Approvals and checks)" : string.Empty),
                Where = onServers
                    ? "Pipelines → Environments → New environment → Virtual machines"
                    : "Pipelines → Environments → New environment"
            });
        }

        if (definition.Artifact.ArtifactType == ArtifactType.DockerImage)
            AddConnection(needs, definition.Artifact.ContainerRegistryConnection, "Docker registry service connection");
        if (definition.Deployment.Kind == DeploymentKind.Kubernetes)
            AddConnection(needs, definition.Deployment.KubernetesServiceConnection, "Kubernetes service connection");
        if (definition.KeyVault != null)
            AddConnection(needs, definition.KeyVault.ServiceConnection, "Azure service connection for Key Vault");

        foreach (var group in definition.VariableGroups.Where(g => !string.IsNullOrWhiteSpace(g.Name)))
        {
            needs.Add(new PipelineRequirement
            {
                Kind = RequirementKind.VariableGroup,
                Name = group.Name,
                Purpose = "Variable group",
                Where = "Pipelines → Library → Variable group",
                IsSecret = group.ContainsSecrets
            });
        }

        if (definition.Deployment.Kind == DeploymentKind.Ansible && !string.IsNullOrWhiteSpace(definition.Deployment.AnsibleSshKeyFile))
        {
            needs.Add(new PipelineRequirement
            {
                Kind = RequirementKind.SecureFile,
                Name = definition.Deployment.AnsibleSshKeyFile.Trim(),
                Purpose = "Private SSH key Ansible logs in to the servers with",
                Where = "Pipelines → Library → Secure files",
                IsSecret = true
            });
        }

        // A build agent that deploys over the network reads each environment's servers from a variable.
        if (yaml.Contains($"$({ServerScript.ServersVariable})", StringComparison.Ordinal))
        {
            foreach (var environment in definition.Environments.Where(e => !string.IsNullOrWhiteSpace(e)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                needs.Add(new PipelineRequirement
                {
                    Kind = RequirementKind.Variable,
                    Name = ServerScript.ServersVariablePrefix + environment.ToUpperInvariant().Replace('-', '_'),
                    Purpose = $"Servers in {environment}, comma-separated, e.g. web01, web02",
                    Where = VariablesPath
                });
            }
        }

        var variables = Macro.Matches(yaml).Select(m => m.Groups[1].Value)
            .Where(name => !DefinedInYaml.Contains(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);
        foreach (var name in variables)
        {
            needs.Add(new PipelineRequirement
            {
                Kind = RequirementKind.Variable,
                Name = name,
                Purpose = Purposes.GetValueOrDefault(name, "Variable used by your settings"),
                Where = VariablesPath,
                IsSecret = SecretName.IsMatch(name)
            });
        }

        return needs;
    }

    /// <summary>A connection entered by name is something to create; one left as <c>$(VARIABLE)</c> shows up as a variable.</summary>
    private static void AddConnection(List<PipelineRequirement> needs, string? name, string purpose)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains("$(", StringComparison.Ordinal))
            return;
        needs.Add(new PipelineRequirement
        {
            Kind = RequirementKind.ServiceConnection,
            Name = name.Trim(),
            Purpose = purpose,
            Where = "Project settings → Service connections → New service connection"
        });
    }
}
