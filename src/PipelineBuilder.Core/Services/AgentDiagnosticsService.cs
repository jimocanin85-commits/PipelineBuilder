using System.Text;
using PipelineBuilder.Core.Abstractions;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Yaml;

namespace PipelineBuilder.Core.Services;

public sealed class AgentDiagnosticsService : IAgentDiagnosticsService
{
    public string GenerateDiagnosticScript(AgentDiagnosticConfig config)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# PipelineBuilder - Self-hosted agent diagnostics");
        sb.AppendLine("$ErrorActionPreference = 'Continue'");
        sb.AppendLine("$results = @()");

        if (config.CheckPowerShellVersion)
        {
            sb.AppendLine("$results += [pscustomobject]@{ Check='PowerShell'; Result=$PSVersionTable.PSVersion.ToString() }");
        }

        if (config.CheckWinRm)
        {
            sb.AppendLine("""
try {
  $wsman = Test-WSMan -ComputerName localhost -ErrorAction Stop
  $results += [pscustomobject]@{ Check='WinRM'; Result='OK' }
} catch {
  $results += [pscustomobject]@{ Check='WinRM'; Result="FAIL: $_" }
}
""");
        }

        if (config.CheckIisModule)
        {
            sb.AppendLine("""
if (Get-Module -ListAvailable -Name WebAdministration) {
  Import-Module WebAdministration -ErrorAction SilentlyContinue
  $results += [pscustomobject]@{ Check='IIS WebAdministration'; Result='OK' }
} else {
  $results += [pscustomobject]@{ Check='IIS WebAdministration'; Result='Module not found' }
}
""");
        }

        if (config.CheckDocker)
        {
            sb.AppendLine("""
try {
  $docker = docker version --format '{{.Server.Version}}' 2>&1
  $results += [pscustomobject]@{ Check='Docker'; Result=$docker }
} catch {
  $results += [pscustomobject]@{ Check='Docker'; Result="FAIL: $_" }
}
""");
        }

        sb.AppendLine("$agents = Get-Service -Name 'vstsagent*' -ErrorAction SilentlyContinue");
        sb.AppendLine("$results += [pscustomobject]@{ Check='Azure Pipelines Agent'; Result=($agents | Select-Object -ExpandProperty Status -Unique) -join ',' }");

        foreach (var folder in config.DeploymentFolders)
        {
            sb.AppendLine($"$results += [pscustomobject]@{{ Check={YamlBuilder.PsLiteral("Path " + folder)}; Result=(Test-Path -LiteralPath {YamlBuilder.PsLiteral(folder)}) }}");
        }

        if (config.CheckNetworkAccess)
        {
            sb.AppendLine("""
$endpoints = @('dev.azure.com', 'login.microsoftonline.com')
foreach ($ep in $endpoints) {
  $r = Test-NetConnection -ComputerName $ep -Port 443 -WarningAction SilentlyContinue
  $results += [pscustomobject]@{ Check="Network $ep"; Result=$r.TcpTestSucceeded }
}
""");
        }

        if (config.CheckPermissions)
        {
            sb.AppendLine("$results += [pscustomobject]@{ Check='Running as'; Result=[System.Security.Principal.WindowsIdentity]::GetCurrent().Name }");
        }

        sb.AppendLine("$results | Format-Table -AutoSize");
        sb.AppendLine("if ($results.Result -match 'FAIL|False|not found') { exit 1 }");
        return sb.ToString();
    }
}
