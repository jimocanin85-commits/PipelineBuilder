using Microsoft.Extensions.DependencyInjection;
using PipelineBuilder.Core.Abstractions;
using PipelineBuilder.Core.DependencyInjection;
using PipelineBuilder.Core.Services;
using PipelineBuilder.Web.State;
using Xunit;

namespace PipelineBuilder.Tests;

/// <summary>
/// Compares the whole generated pipeline with an approved copy in <c>Golden/</c>, so any change in output
/// shows up as a failing test and as a diff in the pull request (architecture goal M2).
/// </summary>
/// <remarks>
/// After an intended change, regenerate the files with <c>PIPELINEBUILDER_UPDATE_GOLDEN=1 dotnet test</c>
/// (or push a commit with <c>[update-golden]</c> in its message) and review the diff before merging.
/// </remarks>
public class GoldenFileTests
{
    public const string UpdateVariable = "PIPELINEBUILDER_UPDATE_GOLDEN";

    /// <summary>The wizard's default settings, without a template.</summary>
    public const string DefaultCase = "default";

    private static readonly IPipelineGeneratorService Generator =
        new ServiceCollection().AddPipelineBuilderCore().BuildServiceProvider().GetRequiredService<IPipelineGeneratorService>();

    /// <summary>The defaults plus every built-in template; each has a golden file named after it.</summary>
    private static IReadOnlyList<string> CaseNames() =>
        new[] { DefaultCase }.Concat(TemplateMarketplaceService.LoadBuiltIn().Select(t => t.Id)).ToList();

    public static TheoryData<string> Cases()
    {
        var cases = new TheoryData<string>();
        foreach (var name in CaseNames())
            cases.Add(name);
        return cases;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void GeneratedPipelineMatchesGoldenFile(string name)
    {
        var definition = WizardState.CreateDefault();
        if (name != DefaultCase)
            Assert.True(new TemplateMarketplaceService().ApplyTo(name, definition), $"Unknown template '{name}'.");

        var actual = Normalize(Generator.Generate(definition).Yaml);
        var path = Path.Combine(RepositoryPaths.GoldenFolder, name + ".yml");

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            Directory.CreateDirectory(RepositoryPaths.GoldenFolder);
            File.WriteAllText(path, actual);
            return;
        }

        Assert.True(File.Exists(path),
            $"Golden file {name}.yml is missing. Run the tests with {UpdateVariable}=1, or push a commit with [update-golden] in its message, then review the new file.");

        var expected = Normalize(File.ReadAllText(path));
        if (expected != actual)
            Assert.Fail(Describe(name, expected, actual));
    }

    [Fact]
    public void EveryGoldenFileHasACase()
    {
        if (!Directory.Exists(RepositoryPaths.GoldenFolder))
            return; // nothing generated yet; the theory above reports the missing files

        var names = CaseNames().ToHashSet(StringComparer.Ordinal);
        var orphans = Directory.EnumerateFiles(RepositoryPaths.GoldenFolder, "*.yml")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(file => !names.Contains(file!))
            .ToList();

        Assert.True(orphans.Count == 0, $"Golden files without a test case (delete them): {string.Join(", ", orphans)}");
    }

    private static string Normalize(string yaml) => yaml.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string Describe(string name, string expected, string actual)
    {
        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');
        var line = 0;
        while (line < expectedLines.Length && line < actualLines.Length && expectedLines[line] == actualLines[line])
            line++;

        string At(string[] lines) => line < lines.Length ? $"'{lines[line]}'" : "(end of file)";
        return $"Generated YAML for '{name}' differs from Golden/{name}.yml at line {line + 1}.\n" +
               $"  expected: {At(expectedLines)}\n" +
               $"  actual:   {At(actualLines)}\n" +
               $"If the change is intended, regenerate the golden files ({UpdateVariable}=1 or a commit with [update-golden]) and review the diff.";
    }
}
