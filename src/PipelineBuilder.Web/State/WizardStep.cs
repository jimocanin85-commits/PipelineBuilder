namespace PipelineBuilder.Web.State;

/// <summary>The wizard's steps, in order.</summary>
public enum WizardStep
{
    /// <summary>What is deployed: the template, the name and how it is built.</summary>
    Start = 1,

    /// <summary>Where it runs: environments, servers or cluster, build agent and variables.</summary>
    Target = 2,

    /// <summary>Rollback, health checks and notifications.</summary>
    Safety = 3,

    /// <summary>The pipeline, what it needs in Azure DevOps, and the download.</summary>
    Result = 4
}
