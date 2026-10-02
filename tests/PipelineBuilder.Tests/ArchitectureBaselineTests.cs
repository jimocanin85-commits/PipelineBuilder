using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using PipelineBuilder.Core.Models;
using Xunit;

namespace PipelineBuilder.Tests;

/// <summary>
/// Ratchets for the architecture goals that are not met yet (see docs/ARCHITECTURE.md for the rules).
/// Each number is today's value: the test fails if it grows, and also when it shrinks, so the baseline
/// is lowered in the same pull request that improves it. Never raise a baseline.
/// </summary>
public class ArchitectureBaselineTests
{
    /// <summary>
    /// M5: files that branch on a deployment kind (<c>case</c>, <c>==</c>, <c>!=</c>, <c>is</c>, <c>or</c> or a switch arm).
    /// Met: only the wizard's field list is left; everything else lives in one handler per kind.
    /// </summary>
    public const int FilesBranchingOnDeploymentKind = 1;

    /// <summary>M9: public setters on <see cref="PipelineDefinition"/>. Target: 0.</summary>
    public const int SettablePropertiesOnPipelineDefinition = 25;

    /// <summary>M12: Core interfaces with exactly one implementation. Target: only the facade, handlers and rules.</summary>
    public const int SingleImplementationInterfaces = 16;

    private static readonly Regex BranchOnDeploymentKind =
        new(@"(case\s+|==\s*|!=\s*|\bis\s+|\bor\s+)DeploymentKind\.[A-Z]|DeploymentKind\.[A-Z]\w*\s*=>", RegexOptions.Compiled);

    [Fact]
    public void FilesThatBranchOnDeploymentKind()
    {
        var files = ArchitectureTests.SourceFiles("src")
            .Where(f => BranchOnDeploymentKind.IsMatch(File.ReadAllText(f)))
            .Select(ArchitectureTests.Relative)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        Ratchet("Files that branch on DeploymentKind (M5)", nameof(FilesBranchingOnDeploymentKind),
            files.Count, FilesBranchingOnDeploymentKind, files);
    }

    [Fact]
    public void SettablePropertiesOnTheDomainModel()
    {
        var settable = typeof(PipelineDefinition).GetProperties()
            .Where(p => p.SetMethod is { IsPublic: true } setter
                        && !setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit)))
            .Select(p => p.Name)
            .ToList();

        Ratchet("Public setters on PipelineDefinition (M9)", nameof(SettablePropertiesOnPipelineDefinition),
            settable.Count, SettablePropertiesOnPipelineDefinition, settable);
    }

    [Fact]
    public void InterfacesWithASingleImplementation()
    {
        var types = typeof(PipelineDefinition).Assembly.GetTypes();
        var single = types
            .Where(t => t.IsInterface && t.IsPublic && t.Namespace?.StartsWith("PipelineBuilder.Core", StringComparison.Ordinal) == true)
            .Where(i => types.Count(t => t.IsClass && !t.IsAbstract && i.IsAssignableFrom(t)) == 1)
            .Select(i => i.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Ratchet("Core interfaces with one implementation (M12)", nameof(SingleImplementationInterfaces),
            single.Count, SingleImplementationInterfaces, single);
    }

    private static void Ratchet(string what, string constant, int actual, int baseline, IEnumerable<string> items)
    {
        var list = string.Join(", ", items);
        Assert.True(actual <= baseline,
            $"{what}: {actual}, baseline {baseline}. The architecture got worse; see docs/ARCHITECTURE.md. Items: {list}");
        Assert.True(actual >= baseline,
            $"{what}: {actual}, baseline {baseline}. Good, it improved: lower {nameof(ArchitectureBaselineTests)}.{constant} to {actual}. Items: {list}");
    }
}
