using SimlifiezYaml.Core.Enums;
using SimlifiezYaml.Core.Models;
using SimlifiezYaml.Core.Yaml;
using Xunit;

namespace SimlifiezYaml.Tests;

public class YamlBuilderTests
{
    [Fact]
    public void PowerShellStep_WritesScriptVerbatim()
    {
        // Scripts live in a YAML literal block, so they must NOT be escaped.
        var step = YamlBuilder.PowerShellStep("Write-Host 'test' $(Build.BuildId) $($x.Name)", "Test");
        Assert.Contains("Write-Host 'test' $(Build.BuildId) $($x.Name)", step);
        Assert.DoesNotContain("''test''", step);
    }

    [Fact]
    public void PowerShellStep_EscapesDisplayNameWithQuotes()
    {
        var step = YamlBuilder.PowerShellStep("echo test", "Test's Step");
        Assert.Contains("displayName: 'Test''s Step'", step);
    }

    [Fact]
    public void PowerShellStep_HandlesMultilineScripts()
    {
        var script = "Write-Host 'line 1'\nWrite-Host 'line 2'";
        var step = YamlBuilder.PowerShellStep(script, "Multi");
        Assert.Contains("    - powershell: |\n        Write-Host 'line 1'\n        Write-Host 'line 2'\n", step);
    }

    [Fact]
    public void Task_EscapesInputValues()
    {
        var task = YamlBuilder.Task("TestTask@1", 
            new Dictionary<string, string> { ["input"] = "value'with'quotes" }, 
            "Display'Name");
        Assert.Contains("input: 'value''with''quotes'", task);
        Assert.Contains("displayName: 'Display''Name'", task);
    }

    [Fact]
    public void Indent_PreservesEmptyLines()
    {
        var content = "line1\n\nline3";
        var indented = YamlBuilder.Indent(content, 2);
        Assert.Contains("  line1", indented);
        Assert.Contains("  line3", indented);
    }

    [Theory]
    [InlineData("C:\\path\\x", "'C:\\path\\x'")]
    [InlineData("it's", "'it''s'")]
    [InlineData("$(Get-Secret); Remove-Item *", "'$(Get-Secret); Remove-Item *'")]
    [InlineData("a\nb", "'a b'")]
    public void PsLiteral_QuotesUserValues(string input, string expected)
    {
        Assert.Equal(expected, YamlBuilder.PsLiteral(input));
    }

    [Fact]
    public void YamlString_DoesNotDoubleBackslashes()
    {
        Assert.Equal("'D:\\backups'", YamlBuilder.YamlString("D:\\backups"));
    }

    [Theory]
    [InlineData("prod", "prod")]
    [InlineData("pre-prod", "pre_prod")]
    public void ToIdentifier_ProducesValidStageNames(string input, string expected)
    {
        Assert.Equal(expected, YamlBuilder.ToIdentifier(input));
    }

    [Fact]
    public void Task_SafelyHandlesPathsWithSpecialChars()
    {
        var task = YamlBuilder.Task("ScriptTask@1",
            new Dictionary<string, string> { ["scriptPath"] = "C:\\path\\to\\script's file.ps1" },
            "Test");
        Assert.Contains(":", task);  // Colon in path is preserved
        Assert.Contains("''", task);  // Quote is escaped
    }
}
