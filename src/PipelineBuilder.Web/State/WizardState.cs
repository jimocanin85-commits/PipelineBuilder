using PipelineBuilder.Core.Abstractions;
using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;

namespace PipelineBuilder.Web.State;

/// <summary>
/// Everything the wizard edits. Step components bind straight to <see cref="Definition"/> (or to
/// the helper properties here), so nothing is lost when jumping between steps.
/// </summary>
public sealed class WizardState
{
    private KeyVaultConfig _keyVault = new();
    private InfrastructureAsCodeConfig _iac = new() { WorkingDirectory = "infra" };
    private AgentDiagnosticConfig _agentDiagnostics = new();

    public WizardState()
    {
        Definition = CreateDefault();
        Load(Definition);
    }

    public WizardStep CurrentStep { get; set; } = WizardStep.ProjectType;
    public PipelineDefinition Definition { get; private set; }

    // Editable lists; the same instances are assigned to Definition.
    public List<VariableGroupConfig> VariableGroups { get; } = new();
    public List<HealthCheckConfig> HealthChecks { get; } = new();
    public List<NotificationConfig> Notifications { get; } = new();

    public GeneratedPipeline? Result { get; private set; }

    /// <summary>Problems that stopped the last generation (invalid settings).</summary>
    public IReadOnlyList<ValidationResult> BlockingErrors { get; private set; } = Array.Empty<ValidationResult>();

    /// <summary>Raised when a component asks to show another step (e.g. "Go to step" on an error).</summary>
    public event Action<WizardStep>? NavigationRequested;

    public void RequestNavigation(WizardStep step) => NavigationRequested?.Invoke(step);

    /// <summary>Raised when settings change in a way the whole page should reflect (e.g. a file was loaded).</summary>
    public event Action? Changed;

    public void NotifyChanged() => Changed?.Invoke();

    /// <summary>
    /// Result of the last "Open saved settings". Kept here rather than in the component, because
    /// loading replaces the form (and so recreates the step components).
    /// </summary>
    public (string Text, bool IsError)? SettingsMessage { get; set; }

    /// <summary>
    /// Replaces all settings, e.g. with a saved settings file. The editable lists and the
    /// Key Vault / IaC / diagnostics sections are rebuilt from <paramref name="definition"/>.
    /// </summary>
    public void Load(PipelineDefinition definition)
    {
        Definition = definition;
        VariableGroups.Clear();
        VariableGroups.AddRange(definition.VariableGroups);
        HealthChecks.Clear();
        HealthChecks.AddRange(definition.HealthChecks);
        Notifications.Clear();
        Notifications.AddRange(definition.Notifications);
        definition.VariableGroups = VariableGroups;
        definition.HealthChecks = HealthChecks;
        definition.Notifications = Notifications;

        _keyVault = definition.KeyVault ?? new KeyVaultConfig { ServiceConnection = definition.AzureServiceConnection };
        _iac = definition.IaC ?? new InfrastructureAsCodeConfig { WorkingDirectory = "infra", ServiceConnection = definition.AzureServiceConnection };
        _agentDiagnostics = definition.AgentDiagnostics ?? new AgentDiagnosticConfig();

        Result = null;
        BlockingErrors = Array.Empty<ValidationResult>();
        ScanResult = null;
        NotifyChanged();
    }

    /// <summary>
    /// Issues to show on a step: blocking problems (always current) plus the warnings and errors
    /// from the last generated pipeline.
    /// </summary>
    public IReadOnlyList<ValidationResult> IssuesFor(WizardStep step) =>
        PipelineDefinitionValidator.ValidateDetailed(Definition)
            .Concat(Result?.ValidationResults.Where(v => v.Severity != ValidationSeverity.Info) ?? Enumerable.Empty<ValidationResult>())
            .Where(v => StepMap.ForField(v.AffectedField) == step)
            .ToList();

    public bool HasErrors(WizardStep step) => IssuesFor(step).Any(v => v.Severity == ValidationSeverity.Error);
    public RepoScanResult? ScanResult { get; set; }
    public string RepoPathsInput { get; set; } = "src/MyApp/MyApp.csproj\nDockerfile\ntests/MyApp.Tests/MyApp.Tests.csproj";

    public PipelineDependencyGraph DependencyGraph => PipelineDependencyGraph.FromDefinition(Definition);

    public string EnvironmentsCsv
    {
        get => string.Join(", ", Definition.Environments);
        set => Definition.Environments = SplitList(value);
    }

    public string IncludeBranches
    {
        get => string.Join(", ", Definition.Trigger.IncludeBranches);
        set => Definition.Trigger.IncludeBranches = SplitList(value);
    }

    public string ExcludeBranches
    {
        get => string.Join(", ", Definition.Trigger.ExcludeBranches);
        set => Definition.Trigger.ExcludeBranches = SplitList(value);
    }

    public string PathFilters
    {
        get => string.Join(", ", Definition.Trigger.PathFilters);
        set => Definition.Trigger.PathFilters = SplitList(value);
    }

