using System.Text.RegularExpressions;
using SimlifiezYaml.Core.Abstractions;
using SimlifiezYaml.Core.Enums;
using SimlifiezYaml.Core.Generators;
using SimlifiezYaml.Core.Models;

namespace SimlifiezYaml.Core.Services;

public sealed class GovernanceValidationService : IGovernanceValidationService
{
    private readonly IVariableGroupService _variableGroupService;

    public GovernanceValidationService(IVariableGroupService variableGroupService)
    {
        _variableGroupService = variableGroupService ?? throw new ArgumentNullException(nameof(variableGroupService));
    }

    public IReadOnlyList<ValidationResult> Validate(PipelineDefinition definition, string yaml)
    {
        var results = new List<ValidationResult>();
        var governance = definition.Governance;

        if (governance.RequiredApprovals)
        {
            // Approvals live on the Azure DevOps environment, so the YAML cannot prove they exist.
            // Remind the user for every environment that should be gated.
            foreach (var env in definition.Environments.Where(e => e.Contains("prod", StringComparison.OrdinalIgnoreCase)))
            {
                results.Add(new ValidationResult
                {
                    Severity = ValidationSeverity.Info,
                    Message = $"Add an approval check to the '{env}' environment in Azure DevOps (Pipelines > Environments > {env} > Approvals and checks).",
                    AffectedField = "Environments",
                    SuggestedFix = "Approvals are configured on the environment, not in YAML."
                });
            }
        }

        results.AddRange(ValidateDeployment(definition));

        if (governance.RequireHealthCheck && definition.Environments.Any(EnvironmentNames.IsProduction))
        {
            if (!definition.HealthChecks.Any(h => h.Enabled))
            {
                results.Add(new ValidationResult
                {
                    Severity = ValidationSeverity.Error,
                    Message = "Production deployments require at least one enabled health check.",
                    AffectedField = nameof(PipelineDefinition.HealthChecks),
                    SuggestedFix = "Enable an HTTP, IIS, or service health check before prod deployment."
                });
            }
        }

        if (governance.RequireRollback && definition.DeploymentTarget == DeploymentTarget.OnPrem)
        {
            if (!definition.Rollback.Enabled)
            {
                results.Add(new ValidationResult
                {
                    Severity = ValidationSeverity.Error,
                    Message = "On-premises deployments require rollback configuration.",
                    AffectedField = nameof(PipelineDefinition.Rollback),
                    SuggestedFix = "Enable rollback and specify a backup path or rollback script."
                });
            }
        }

        if (!string.IsNullOrWhiteSpace(governance.NamingConvention))
        {
            try
            {
                if (!Regex.IsMatch(definition.Name, governance.NamingConvention))
                {
                    results.Add(new ValidationResult
                    {
                        Severity = ValidationSeverity.Warning,
                        Message = $"Pipeline name '{definition.Name}' does not match naming convention.",
                        AffectedField = nameof(PipelineDefinition.Name),
                        SuggestedFix = $"Rename pipeline to match pattern: {governance.NamingConvention}"
                    });
                }
            }
            catch (RegexParseException ex)
            {
                results.Add(new ValidationResult
                {
                    Severity = ValidationSeverity.Error,
                    Message = $"Governance naming convention regex is invalid: {ex.Message}",
                    AffectedField = nameof(GovernancePolicyConfig.NamingConvention),
                    SuggestedFix = "Provide a valid regular expression for pipeline naming."
                });
            }
            catch (Exception ex)
            {
                results.Add(new ValidationResult
                {
                    Severity = ValidationSeverity.Error,
                    Message = $"Error validating naming convention: {ex.GetType().Name}: {ex.Message}",
                    AffectedField = nameof(GovernancePolicyConfig.NamingConvention),
                    SuggestedFix = "Review naming convention configuration."
                });
            }
        }

        foreach (var forbidden in governance.ForbiddenTasks)
        {
            if (yaml.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(new ValidationResult
                {
                    Severity = ValidationSeverity.Error,
                    Message = $"Forbidden task '{forbidden}' detected in generated YAML.",
                    AffectedField = "Yaml",
                    SuggestedFix = $"Remove or replace task '{forbidden}' per governance policy."
                });
            }
        }

        foreach (var required in governance.RequiredTasks)
        {
            if (!yaml.Contains(required, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(new ValidationResult
                {
                    Severity = ValidationSeverity.Warning,
                    Message = $"Required task '{required}' is missing from generated YAML.",
                    AffectedField = "Yaml",
                    SuggestedFix = $"Add task '{required}' to the appropriate stage."
                });
            }
        }

        results.AddRange(_variableGroupService.Validate(definition.VariableGroups, governance));
        return results;
    }

    private static IEnumerable<ValidationResult> ValidateDeployment(PipelineDefinition definition)
    {
        var deployment = definition.Deployment;
        var strategy = definition.DeploymentStrategy.StrategyType;
        var onServers = DeploymentStageGenerator.UsesServerResources(definition);

        if (deployment.Kind == DeploymentKind.Custom && string.IsNullOrWhiteSpace(deployment.CustomScript))
        {
            yield return Result(ValidationSeverity.Warning,
                "No deployment kind is selected, so the deploy step is only a placeholder.",
                nameof(PipelineDefinition.Deployment),
                "Choose IIS, Windows service, file share, App Service or Docker, or provide a custom deploy script.");
        }

        if (deployment.IsServerDeployment && definition.DeploymentTarget == DeploymentTarget.Cloud)
        {
            yield return Result(ValidationSeverity.Warning,
                $"{deployment.Kind} deployments need your own servers, but the deployment target is Cloud, so they would run on a hosted build agent.",
                nameof(PipelineDefinition.DeploymentTarget),
                "Set the deployment target to OnPrem or Hybrid and register the servers in each Azure DevOps environment.");
        }

        if (onServers)
        {
            yield return Result(ValidationSeverity.Info,
                "Deployments run on the servers registered in each Azure DevOps environment (Virtual machine resources).",
                nameof(PipelineDefinition.Environments),
                "Register the target servers under Pipelines > Environments > <environment> > Add resource > Virtual machines.");
        }

        if (strategy == DeploymentStrategyType.Rolling && !onServers)
        {
            yield return Result(ValidationSeverity.Warning,
                "Rolling deployments need servers registered in the environment; this pipeline deploys everything at once.",
                nameof(PipelineDefinition.DeploymentStrategy),
                "Use an on-premises deployment target, or choose the Standard strategy.");
        }

        if (strategy is DeploymentStrategyType.Canary or DeploymentStrategyType.BlueGreen)
        {
            yield return Result(ValidationSeverity.Warning,
                $"{strategy} traffic routing depends on your load balancer and is generated as a placeholder step.",
                nameof(PipelineDefinition.DeploymentStrategy),
                "Replace the placeholder step with your traffic-switch commands.");
        }

        if (deployment.Kind == DeploymentKind.Kubernetes && definition.Artifact.ArtifactType != ArtifactType.DockerImage)
        {
            yield return Result(ValidationSeverity.Warning,
                "Kubernetes deployments need a container image, but the artifact is not a Docker image.",
                nameof(PipelineDefinition.Artifact),
                "Set the artifact type to DockerImage.");
        }

        foreach (var email in definition.Notifications.Where(n => n.NotificationType == NotificationType.Email
                                                                  && !n.EmailRecipients.Any(r => !string.IsNullOrWhiteSpace(r))))
        {
            yield return Result(ValidationSeverity.Warning,
                "An email notification has no recipients, so it will be skipped.",
                nameof(PipelineDefinition.Notifications),
                "Add at least one recipient on the Notifications step.");
        }

        if (strategy == DeploymentStrategyType.SlotSwap && deployment.Kind != DeploymentKind.AzureAppService)
        {
            yield return Result(ValidationSeverity.Error,
                "The slot-swap strategy only works with Azure App Service deployments.",
                nameof(PipelineDefinition.DeploymentStrategy),
                "Set the deployment kind to Azure App Service, or choose another strategy.");
        }

        if (definition.Rollback.Enabled && deployment.Kind == DeploymentKind.AzureAppService)
        {
            yield return Result(ValidationSeverity.Info,
                "App Service rollback is not automatic: the failure hook prints the command to swap the slots back.",
                nameof(PipelineDefinition.Rollback),
                "Use the slot-swap strategy so production only changes after the new version is deployed.");
        }
    }

    private static ValidationResult Result(ValidationSeverity severity, string message, string field, string fix) => new()
    {
        Severity = severity,
        Message = message,
        AffectedField = field,
        SuggestedFix = fix
    };
}
