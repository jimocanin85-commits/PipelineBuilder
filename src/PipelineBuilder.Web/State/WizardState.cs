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

    private readonly IPipelineValidator _validator;

    public WizardState(IPipelineValidator validator)
    {
        _validator = validator;
        Definition = CreateDefault();
        Load(Definition);
    }

    public WizardStep CurrentStep { get; set; } = WizardStep.Start;
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
    /// Key Vault section is rebuilt from <paramref name="definition"/>.
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

        Result = null;
        BlockingErrors = Array.Empty<ValidationResult>();
        NotifyChanged();
    }

    /// <summary>
    /// Issues to show on a step: blocking problems (always current) plus the warnings and errors
    /// from the last generated pipeline.
    /// </summary>
    public IReadOnlyList<ValidationResult> IssuesFor(WizardStep step) =>
        _validator.ValidateInput(Definition)
            .Concat(Result?.ValidationResults.Where(v => v.Severity != ValidationSeverity.Info) ?? Enumerable.Empty<ValidationResult>())
            .Where(v => StepMap.ForField(v.AffectedField) == step)
            .ToList();

    public bool HasErrors(WizardStep step) => IssuesFor(step).Any(v => v.Severity == ValidationSeverity.Error);

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

    public string ContainerPorts
    {
        get => string.Join(", ", Definition.Deployment.ContainerPorts);
        set => Definition.Deployment.ContainerPorts = SplitList(value);
    }

    /// <summary>One <c>NAME=value</c> per line.</summary>
    public string ContainerEnvironment
    {
        get => string.Join("\n", Definition.Deployment.ContainerEnvironment);
        set => Definition.Deployment.ContainerEnvironment =
            (value ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public KeyVaultConfig KeyVault => _keyVault;

    public bool KeyVaultEnabled
    {
        get => Definition.KeyVault != null;
        set => Definition.KeyVault = value ? _keyVault : null;
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
        }
    }

    public void Generate(IPipelineGeneratorService generator)
    {
        // Invalid settings: show the problems (with links to their steps) instead of generating.
        BlockingErrors = _validator.ValidateInput(Definition);
        Result = BlockingErrors.Count == 0 ? generator.Generate(Definition) : null;
    }

    public bool ApplyTemplate(ITemplateCatalogue templates, string templateId)
    {
        if (!templates.ApplyTo(templateId, Definition))
            return false;

        // An image needs a real name; the artifact default "drop" only suits a folder of files.
        if (Definition.Artifact.ArtifactType == ArtifactType.DockerImage && Definition.Artifact.ArtifactName == DefaultArtifactName)
            Definition.Artifact.ArtifactName = ImageNameFrom(Definition.Name);
        else if (Definition.Artifact.ArtifactType != ArtifactType.DockerImage)
            Definition.Artifact.ArtifactName = DefaultArtifactName;
        return true;
    }

    /// <summary>Rolling deployments update the servers a few at a time.</summary>
    public bool Rolling
    {
        get => Definition.DeploymentStrategy.StrategyType == DeploymentStrategyType.Rolling;
        set => Definition.DeploymentStrategy.StrategyType = value ? DeploymentStrategyType.Rolling : DeploymentStrategyType.Standard;
    }

    public const string DefaultArtifactName = "drop";

    /// <summary>A valid image repository name from a pipeline name: lower case, with letters, digits, '.', '_' and '-'.</summary>
    public static string ImageNameFrom(string pipelineName)
    {
        var name = new string(pipelineName.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '-').ToArray()).Trim('-', '.', '_');
        return name.Length == 0 ? "app" : name;
    }

    /// <summary>The settings a new wizard starts with: an IIS website, with nothing to set up but the environments.</summary>
    public static PipelineDefinition CreateDefault() => new()
    {
        Name = "my-app",
        TemplateId = "iis-onprem",
        ProjectType = ProjectType.DotNet,
        BuildAgent = BuildAgentType.MicrosoftHosted,
        Environments = new[] { "test", "prod" },
        DotNetProjectPath = "**/*.csproj",
        TestProjectPath = "**/*Tests*.csproj",
        Artifact = new ArtifactConfig { ArtifactType = ArtifactType.PipelineArtifact, ArtifactName = DefaultArtifactName },
        Deployment = new DeploymentConfig { Kind = DeploymentKind.Iis, WebsiteName = "Default Web Site" },
        Rollback = new RollbackConfig { Enabled = true, Target = RollbackTarget.Iis, RetentionCount = 5 }
    };

    private static IReadOnlyList<string> SplitList(string? value) =>
        (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
