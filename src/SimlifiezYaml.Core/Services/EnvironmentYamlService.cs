using SimlifiezYaml.Core.Abstractions;

namespace SimlifiezYaml.Core.Services;

public sealed class EnvironmentYamlService : IEnvironmentYamlService
{
    public const string ApprovalUiNote =
        "Approvals are configured in Azure DevOps Environments, not directly in YAML.";

    public string GetApprovalUiNote() => ApprovalUiNote;
}
