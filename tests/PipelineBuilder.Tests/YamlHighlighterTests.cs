using PipelineBuilder.Web.Components.Steps;
using Xunit;

namespace PipelineBuilder.Tests;

/// <summary>The pipeline file as HTML: coloured, but never changed, and never trusted.</summary>
public class YamlHighlighterTests
{
    private static string Html(string yaml, string? previous = null) => YamlHighlighter.ToHtml(yaml, previous).Value;

    [Fact]
    public void CommentsKeysAndVariablesAreSetApart()
    {
        var html = Html("# Runs on main.\ntrigger:\n  - 'main'\n- stage: Deploy_${{ environment }}\n  target: '$(DEPLOY_PATH)'");

        Assert.Equal(
            "<span class=\"line comment\"># Runs on main.</span>\n" +
            "<span class=\"line\"><span class=\"k\">trigger</span>:</span>\n" +
            "<span class=\"line\">  - &#39;main&#39;</span>\n" +
            "<span class=\"line\">- <span class=\"k\">stage</span>: Deploy_<span class=\"x\">${{ environment }}</span></span>\n" +
            "<span class=\"line\">  <span class=\"k\">target</span>: &#39;<span class=\"x\">$(DEPLOY_PATH)</span>&#39;</span>",
            html);
    }

    [Fact]
    public void AConditionWrittenAsAKeyIsAnExpression()
    {
        var html = Html("    ${{ if in(environment, 'prod') }}:\n      condition: succeeded()");

        Assert.Contains("<span class=\"x\">${{ if in(environment, &#39;prod&#39;) }}</span>:", html);
        Assert.Contains("<span class=\"k\">condition</span>: succeeded()", html);
    }

    [Fact]
    public void AColonInsideAScriptDoesNotMakeAKey()
    {
        var html = Html("- bash: |\n    echo \"status: $(Build.BuildId)\"\n\n    exit 0\n  displayName: 'Say it'");
        var lines = html.Split('\n');

        Assert.Equal("<span class=\"line\">- <span class=\"k\">bash</span>: |</span>", lines[0]);
        Assert.DoesNotContain("class=\"k\"", lines[1]);
        Assert.Contains("<span class=\"x\">$(Build.BuildId)</span>", lines[1]);
        Assert.Equal("<span class=\"line\"></span>", lines[2]); // an empty line does not end the script
        Assert.DoesNotContain("class=\"k\"", lines[3]);
        Assert.Contains("<span class=\"k\">displayName</span>", lines[4]); // back at the step's own level
    }

    [Theory]
    [InlineData("# Pipeline: <script>alert(1)</script>")]
    [InlineData("name: \"><img src=x onerror=alert(1)>")]
    [InlineData("- bash: |\n    echo '</span><script>alert(1)</script>'")]
    [InlineData("  ${{ <b>bold</b> }}:")]
    public void NothingFromTheFileBecomesMarkup(string yaml)
    {
        var html = Html(yaml, previous: "something else");

        // The only tags are the highlighter's own.
        var tags = System.Text.RegularExpressions.Regex.Matches(html, "<[^>]*>").Select(m => m.Value).Distinct();
        Assert.All(tags, tag => Assert.Matches("^(</span>|<span class=\"(line|line comment|line changed|line comment changed|k|x)\">)$", tag));
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("<img", html);
    }

    [Fact]
    public void TheTextIsTheFileItself()
    {
        const string yaml = "# One\r\ntrigger:\r\n  branches: [ 'a & b', \"<c>\" ]\r\n\r\n- task: X@1";

        var text = System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(Html(yaml), "<[^>]*>", string.Empty));

        Assert.Equal(yaml.ReplaceLineEndings("\n"), text);
    }

    [Fact]
    public void OnlyLinesThatAreNewAreMarkedAsChanged()
    {
        const string before = "stages:\n- stage: Build\n  jobs:\n- stage: Deploy\n  jobs:";
        const string after = "stages:\n- stage: Build\n  jobs:\n  condition: always()\n- stage: Deploy\n  jobs:\n  jobs:";

        var lines = Html(after, before).Split('\n');

        Assert.Equal(new[] { 3, 6 }, Enumerable.Range(0, lines.Length).Where(i => lines[i].Contains("line changed")));
        Assert.DoesNotContain("changed", Html(after));          // shown for the first time: nothing is a change
        Assert.DoesNotContain("changed", Html(after, after));   // shown again unchanged
    }
}
