using PipelineBuilder.Core.Enums;

namespace PipelineBuilder.Web.Components.Steps;

/// <summary>What each kind of notification is called in the wizard.</summary>
public static class NotificationTitles
{
    public static string For(NotificationType type) => type switch
    {
        NotificationType.TeamsWebhook => "Teams",
        NotificationType.Email => "Email",
        _ => "Webhook"
    };
}
