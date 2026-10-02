using Microsoft.Extensions.Logging;
using PipelineBuilder.Core.Abstractions;
using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Generators;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Yaml;

namespace PipelineBuilder.Core.Services;

/// <summary>
/// Orchestrates the generation of Azure DevOps pipeline YAML from pipeline definitions.
/// Coordinates multiple specialized services and generators to produce complete, validated pipelines.
/// </summary>
public sealed class PipelineGeneratorService : IPipelineGeneratorService
{
    private readonly IVariableGroupService _variableGroupService;
    private readonly BuildStageGenerator _buildGenerator;
    private readonly DeploymentStageGenerator _deploymentGenerator;
    private readonly NotificationStepGenerator _notificationGenerator;
    private readonly IPipelineValidator _validator;
    private readonly IYamlExplanationService _explanationService;
    private readonly IAgentDiagnosticsService _agentDiagnosticsService;
    private readonly ILogger<PipelineGeneratorService> _logger;

    public PipelineGeneratorService(
        IVariableGroupService variableGroupService,
        BuildStageGenerator buildGenerator,
        DeploymentStageGenerator deploymentGenerator,
        NotificationStepGenerator notificationGenerator,
        IPipelineValidator validator,
        IYamlExplanationService explanationService,
        IAgentDiagnosticsService agentDiagnosticsService,
        ILogger<PipelineGeneratorService> logger)
    {
        _variableGroupService = variableGroupService;
        _buildGenerator = buildGenerator;
        _deploymentGenerator = deploymentGenerator;
        _notificationGenerator = notificationGenerator;
        _validator = validator;
        _explanationService = explanationService;
        _agentDiagnosticsService = agentDiagnosticsService;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Generates the pipeline. Failures are not logged here: they surface as exceptions and the host
    /// logs them once, at its boundary.
    /// </summary>
    /// <exception cref="ArgumentException">The settings have blocking problems.</exception>
    /// <exception cref="InvalidOperationException">The generated YAML is invalid, which is a bug.</exception>
    public GeneratedPipeline Generate(PipelineDefinition definition)
    {
        ThrowIfInvalid(definition);
        _logger.LogInformation("Starting pipeline generation for: {PipelineName}", definition.Name);

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
                "PipelineBuilder generated an invalid pipeline. This is a bug; please report it with your settings file.\n  - " +
                string.Join("\n  - ", yamlProblems));

        // Governance, deployment advice, secrets and Key Vault findings about the generated pipeline.
        var validation = _validator.ValidateGenerated(definition, yaml);

        string? diagnosticScript = null;
        if (definition.AgentDiagnostics != null)
            diagnosticScript = _agentDiagnosticsService.GenerateDiagnosticScript(definition.AgentDiagnostics);

        var errorCount = validation.Count(v => v.Severity == ValidationSeverity.Error);
        var warningCount = validation.Count(v => v.Severity == ValidationSeverity.Warning);
        _logger.LogInformation(
            "Pipeline generation completed for {PipelineName}: {YamlLength} chars, {ErrorCount} errors, {WarningCount} warnings",
            definition.Name, yaml.Length, errorCount, warningCount);

        return new GeneratedPipeline
        {
            Yaml = yaml,
            Explanations = _explanationService.ExplainYaml(yaml),
            ValidationResults = validation,
            DiagnosticScript = diagnosticScript
        };
    }

    /// <exception cref="ArgumentException">Thrown if the settings have blocking problems.</exception>
    private void ThrowIfInvalid(PipelineDefinition definition)
    {
        var errors = _validator.ValidateInput(definition);
        if (errors.Count > 0)
        {
            throw new ArgumentException(
                $"Pipeline definition validation failed with {errors.Count} error(s):\n" +
                string.Join("\n", errors.Select(e => $"  - {e.Message}")),
                nameof(definition));
        }
    }
}
