using PipelineBuilder.Core.Abstractions;
using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Yaml;

namespace PipelineBuilder.Core.Services;

public sealed class HealthCheckYamlService : IHealthCheckYamlService
{
    public IReadOnlyList<string> GenerateHealthCheckSteps(HealthCheckConfig config, ScriptShell shell = ScriptShell.PowerShell, DeploymentConfig? deployment = null)
    {
        if (!config.Enabled) return Array.Empty<string>();

        // Without deployment settings the check runs where the job runs; with them it runs on the servers.
        var onServers = deployment ?? new DeploymentConfig();
        return shell == ScriptShell.Bash ? BashSteps(config, onServers) : PowerShellSteps(config, onServers);
    }

    private static IReadOnlyList<string> PowerShellSteps(HealthCheckConfig config, DeploymentConfig deployment)
    {
        return config.HealthCheckType switch
        {
            HealthCheckType.HttpEndpoint => new[]
            {
                ServerScript.Step(deployment, ScriptShell.PowerShell, $$"""
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
                ServerScript.Step(deployment, ScriptShell.PowerShell, $$"""
Import-Module WebAdministration -ErrorAction Stop
$pool = {{YamlBuilder.PsLiteral(config.AppPoolName ?? "DefaultAppPool")}}
$state = (Get-WebAppPoolState -Name $pool).Value
if ($state -ne 'Started') { throw "App pool $pool is $state" }
Write-Host "App pool $pool is healthy"
""", "IIS app pool health check")
            },
            HealthCheckType.WindowsService => new[]
            {
                ServerScript.Step(deployment, ScriptShell.PowerShell, $$"""
$svc = Get-Service -Name {{YamlBuilder.PsLiteral(config.ServiceName ?? "W3SVC")}} -ErrorAction Stop
if ($svc.Status -ne 'Running') { throw "Service $($svc.Name) is $($svc.Status)" }
Write-Host "Service $($svc.Name) is running"
""", "Windows service health check")
            },
            HealthCheckType.PortCheck => new[]
            {
                ServerScript.Step(deployment, ScriptShell.PowerShell, $$"""
$port = {{config.Port ?? 80}}
$r = Test-NetConnection -ComputerName localhost -Port $port -WarningAction SilentlyContinue
if (-not $r.TcpTestSucceeded) { throw "Port $port is not reachable" }
Write-Host "Port $port is open"
""", "Port health check")
            },
            HealthCheckType.CustomPowerShell => new[]
            {
                ServerScript.Step(deployment, ScriptShell.PowerShell, config.CustomScript ?? "Write-Host 'Custom health check passed'", "Custom health check")
            },
            _ => Array.Empty<string>()
        };
    }

    /// <summary>The same checks for Linux servers. IIS app pool checks have no Linux counterpart and are skipped.</summary>
    private static IReadOnlyList<string> BashSteps(HealthCheckConfig config, DeploymentConfig deployment) => config.HealthCheckType switch
    {
        HealthCheckType.HttpEndpoint => new[]
        {
            ServerScript.Step(deployment, ScriptShell.Bash, $$"""
uri={{YamlBuilder.BashLiteral(config.Url ?? "https://localhost/health")}}
expected={{config.ExpectedStatusCode}}
timeout={{config.TimeoutSeconds}}
retries={{config.RetryCount}}
for i in `seq 1 $retries`; do
  code=`curl -s -o /dev/null -w '%{http_code}' --max-time $timeout "$uri" || true`
  if [ "$code" = "$expected" ]; then echo 'Health check passed'; exit 0; fi
  echo "Attempt $i failed: HTTP $code"
  sleep 5
done
echo '##vso[task.logissue type=error]HTTP health check failed'
exit 1
""", "HTTP endpoint health check")
        },
        HealthCheckType.WindowsService => new[]
        {
            ServerScript.Step(deployment, ScriptShell.Bash, $$"""
service={{YamlBuilder.BashLiteral(config.ServiceName ?? "$(SERVICE_NAME)")}}
if ! systemctl is-active --quiet "$service"; then
  echo "##vso[task.logissue type=error]Service $service is not running"
  exit 1
fi
echo "Service $service is running"
""", "Service health check")
        },
        HealthCheckType.PortCheck => new[]
        {
            ServerScript.Step(deployment, ScriptShell.Bash, $$"""
port={{config.Port ?? 80}}
if ! timeout 5 bash -c "</dev/tcp/localhost/$port" 2>/dev/null; then
  echo "##vso[task.logissue type=error]Port $port is not reachable"
  exit 1
fi
echo "Port $port is open"
""", "Port health check")
        },
        HealthCheckType.CustomPowerShell => new[]
        {
            ServerScript.Step(deployment, ScriptShell.Bash, config.CustomScript ?? "echo 'Custom health check passed'", "Custom health check")
        },
        _ => Array.Empty<string>()
    };
}
