using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Services;
using PipelineBuilder.Core.Yaml;
using Xunit;

namespace PipelineBuilder.Tests;

public class GovernanceAndSecretsTests
{
    private const string Pipeline = """
        # Generated pipeline; CmdLine@2 is not used here, only mentioned.
        stages:
        - stage: Build
          jobs:
          - job: BuildJob
            steps:
            - task: DotNetCoreCLI@2
              displayName: 'Build (we stopped using Docker@2 and PowerShell@2)'
              inputs:
                command: 'build'
            - powershell: |
                Write-Host 'Do not use AzureKeyVault@2 here'
              displayName: 'Inline script'
            - task: PublishPipelineArtifact@1
              inputs:
                targetPath: 'out'
        """;

    private static IReadOnlyList<ValidationResult> Validate(GovernancePolicyConfig governance) =>
        new GovernanceValidationService(new VariableGroupService()).Validate(
            new PipelineDefinition { Name = "app", Environments = new[] { "test" }, Governance = governance }, Pipeline);

    private static GovernancePolicyConfig Policy() => new() { RequiredApprovals = false, RequireHealthCheck = false };

    [Fact]
    public void FindsOnlyTasksThatRun()
    {
        var tasks = PipelineTaskInventory.FindTasks(Pipeline);

        Assert.Equal(new[] { "DotNetCoreCLI@2", "PowerShell@2", "PublishPipelineArtifact@1" }, tasks);
    }

    [Theory]
    [InlineData("CmdLine@2")]        // only in a comment
    [InlineData("Docker@2")]         // only in a display name
    [InlineData("AzureKeyVault@2")]  // only inside a script
    public void TaskNamesInTextAreNotFlagged(string forbidden)
    {
        var policy = Policy();
        policy.ForbiddenTasks = new[] { forbidden };

        Assert.DoesNotContain(Validate(policy), r => r.Message.Contains("Forbidden task"));
    }

    [Theory]
    [InlineData("DotNetCoreCLI@2")]
    [InlineData("dotnetcorecli")]   // no version: any version, any casing
    [InlineData("PowerShell@2")]    // the powershell: shortcut counts as PowerShell@2
    public void TasksThatRunAreFlagged(string forbidden)
    {
        var policy = Policy();
        policy.ForbiddenTasks = new[] { forbidden };

        Assert.Contains(Validate(policy), r => r.Severity == ValidationSeverity.Error && r.Message.Contains("Forbidden task"));
    }

    [Fact]
    public void ForbiddenVersionDoesNotMatchOtherVersions()
    {
        var policy = Policy();
        policy.ForbiddenTasks = new[] { "DotNetCoreCLI@1" };

        Assert.DoesNotContain(Validate(policy), r => r.Message.Contains("Forbidden task"));
    }

    [Fact]
    public void RequiredTasksAreCheckedAgainstStepsNotText()
    {
        var policy = Policy();
        policy.RequiredTasks = new[] { "PublishPipelineArtifact", "Docker@2" };

        var missing = Validate(policy).Where(r => r.Message.Contains("Required task")).ToList();

        var single = Assert.Single(missing);
        Assert.Contains("Docker@2", single.Message); // only mentioned in a display name, so still missing
    }

    [Theory]
    [InlineData("password = 'abc12345' # $(unused)", true)]
    [InlineData("  connectionString: 'Server=db;Password=x' $(Build.BuildId)", true)]
    [InlineData("password: $(DB_PASSWORD)", false)]
    [InlineData("password: ${{ variables.dbPassword }}", false)]
    [InlineData("SMTP_PASSWORD: '$(SMTP_PASSWORD)'", false)]
    [InlineData("password: ***", false)]
    public void SecretsNextToVariableReferencesAreStillFound(string line, bool expected)
    {
        var found = new SecretsGovernanceService().ScanYaml(line).Any(r => r.HasPlainTextSecret);
        Assert.Equal(expected, found);
    }

    [Fact]
    public void SecretGroupsSharedWithTheWholePipelineAreFlagged()
    {
        var groups = new[]
        {
            new VariableGroupConfig { Name = "vg-shared-secrets", Scope = VariableGroupScope.Pipeline, ContainsSecrets = true },
            new VariableGroupConfig { Name = "vg-prod-secrets", Scope = VariableGroupScope.Environment, EnvironmentName = "prod", ContainsSecrets = true },
            new VariableGroupConfig { Name = "vg-common", Scope = VariableGroupScope.Pipeline }
        };

        var warnings = new VariableGroupService().Validate(groups, null);

        var warning = Assert.Single(warnings);
        Assert.Equal(ValidationSeverity.Warning, warning.Severity);
        Assert.Contains("vg-shared-secrets", warning.Message);
    }
}
