using Microsoft.Extensions.Logging;
using SimlifiezYaml.Core.Abstractions;
using SimlifiezYaml.Core.Enums;
using SimlifiezYaml.Core.Generators;
using SimlifiezYaml.Core.Models;
using SimlifiezYaml.Core.Yaml;

namespace SimlifiezYaml.Core.Services;

/// <summary>
/// Orchestrates the generation of Azure DevOps pipeline YAML from pipeline definitions.
/// Coordinates multiple specialized services and generators to produce complete, validated pipelines.
/// </summary>
public sealed class PipelineGeneratorService : IPipelineGeneratorService
{
    private readonly IVariableGroupService _variableGroupService;
    private readonly IKeyVaultYamlService _keyVaultService;
    private readonly BuildStageGenerator _buildGenerator;
    private readonly DeploymentStageGenerator _deploymentGenerator;
    private readonly NotificationStepGenerator _notificationGenerator;
    private readonly GovernanceValidator _governanceValidator;
    private readonly IYamlExplanationService _explanationService;
    private readonly IAgentDiagnosticsService _agentDiagnosticsService;
    private readonly ILogger<PipelineGeneratorService> _logger;

    public PipelineGeneratorService(
        IVariableGroupService variableGroupService,
        IKeyVaultYamlService keyVaultService,
        BuildStageGenerator buildGenerator,
        DeploymentStageGenerator deploymentGenerator,
        NotificationStepGenerator notificationGenerator,
        GovernanceValidator governanceValidator,
        IYamlExplanationService explanationService,
        IAgentDiagnosticsService agentDiagnosticsService,
        ILogger<PipelineGeneratorService> logger)
    {
        _variableGroupService = variableGroupService;
        _keyVaultService = keyVaultService;
        _buildGenerator = buildGenerator;
        _deploymentGenerator = deploymentGenerator;
        _notificationGenerator = notificationGenerator;
        _governanceValidator = governanceValidator;
        _explanationService = explanationService;
        _agentDiagnosticsService = agentDiagnosticsService;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public GeneratedPipeline Generate(PipelineDefinition definition)
    {
        // Validate input before processing
        try
        {
            PipelineDefinitionValidator.ValidateOrThrow(definition);
            _logger.LogInformation("Starting pipeline generation for: {PipelineName}", definition.Name);
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "Pipeline validation failed for: {PipelineName}", definition.Name);
            throw;
        }

        try
        {
            // Build YAML using fluent assembler pattern
            var assembler = new PipelineYamlAssembler()
                .AddHeader(definition.Name)
                .AddTrigger(definition.Trigger)
                .AddVariables(_variableGroupService.GeneratePipelineVariables(definition))
                .AddPool(PoolConfigurationHelper.GeneratePoolConfiguration(definition.BuildAgent, definition.PoolName))
                .StartStages()
                .AddStage(_buildGenerator.Generate(definition))
                .AddStage(_deploymentGenerator.Generate(definition))
                .AddNotificationStages(definition.Notifications, _notificationGenerator, definition);

            var yaml = assembler.Build();

            // Safety net: never hand out YAML that Azure DevOps would reject.
            var yamlProblems = GeneratedYamlValidator.Validate(yaml);
            if (yamlProblems.Count > 0)
                throw new InvalidOperationException(
                    "SimlifiezYaml generated an invalid pipeline. This is a bug; please report it with your settings file.\n  - " +
                    string.Join("\n  - ", yamlProblems));
            
            // Validate the generated YAML
            var validation = _governanceValidator.ValidateAll(definition, yaml);
            validation = validation.Concat(_keyVaultService.Validate(definition.KeyVault)).ToList();

            string? diagnosticScript = null;
            if (definition.AgentDiagnostics != null)
                diagnosticScript = _agentDiagnosticsService.GenerateDiagnosticScript(definition.AgentDiagnostics);

            var errorCount = validation.Count(v => v.Severity == ValidationSeverity.Error);
            var warningCount = validation.Count(v => v.Severity == ValidationSeverity.Warning);
            
            _logger.LogInformation(
                "Pipeline generation completed for {PipelineName}: {YamlLength} chars, {ErrorCount} errors, {WarningCount} warnings",
                definition.Name, yaml.Length, errorCount, warningCount);

            if (errorCount > 0)
                _logger.LogWarning("Pipeline {PipelineName} has {ErrorCount} validation errors", definition.Name, errorCount);

            return new GeneratedPipeline
            {
                Yaml = yaml,
                Explanations = _explanationService.ExplainYaml(yaml),
                ValidationResults = validation,
                DiagnosticScript = diagnosticScript
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during pipeline generation for {PipelineName}", definition.Name);
            throw;
        }
    }
}
