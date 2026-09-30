using System.Text;
using System.Text.RegularExpressions;

namespace SimlifiezYaml.Core.Yaml;

/// <summary>
/// Helpers for emitting Azure DevOps YAML fragments.
/// </summary>
/// <remarks>
/// Escaping rules:
/// <list type="bullet">
/// <item>Scripts are emitted as YAML literal block scalars (<c>|</c>), which need no escaping —
/// the script text reaches the agent exactly as written. Do NOT escape whole scripts.</item>
/// <item>User-supplied values that are placed <i>inside</i> a PowerShell script must be quoted
/// with <see cref="PsLiteral"/> so they cannot break out of the string.</item>
/// <item>Scalar values (task inputs, display names) are written as YAML single-quoted strings,
/// where the only escape is doubling the single quote. Backslashes are literal.</item>
/// </list>
/// </remarks>
public static class YamlBuilder
{
    private static readonly Regex NonIdentifierChars = new("[^A-Za-z0-9_]", RegexOptions.Compiled);

    public static string Indent(string content, int spaces = 2)
    {
        var pad = new string(' ', spaces);
        var lines = NormalizeNewLines(content).Split('\n');
        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                sb.Append('\n');
                continue;
            }
            sb.Append(pad).Append(line).Append('\n');
        }
        return sb.ToString().TrimEnd();
    }

    public static string Task(string taskName, IDictionary<string, string> inputs, string? displayName = null, int indent = 4,
        IReadOnlyDictionary<string, string>? env = null)
    {
        var pad = new string(' ', indent);
        var sb = new StringBuilder();
        sb.Append(pad).Append("- task: ").Append(taskName).Append('\n');
        if (!string.IsNullOrEmpty(displayName))
            sb.Append(pad).Append("  displayName: ").Append(YamlString(displayName)).Append('\n');
        sb.Append(pad).Append("  inputs:\n");
        foreach (var (key, value) in inputs)
            sb.Append(pad).Append("    ").Append(key).Append(": ").Append(YamlString(value)).Append('\n');
        AppendEnv(sb, pad, env);
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Step-level environment variables. Secret pipeline variables are not visible to scripts
    /// unless they are mapped like this, e.g. <c>SMTP_PASSWORD: $(SMTP_PASSWORD)</c>.
    /// </summary>
    private static void AppendEnv(StringBuilder sb, string pad, IReadOnlyDictionary<string, string>? env)
    {
        if (env is not { Count: > 0 }) return;
        sb.Append(pad).Append("  env:\n");
        foreach (var (key, value) in env)
            sb.Append(pad).Append("    ").Append(key).Append(": ").Append(YamlString(value)).Append('\n');
    }

    /// <summary>
    /// Emits a <c>powershell:</c> step. The script is written verbatim as a literal block scalar.
    /// </summary>
    public static string PowerShellStep(string script, string displayName, int indent = 4, string? condition = null,
        IReadOnlyDictionary<string, string>? env = null)
    {
        var pad = new string(' ', indent);
        var body = new string(' ', indent + 4);
        var sb = new StringBuilder();
        sb.Append(pad).Append("- powershell: |\n");
        foreach (var line in NormalizeNewLines(script).TrimEnd('\n').Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
                sb.Append('\n');
            else
                sb.Append(body).Append(line.TrimEnd()).Append('\n');
        }
        sb.Append(pad).Append("  displayName: ").Append(YamlString(displayName)).Append('\n');
        if (!string.IsNullOrWhiteSpace(condition))
            sb.Append(pad).Append("  condition: ").Append(condition).Append('\n');
        AppendEnv(sb, pad, env);
        return sb.ToString().TrimEnd();
    }

    public static string ScriptStep(string script, string displayName, int indent = 4)
    {
        var pad = new string(' ', indent);
        return $"{pad}- script: {YamlString(script)}\n{pad}  displayName: {YamlString(displayName)}";
    }

    /// <summary>
    /// Quotes a value as a YAML single-quoted scalar. In single-quoted YAML the only escape
    /// is <c>''</c> for a literal quote; backslashes are NOT escape characters.
    /// </summary>
    public static string YamlString(string? value)
    {
        var singleLine = NormalizeNewLines(value ?? string.Empty).Replace("\n", " ");
        return "'" + singleLine.Replace("'", "''") + "'";
    }

    /// <summary>
    /// Quotes a value as a PowerShell single-quoted (verbatim) string literal, e.g. for
    /// user-supplied paths, service names or URLs placed inside a generated script.
    /// Single-quoted PowerShell strings do not expand <c>$variables</c> or <c>$(subexpressions)</c>.
    /// Note that Azure DevOps macro syntax <c>$(Name)</c> is still expanded by the agent before
    /// PowerShell runs, which is the intended way to reference pipeline variables.
    /// </summary>
    public static string PsLiteral(string? value)
    {
        var singleLine = NormalizeNewLines(value ?? string.Empty).Replace("\n", " ");
        // PowerShell also treats the typographic quotes ‘ ’ ‚ ‛ as single quotes.
        var escaped = Regex.Replace(singleLine, "['‘’‚‛]", m => m.Value + m.Value);
        return "'" + escaped + "'";
    }

    /// <summary>
    /// Converts a name (for example an environment such as <c>pre-prod</c>) into a valid
    /// Azure DevOps stage or job identifier, which may contain only letters, digits and underscores.
    /// </summary>
    public static string ToIdentifier(string name) =>
        NonIdentifierChars.Replace(name ?? string.Empty, "_");

    private static string NormalizeNewLines(string value) =>
        value.Replace("\r\n", "\n").Replace('\r', '\n');
}
