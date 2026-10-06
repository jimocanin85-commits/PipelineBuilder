using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;

namespace PipelineBuilder.Web.Components.Steps;

/// <summary>
/// The pipeline file as HTML: one element per line, with comments, keys and variables set apart, so
/// the stylesheet can number and colour them. The text itself is unchanged.
/// <para>
/// The file holds what the user typed (names, paths, scripts), so every piece of it is encoded
/// before it is put into the markup. Nothing from the file is ever written as HTML.
/// </para>
/// </summary>
public static class YamlHighlighter
{
    /// <summary>A template expression <c>${{ ... }}</c> or a variable <c>$(Name)</c>.</summary>
    private static readonly Regex Expression = new(@"(\$\{\{.*?\}\}|\$\([A-Za-z_][\w.]*\))", RegexOptions.Compiled);

    /// <summary>A line that starts with a key: <c>name:</c>, <c>- task:</c> or <c>${{ if ... }}:</c>.</summary>
    private static readonly Regex KeyLine = new(@"^(\s*(?:-\s)?)(\$\{\{.*\}\}|[A-Za-z_][\w.-]*)(:)(\s.*|)$", RegexOptions.Compiled);

    /// <param name="yaml">The file to show.</param>
    /// <param name="previous">The file as it was shown last. Lines that are not in it are marked <c>changed</c>.</param>
    public static MarkupString ToHtml(string yaml, string? previous = null)
    {
        ArgumentNullException.ThrowIfNull(yaml);

        // How often each line occurred before; a line is new when the old file has none of it left.
        var unmatched = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in previous == null ? Array.Empty<string>() : Lines(previous))
            unmatched[line] = unmatched.GetValueOrDefault(line) + 1;

        var html = new StringBuilder(yaml.Length * 2);
        var scriptIndent = -1; // inside a script ("bash: |"), a colon does not make a key
        var first = true;
        foreach (var line in Lines(yaml))
        {
            if (!first) html.Append('\n');
            first = false;

            var trimmed = line.Trim();
            var indent = line.Length - line.TrimStart().Length;
            var css = "line";
            string content;
            if (scriptIndent >= 0 && (trimmed.Length == 0 || indent > scriptIndent))
            {
                content = WithExpressions(line);
            }
            else
            {
                scriptIndent = -1;
                var key = KeyLine.Match(line);
                if (trimmed.StartsWith('#'))
                {
                    css = "line comment";
                    content = Encode(line);
                }
                else if (key.Success)
                {
                    var name = key.Groups[2].Value;
                    var value = key.Groups[4].Value;
                    content = Encode(key.Groups[1].Value)
                              + $"<span class=\"{(name[0] == '$' ? "x" : "k")}\">{Encode(name)}</span>:"
                              + WithExpressions(value);
                    // The script's lines are indented deeper than the key itself ("- bash" starts at the b).
                    if (value.Trim() == "|") scriptIndent = key.Groups[1].Length;
                }
                else
                {
                    content = WithExpressions(line);
                }
            }

            if (previous != null)
            {
                if (unmatched.GetValueOrDefault(line) > 0) unmatched[line]--;
                else css += " changed";
            }
            html.Append("<span class=\"").Append(css).Append("\">").Append(content).Append("</span>");
        }
        return new MarkupString(html.ToString());
    }

    private static string[] Lines(string text) => text.ReplaceLineEndings("\n").Split('\n');

    /// <summary>The text, encoded, with its expressions and variables wrapped. Split keeps the matches at the odd places.</summary>
    private static string WithExpressions(string text) =>
        string.Concat(Expression.Split(text).Select((part, i) => i % 2 == 1 ? $"<span class=\"x\">{Encode(part)}</span>" : Encode(part)));

    private static string Encode(string text) => WebUtility.HtmlEncode(text);
}
