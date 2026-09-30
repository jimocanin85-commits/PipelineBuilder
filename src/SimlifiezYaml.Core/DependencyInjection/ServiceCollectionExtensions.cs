using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SimlifiezYaml.Core.Abstractions;
using SimlifiezYaml.Core.Generators;
using SimlifiezYaml.Core.Services;

namespace SimlifiezYaml.Core.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSimlifiezYamlCore(this IServiceCollection services)
    {
        // Fall back to a no-op logger when the host has not configured logging (e.g. unit tests).
        // Hosts that call AddLogging() first keep their own ILogger<T> registration.
        services.TryAdd(ServiceDescriptor.Singleton(typeof(ILogger<>), typeof(NullLogger<>)));

        services.AddSingleton<IVariableGroupService, VariableGroupService>();
        services.AddSingleton<IKeyVaultYamlService, KeyVaultYamlService>();
        services.AddSingleton<IArtifactYamlService, ArtifactYamlService>();
        services.AddSingleton<IRollbackYamlService, RollbackYamlService>();
        services.AddSingleton<IHealthCheckYamlService, HealthCheckYamlService>();
        services.AddSingleton<INotificationYamlService, NotificationYamlService>();
        services.AddSingleton<IIacYamlService, IacYamlService>();
        services.AddSingleton<IDeploymentStrategyService, DeploymentStrategyService>();
        services.AddSingleton<IDeploymentStepService, DeploymentStepService>();
        services.AddSingleton<IGovernanceValidationService, GovernanceValidationService>();
        services.AddSingleton<IRepoScannerService, RepoScannerService>();
        services.AddSingleton<IYamlExplanationService, YamlExplanationService>();
        services.AddSingleton<IAgentDiagnosticsService, AgentDiagnosticsService>();
        services.AddSingleton<ISecretsGovernanceService, SecretsGovernanceService>();
        services.AddSingleton<IEnvironmentYamlService, EnvironmentYamlService>();
        services.AddSingleton<ITemplateMarketplaceService, TemplateMarketplaceService>();
        services.AddSingleton<IPipelineGeneratorService, PipelineGeneratorService>();

        services.AddSingleton<BuildStageGenerator>();
        services.AddSingleton<DeploymentStageGenerator>();
        services.AddSingleton<NotificationStepGenerator>();
        services.AddSingleton<GovernanceValidator>();

        return services;
    }
}
