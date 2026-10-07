using PipelineBuilder.Core.Abstractions;
using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Yaml;

namespace PipelineBuilder.Core.Services;

/// <summary>
/// Generates notification steps. Whether a step runs on success or failure is decided by the
/// stage it is placed in (see <see cref="PipelineYamlAssembler.AddNotificationStages"/>), not by
/// the step itself.
/// <para>
/// The pipeline's name and build number are read from the environment (<c>$env:BUILD_BUILDNUMBER</c>),
/// not written into the script with <c>$(Build.BuildNumber)</c>: a build can change its own number,
/// and text pasted into a script would run as code in a stage that holds the webhook and mail secrets.
/// </para>
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
$webhook = $env:WEBHOOK_URL
if ([string]::IsNullOrWhiteSpace($webhook) -or $webhook.StartsWith('$(')) {
  Write-Warning 'Teams webhook variable is not set; skipping notification.'
  exit 0
}
$body = @{ text = {{Headline(outcome)}} } | ConvertTo-Json
Invoke-RestMethod -Uri $webhook -Method Post -Body $body -ContentType 'application/json'
""", succeeded ? "Notify Teams on success" : "Notify Teams on failure", env: WebhookEnv(config.TeamsWebhookVariable, "TEAMS_WEBHOOK_URL"))
            },
            NotificationType.Email => new[] { EmailStep(config, succeeded, outcome) },
            NotificationType.CustomWebhook => new[]
            {
                YamlBuilder.PowerShellStep($$"""
$webhook = $env:WEBHOOK_URL
if ([string]::IsNullOrWhiteSpace($webhook) -or $webhook.StartsWith('$(')) {
  Write-Warning 'Webhook variable is not set; skipping notification.'
  exit 0
}
$payload = @{ event = '{{outcome}}'; build = $env:BUILD_BUILDNUMBER } | ConvertTo-Json
Invoke-RestMethod -Uri $webhook -Method Post -Body $payload -ContentType 'application/json'
""", succeeded ? "Custom webhook on success" : "Custom webhook on failure", env: WebhookEnv(config.WebhookUrlVariable, "CUSTOM_WEBHOOK_URL"))
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
$message.Subject = {{Headline(outcome)}}
$message.Body = 'Run: {0}{1}/_build/results?buildId={2}' -f $env:SYSTEM_COLLECTIONURI, $env:SYSTEM_TEAMPROJECT, $env:BUILD_BUILDID
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
    /// Maps the webhook variable into the step's environment as <c>WEBHOOK_URL</c>. A secret is only
    /// visible to a script when it is mapped like this, and the address never becomes part of the
    /// script text. Invalid variable names fall back to the default name.
    /// </summary>
    private static Dictionary<string, string> WebhookEnv(string? variableName, string fallback)
    {
        var name = string.IsNullOrWhiteSpace(variableName) || !IsValidVariableName(variableName)
            ? fallback
            : variableName.Trim();
        return new Dictionary<string, string> { ["WEBHOOK_URL"] = $"$({name})" };
    }

    /// <summary>What every notification says, as a PowerShell string: which pipeline, which run, and how it went.</summary>
    private static string Headline(string outcome) => $"\"Pipeline $env:BUILD_DEFINITIONNAME #$env:BUILD_BUILDNUMBER {outcome}\"";

    private static bool IsValidVariableName(string name) =>
        name.Trim().All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '-');
}
