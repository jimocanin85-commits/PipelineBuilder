using PipelineBuilder.Core.Abstractions;
using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Yaml;

namespace PipelineBuilder.Core.Services;

public sealed class HealthCheckYamlService : IHealthCheckYamlService
{
    public IReadOnlyList<string> GenerateHealthCheckSteps(HealthCheckConfig config)
    {
        if (!config.Enabled) return Array.Empty<string>();

        return config.HealthCheckType switch
        {
            HealthCheckType.HttpEndpoint => new[]
            {
                YamlBuilder.PowerShellStep($$"""
$uri = {{YamlBuilder.PsLiteral(config.Url ?? "https://localhost/health")}}
$expected = {{config.ExpectedStatusCode}}
$timeout = {{config.TimeoutSeconds}}
$retries = {{config.RetryCount}}
for ($i = 1; $i -le $retries; $i++) {
  try {
    $r = Invoke-WebRequest -Uri $uri -UseBasicParsing -TimeoutSec $timeout
    if ($r.StatusCode -eq $expected) { Write-Host 'Health check passed'; exit 0 }
  } catch { Write-Warning "Attempt $i failed: $_" }
  Start-Sleep -Seconds 5
}
Write-Error 'HTTP health check failed'
exit 1
""", "HTTP endpoint health check")
            },
            HealthCheckType.IisAppPool => new[]
            {
                YamlBuilder.PowerShellStep($$"""
Import-Module WebAdministration -ErrorAction Stop
$pool = {{YamlBuilder.PsLiteral(config.AppPoolName ?? "DefaultAppPool")}}
$state = (Get-WebAppPoolState -Name $pool).Value
if ($state -ne 'Started') { throw "App pool $pool is $state" }
Write-Host "App pool $pool is healthy"
""", "IIS app pool health check")
            },
            HealthCheckType.WindowsService => new[]
            {
                YamlBuilder.PowerShellStep($$"""
$svc = Get-Service -Name {{YamlBuilder.PsLiteral(config.ServiceName ?? "W3SVC")}} -ErrorAction Stop
if ($svc.Status -ne 'Running') { throw "Service $($svc.Name) is $($svc.Status)" }
Write-Host "Service $($svc.Name) is running"
""", "Windows service health check")
            },
            HealthCheckType.PortCheck => new[]
            {
                YamlBuilder.PowerShellStep($$"""
$port = {{config.Port ?? 80}}
$r = Test-NetConnection -ComputerName localhost -Port $port -WarningAction SilentlyContinue
if (-not $r.TcpTestSucceeded) { throw "Port $port is not reachable" }
Write-Host "Port $port is open"
""", "Port health check")
            },
            HealthCheckType.CustomPowerShell => new[]
            {
                YamlBuilder.PowerShellStep(config.CustomScript ?? "Write-Host 'Custom health check passed'", "Custom health check")
            },
            _ => Array.Empty<string>()
        };
    }
}
