namespace PipelineBuilder.Web.Components.Steps;

/// <summary>
/// Explanations of the Azure DevOps terms the wizard uses. They are shown behind "What is this?",
/// so the screen stays short for people who already know the terms.
/// </summary>
public static class Help
{
    public const string Environment =
        "An environment is a place the app runs, such as test or prod. You create it in Azure DevOps under Pipelines → Environments. " +
        "Your servers and approvals are set there.";

    public const string ServiceConnection =
        "A service connection is a login stored in Azure DevOps, so no password is written in the file. " +
        "Create it under Project settings → Service connections and type its name here.";

    public const string VariableGroup =
        "A variable group is a set of values stored in Azure DevOps under Pipelines → Library, such as paths and passwords. " +
        "They are not written in the file.";

    public const string SecureFile =
        "A secure file is a file stored in Azure DevOps under Pipelines → Library → Secure files. Upload the key there and type its name here.";

    public const string AnsibleValues =
        "The playbook gets three values: package_path (the folder with the build), environment_name (for example test) and build_id.";

    public const string ApprovalInFile =
        "This approval is in the pipeline file. An approval on the environment in Azure DevOps is safer, " +
        "because it cannot be removed by changing the file. Use that one for prod.";

    public const string EmptyField =
        "An empty field becomes a variable you set in Azure DevOps. They are listed under 'Needs in Azure DevOps'.";
}
