using PipelineBuilder.Core.Abstractions;
using PipelineBuilder.Core.Models;

namespace PipelineBuilder.Core.Generators;

public sealed class GovernanceValidator
{
    private readonly IGovernanceValidationService _governanceService;
    private readonly ISecretsGovernanceService _secretsService;

    public GovernanceValidator(
        IGovernanceValidationService governanceService,
        ISecretsGovernanceService secretsService)
    {
        _governanceService = governanceService;
        _secretsService = secretsService;
    }

    public IReadOnlyList<ValidationResult> ValidateAll(PipelineDefinition definition, string yaml)
    {
        var results = new List<ValidationResult>();
        results.AddRange(_governanceService.Validate(definition, yaml));
        var secrets = _secretsService.ScanYaml(yaml);
        results.AddRange(_secretsService.ToValidationResults(secrets));
        return results;
    }
}
