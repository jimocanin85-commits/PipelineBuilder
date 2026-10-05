namespace PipelineBuilder.Web.Components.Steps;

/// <summary>
/// Explanations of the Azure DevOps terms the wizard uses. They are shown behind "What is this?",
/// so the screen stays short for people who already know the terms.
/// </summary>
public static class Help
{
    public const string Environment =
        "An environment is a named place the app runs in, such as test or prod. In Azure DevOps you create it under " +
        "Pipelines → Environments. Your servers are registered in it, and approvals are set on it, so nobody deploys to prod by accident.";

    public const string ServiceConnection =
        "A service connection is a login that Azure DevOps keeps for you, so no password is written in the pipeline. " +
        "Create it under Project settings → Service connections and type its name here.";

    public const string VariableGroup =
        "A variable group is a named set of values kept in Azure DevOps under Pipelines → Library, for example server paths and passwords. " +
        "The pipeline reads them when it runs, so they are not written in the file.";

    public const string EmptyField =
        "Leave a field empty to set it in Azure DevOps instead: it becomes a pipeline variable, listed on the Result step.";
}
