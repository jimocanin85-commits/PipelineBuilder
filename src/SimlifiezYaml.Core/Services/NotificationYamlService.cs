using SimlifiezYaml.Core.Abstractions;
using SimlifiezYaml.Core.Enums;
using SimlifiezYaml.Core.Models;
using SimlifiezYaml.Core.Yaml;

namespace SimlifiezYaml.Core.Services;

/// <summary>
/// Generates notification steps. Whether a step runs on success or failure is decided by the
/// stage it is placed in (see <see cref="PipelineYamlAssembler.AddNotificationStages"/>), not by
/// the step itself.
/// </summary>
public sealed class NotificationYamlService : INotificationYamlService
{
    public IReadOnlyList<string> GenerateNotificationSteps(NotificationConfig config, bool succeeded)
    {
        var outcome = succeeded ? "succeeded" : "failed";
        return config.NotificationType switch
        {
            NotificationType.TeamsWebhook => new[]
            {
                YamlBuilder.PowerShellStep($$"""
$webhook = {{WebhookMacro(config.TeamsWebhookVariable, "TEAMS_WEBHOOK_URL")}}
if ([string]::IsNullOrWhiteSpace($webhook) -or $webhook.StartsWith('$(')) {
  Write-Warning 'Teams webhook variable is not set; skipping notification.'
  exit 0
}
$body = @{ text = "Pipeline $(Build.DefinitionName) #$(Build.BuildNumber) {{outcome}}" } | ConvertTo-Json
Invoke-RestMethod -Uri $webhook -Method Post -Body $body -ContentType 'application/json'
""", succeeded ? "Notify Teams on success" : "Notify Teams on failure")
            },
            NotificationType.Email => new[] { EmailStep(config, succeeded, outcome) },
            NotificationType.CustomWebhook => new[]
            {
                YamlBuilder.PowerShellStep($$"""
$webhook = {{WebhookMacro(config.WebhookUrlVariable, "CUSTOM_WEBHOOK_URL")}}
if ([string]::IsNullOrWhiteSpace($webhook) -or $webhook.StartsWith('$(')) {
  Write-Warning 'Webhook variable is not set; skipping notification.'
  exit 0
}
$payload = @{ event = '{{outcome}}'; build = '$(Build.BuildNumber)' } | ConvertTo-Json
Invoke-RestMethod -Uri $webhook -Method Post -Body $payload -ContentType 'application/json'
""", succeeded ? "Custom webhook on success" : "Custom webhook on failure")
            },
            _ => Array.Empty<string>()
        };
    }

    /// <summary>
    /// Sends the email over SMTP. Server settings come from pipeline variables; the password is a
    /// secret variable, which scripts only see when it is mapped into the step's environment.
    /// If SMTP_HOST is not defined the step warns and succeeds, so a missing setup never fails a deploy.
    /// </summary>
    private static string EmailStep(NotificationConfig config, bool succeeded, string outcome)
    {
        var recipients = string.Join(", ", config.EmailRecipients
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => YamlBuilder.PsLiteral(r.Trim())));
        var env = new Dictionary<string, string> { ["SMTP_PASSWORD"] = "$(SMTP_PASSWORD)" };

        return YamlBuilder.PowerShellStep($$"""
$smtpHost = '$(SMTP_HOST)'
if ([string]::IsNullOrWhiteSpace($smtpHost) -or $smtpHost.StartsWith('$(')) {
  Write-Warning 'SMTP_HOST is not set; skipping email notification.'
  exit 0
}
$recipients = @({{recipients}})
if ($recipients.Count -eq 0) {
  Write-Warning 'No email recipients configured; skipping email notification.'
  exit 0
}
$port = '$(SMTP_PORT)'
if ($port.StartsWith('$(')) { $port = '587' }
$from = '$(EMAIL_FROM)'
$user = '$(SMTP_USERNAME)'
$message = New-Object System.Net.Mail.MailMessage
$message.From = $from
foreach ($recipient in $recipients) { $message.To.Add($recipient) }
$message.Subject = "Pipeline $(Build.DefinitionName) #$(Build.BuildNumber) {{outcome}}"
$message.Body = "Run: $(System.CollectionUri)$(System.TeamProject)/_build/results?buildId=$(Build.BuildId)"
$client = New-Object System.Net.Mail.SmtpClient($smtpHost, [int]$port)
$client.EnableSsl = $true
if (-not $user.StartsWith('$(')) {
  $client.Credentials = New-Object System.Net.NetworkCredential($user, $env:SMTP_PASSWORD)
}
$client.Send($message)
Write-Host "Email sent to $($recipients -join ', ')"
""", succeeded ? "Email on success" : "Email on failure", env: env);
    }

    /// <summary>
    /// Builds a PowerShell string literal holding an Azure DevOps macro such as
    /// <c>'$(TEAMS_WEBHOOK_URL)'</c>, which the agent replaces with the variable's value at runtime.
    /// Invalid variable names fall back to the default name.
    /// </summary>
    private static string WebhookMacro(string? variableName, string fallback)
    {
        var name = string.IsNullOrWhiteSpace(variableName) || !IsValidVariableName(variableName)
            ? fallback
            : variableName.Trim();
        return YamlBuilder.PsLiteral($"$({name})");
    }

    private static bool IsValidVariableName(string name) =>
        name.Trim().All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-');
}
