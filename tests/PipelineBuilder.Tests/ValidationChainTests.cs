using Microsoft.Extensions.DependencyInjection;
using PipelineBuilder.Core.Abstractions;
using PipelineBuilder.Core.DependencyInjection;
using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Validation;
using PipelineBuilder.Web.State;
using Xunit;

namespace PipelineBuilder.Tests;

/// <summary>The validation chain: one entry point, rules with ids (architecture goal M6).</summary>
public class ValidationChainTests
{
    private readonly PipelineValidator _validator = PipelineValidator.CreateDefault();

    [Fact]
    public void RuleIdsAreUniqueAndNamedByArea()
    {
        var ids = _validator.Rules.Select(r => r.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(ids, id => Assert.Matches("^[a-z]+\\.[a-z-]+$", id));
        Assert.Contains(_validator.Rules, r => r.Stage == ValidationStage.Input);
        Assert.Contains(_validator.Rules, r => r.Stage == ValidationStage.Generated);
    }

    [Fact]
    public void InputFindingsCarryTheirRuleId()
    {
        var definition = new PipelineDefinition { Name = "", Environments = Array.Empty<string>() };

        var findings = _validator.ValidateInput(definition);

        Assert.Contains(findings, f => f.RuleId == "input.name");
        Assert.Contains(findings, f => f.RuleId == "input.environments");
        Assert.All(findings, f => Assert.StartsWith("input.", f.RuleId));
    }

    [Fact]
    public void GeneratedFindingsCarryTheirRuleId()
    {
        var definition = WizardState.CreateDefault();
        definition.HealthChecks = Array.Empty<HealthCheckConfig>();
        definition.Governance.ForbiddenTasks = new[] { "DotNetCoreCLI@2" };
        var generator = new ServiceCollection().AddPipelineBuilderCore().BuildServiceProvider().GetRequiredService<IPipelineGeneratorService>();

        var findings = generator.Generate(definition).ValidationResults;

        Assert.Contains(findings, f => f.RuleId == "governance.health-check");
        Assert.Contains(findings, f => f.RuleId == "governance.forbidden-tasks");
        Assert.Contains(findings, f => f.RuleId == "governance.approvals");
        Assert.All(findings, f => Assert.False(string.IsNullOrEmpty(f.RuleId)));
        Assert.DoesNotContain(findings, f => f.RuleId!.StartsWith("input.", StringComparison.Ordinal));
    }

    [Fact]
    public void AValidDefinitionHasNoInputFindings()
    {
        Assert.Empty(_validator.ValidateInput(WizardState.CreateDefault()));
    }

    [Fact]
    public void ARuleRegisteredByTheHostRunsAfterTheBuiltInOnes()
    {
        var services = new ServiceCollection().AddPipelineBuilderCore();
        services.AddSingleton(new ValidationRule("house.pipeline-prefix", ValidationStage.Input, context =>
            context.Definition.Name.StartsWith("team-", StringComparison.Ordinal)
                ? Array.Empty<ValidationResult>()
                : new[]
                {
                    new ValidationResult
                    {
                        Severity = ValidationSeverity.Error,
                        Message = "Pipeline names start with 'team-'.",
                        AffectedField = nameof(PipelineDefinition.Name)
                    }
                }));
        var provider = services.BuildServiceProvider();
        var definition = WizardState.CreateDefault();

        var finding = Assert.Single(provider.GetRequiredService<IPipelineValidator>().ValidateInput(definition));
        Assert.Equal("house.pipeline-prefix", finding.RuleId);

        // An input finding blocks generation.
        var ex = Assert.Throws<ArgumentException>(() => provider.GetRequiredService<IPipelineGeneratorService>().Generate(definition));
        Assert.Contains("Pipeline names start with 'team-'.", ex.Message);

        definition.Name = "team-web";
        Assert.Empty(provider.GetRequiredService<IPipelineValidator>().ValidateInput(definition));
    }

    [Fact]
    public void DuplicateRuleIdsAreRejected()
    {
        var rule = new ValidationRule("house.rule", ValidationStage.Input, _ => Array.Empty<ValidationResult>());

        Assert.Throws<ArgumentException>(() => new PipelineValidator(new[] { rule, rule }));
    }

    [Fact]
    public void ARuleNeedsAnIdAndACheck()
    {
        Assert.Throws<ArgumentException>(() => new ValidationRule(" ", ValidationStage.Input, _ => Array.Empty<ValidationResult>()));
        Assert.Throws<ArgumentNullException>(() => new ValidationRule("house.rule", ValidationStage.Input, null!));
        Assert.Throws<ArgumentNullException>(() => _validator.ValidateGenerated(null!, ""));
    }

    [Theory]
    [InlineData("[", ValidationSeverity.Error, "regex is invalid")]
    [InlineData("^team-", ValidationSeverity.Warning, "does not match naming convention")]
    public void TheNamingConventionIsChecked(string pattern, ValidationSeverity severity, string message)
    {
        var definition = WizardState.CreateDefault();
        definition.Governance.NamingConvention = pattern;

        var finding = Assert.Single(_validator.ValidateGenerated(definition, "steps: []"), f => f.RuleId == "governance.naming-convention");

        Assert.Equal(severity, finding.Severity);
        Assert.Contains(message, finding.Message);
    }

    [Fact]
    public void AMatchingNameHasNoNamingFinding()
    {
        var definition = WizardState.CreateDefault();
        definition.Governance.NamingConvention = "^enterprise-";

        Assert.DoesNotContain(_validator.ValidateGenerated(definition, "steps: []"), f => f.RuleId == "governance.naming-convention");
    }
}