    public string RequiredVariableGroups
    {
        get => string.Join(", ", Definition.Governance.RequiredVariableGroups);
        set => Definition.Governance.RequiredVariableGroups = SplitList(value);
    }

    public string ForbiddenTasks
    {
        get => string.Join(", ", Definition.Governance.ForbiddenTasks);
        set => Definition.Governance.ForbiddenTasks = SplitList(value);
    }

    public string RequiredTasks
    {
        get => string.Join(", ", Definition.Governance.RequiredTasks);
        set => Definition.Governance.RequiredTasks = SplitList(value);
    }

    public string DeploymentFolders
    {
        get => string.Join("\n", _agentDiagnostics.DeploymentFolders);
        set => _agentDiagnostics.DeploymentFolders = SplitLines(value);
    }

    public AgentDiagnosticConfig AgentDiagnostics => _agentDiagnostics;
    public KeyVaultConfig KeyVault => _keyVault;
    public InfrastructureAsCodeConfig IaC => _iac;

    public bool KeyVaultEnabled
    {
        get => Definition.KeyVault != null;
        set => Definition.KeyVault = value ? _keyVault : null;
    }

    public bool IaCEnabled
    {
        get => Definition.IaC != null;
        set => Definition.IaC = value ? _iac : null;
    }

    public bool AgentDiagnosticsEnabled
    {
        get => Definition.AgentDiagnostics != null;
        set => Definition.AgentDiagnostics = value ? _agentDiagnostics : null;
    }

    /// <summary>Sets the Azure service connection everywhere it is used.</summary>
    public string AzureServiceConnection
    {
        get => Definition.AzureServiceConnection;
        set
        {
            var connection = string.IsNullOrWhiteSpace(value) ? "$(AZURE_SERVICE_CONNECTION)" : value.Trim();
            Definition.AzureServiceConnection = connection;
            _keyVault.ServiceConnection = connection;
            _iac.ServiceConnection = connection;
        }
    }

    public void Generate(IPipelineGeneratorService generator)
    {
        // Invalid settings: show the problems (with links to their steps) instead of generating.
        BlockingErrors = PipelineDefinitionValidator.ValidateDetailed(Definition);
        Result = BlockingErrors.Count == 0 ? generator.Generate(Definition) : null;
    }

    public void ScanRepository(IRepoScannerService scanner)
    {
        ScanResult = scanner.ScanFileList(SplitLines(RepoPathsInput));
        if (ScanResult.ProjectType != ProjectType.Unknown)
            Definition.ProjectType = ScanResult.ProjectType;
    }

    public bool ApplyTemplate(ITemplateMarketplaceService marketplace, string templateId)
    {
        var applied = marketplace.ApplyTo(templateId, Definition);
        if (applied && Definition.IaC != null)
            _iac = Definition.IaC;
        return applied;
    }

    public static PipelineDefinition CreateDefault() => new()
    {
        Name = "enterprise-pipeline",
        ProjectType = ProjectType.DotNet,
        BuildAgent = BuildAgentType.MicrosoftHosted,
        DeploymentTarget = DeploymentTarget.OnPrem,
        Environments = new[] { "test", "preprod", "prod" },
        DotNetProjectPath = "**/*.csproj",
        TestProjectPath = "**/*Tests*.csproj",
        VariableGroups = new[]
        {
            new VariableGroupConfig { Name = "vg-test", Scope = VariableGroupScope.Pipeline },
            new VariableGroupConfig { Name = "vg-prod-secrets", Scope = VariableGroupScope.Environment, EnvironmentName = "prod", ContainsSecrets = true }
        },
        Artifact = new ArtifactConfig { ArtifactType = ArtifactType.PipelineArtifact, ArtifactName = "drop" },
        Deployment = new DeploymentConfig { Kind = DeploymentKind.Iis, WebsiteName = "Default Web Site" },
        Rollback = new RollbackConfig { Enabled = true, BackupPath = @"D:\backups", Target = RollbackTarget.Iis, RetentionCount = 5 },
        HealthChecks = new[]
        {
            new HealthCheckConfig { Enabled = true, HealthCheckType = HealthCheckType.HttpEndpoint, Url = "https://myapp-{environment}.contoso.com/health", ExpectedStatusCode = 200 }
        },
        Notifications = new[]
        {
            new NotificationConfig { NotificationType = NotificationType.TeamsWebhook, TeamsWebhookVariable = "TEAMS_WEBHOOK_URL", NotifyOnFailure = true }
        },
        Governance = new GovernancePolicyConfig
        {
            RequiredApprovals = true,
            RequireHealthCheck = true,
            RequireRollback = true,
            RequiredVariableGroups = new[] { "vg-test" },
            ForbiddenTasks = new[] { "CmdLine@2" }
        },
        AgentDiagnostics = new AgentDiagnosticConfig
        {
            CheckWinRm = true,
            CheckIisModule = true,
            DeploymentFolders = new[] { @"D:\deploy", @"C:\inetpub\wwwroot" }
        }
    };

    private static IReadOnlyList<string> SplitList(string? value) =>
        (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IReadOnlyList<string> SplitLines(string? value) =>
        (value ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
