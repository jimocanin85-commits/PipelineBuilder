using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PipelineBuilder.Core.Abstractions;
using PipelineBuilder.Core.Deployment;
using PipelineBuilder.Core.Generators;
using PipelineBuilder.Core.Services;

namespace PipelineBuilder.Core.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPipelineBuilderCore(this IServiceCollection services, Action<PipelineBuilderOptions>? configure = null)
    {
        var options = new PipelineBuilderOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        // Fall back to a no-op logger when the host has not configured logging (e.g. unit tests).
        // Hosts that call AddLogging() first keep their own ILogger<T> registration.
        services.TryAdd(ServiceDescriptor.Singleton(typeof(ILogger<>), typeof(NullLogger<>)));

        // One handler per deployment kind. A handler registered after these replaces the built-in one for its kind.
        foreach (var handler in DeploymentKindRegistry.BuiltInHandlers())
            services.AddSingleton(typeof(IDeploymentKindHandler), handler);
        services.AddSingleton<IDeploymentKinds, DeploymentKindRegistry>();

        services.AddSingleton<IVariableGroupService, VariableGroupService>();
        services.AddSingleton<IKeyVaultYamlService, KeyVaultYamlService>();
        services.AddSingleton<IArtifactYamlService, ArtifactYamlService>();
        services.AddSingleton<IHealthCheckYamlService, HealthCheckYamlService>();
        services.AddSingleton<INotificationYamlService, NotificationYamlService>();
        services.AddSingleton<IIacYamlService, IacYamlService>();
        services.AddSingleton<IDeploymentStrategyService, DeploymentStrategyService>();
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
