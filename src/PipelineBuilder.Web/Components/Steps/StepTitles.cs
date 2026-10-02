using PipelineBuilder.Web.State;

namespace PipelineBuilder.Web.Components.Steps;

public static class StepTitles
{
    public static string For(WizardStep step) => step switch
    {
        WizardStep.Start => "What",
        WizardStep.Target => "Where",
        WizardStep.Safety => "Safety",
        WizardStep.Result => "Result",
        _ => step.ToString()
    };
}
