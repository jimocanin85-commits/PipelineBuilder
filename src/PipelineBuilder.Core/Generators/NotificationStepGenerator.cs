using PipelineBuilder.Core.Abstractions;
using PipelineBuilder.Core.Models;

namespace PipelineBuilder.Core.Generators;

public sealed class NotificationStepGenerator
{
    private readonly INotificationYamlService _notificationService;

    public NotificationStepGenerator(INotificationYamlService notificationService) => _notificationService = notificationService;

    /// <summary>Returns the steps to run when the pipeline succeeded or failed.</summary>
    public IReadOnlyList<string> GenerateSteps(PipelineDefinition definition, bool succeeded) =>
        definition.Notifications
            .Where(n => succeeded ? n.NotifyOnSuccess : n.NotifyOnFailure)
            .SelectMany(n => _notificationService.GenerateNotificationSteps(n, succeeded))
            .ToList();
}
