using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Web.State;
using Xunit;

namespace PipelineBuilder.Tests;

/// <summary>
/// The layering rules from docs/ARCHITECTURE.md (architecture goal M1). A failure here means a change
/// breaks the architecture, not that the test is wrong.
/// </summary>
public class ArchitectureTests
{
    private static readonly Assembly Core = typeof(PipelineDefinition).Assembly;
    private static readonly Assembly Web = typeof(WizardState).Assembly;

    /// <summary>The foundation layer: it may depend only on itself and the .NET libraries.</summary>
    private static readonly string[] Foundation =
    {
        "PipelineBuilder.Core.Enums",
        "PipelineBuilder.Core.Models",
        "PipelineBuilder.Core.Yaml",
    };

    /// <summary>Namespaces whose classes are wired by dependency injection.</summary>
    private static readonly string[] Wired =
    {
        "PipelineBuilder.Core.Services",
        "PipelineBuilder.Core.Deployment",
        "PipelineBuilder.Core.Validation",
        "PipelineBuilder.Core.Generators",
        "PipelineBuilder.Web.State",
        "PipelineBuilder.Web.Security",
    };

    public const int MaxConstructorDependencies = 5;

    /// <summary>
    /// Classes above the limit when the rule was introduced. Phase 2 (goals M5 and M7) removes them;
    /// lower a number when a class loses dependencies and delete the entry when it is within the limit.
    /// </summary>
    private static readonly Dictionary<string, int> KnownHubs = new(StringComparer.Ordinal)
    {
        ["PipelineGeneratorService"] = 6,
    };

    [Fact]
    public void CoreDoesNotReferenceAspNetCoreOrWeb()
    {
        var forbidden = Core.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(n => n.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
                        || n.StartsWith("PipelineBuilder.Web", StringComparison.Ordinal))
            .ToList();

        Assert.True(forbidden.Count == 0, $"PipelineBuilder.Core references {string.Join(", ", forbidden)}.");
    }

    [Fact]
    public void CoreSourceDoesNotUseWeb()
    {
        var offenders = SourceFiles(Path.Combine("src", "PipelineBuilder.Core"))
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"\b(Microsoft\.AspNetCore|PipelineBuilder\.Web)\b"))
            .Select(Relative)
            .ToList();

        Assert.True(offenders.Count == 0, $"Core files that use ASP.NET Core or Web: {string.Join(", ", offenders)}");
    }

    /// <summary>Goal M8: Core lets failures surface as exceptions; the host logs them once.</summary>
    [Fact]
    public void CoreDoesNotLogAndRethrow()
    {
        var offenders = SourceFiles(Path.Combine("src", "PipelineBuilder.Core"))
            .Where(f => HasCatchThatLogsAndRethrows(File.ReadAllText(f)))
            .Select(Relative)
            .ToList();

        Assert.True(offenders.Count == 0, $"Core files that catch, log and rethrow: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void FoundationDependsOnlyOnFoundation()
    {
        var violations = Core.GetTypes()
            .Where(t => InAny(t, Foundation))
            .SelectMany(t => SignatureTypes(t)
                .Where(used => used.Namespace?.StartsWith("PipelineBuilder", StringComparison.Ordinal) == true && !InAny(used, Foundation))
                .Select(used => $"{t.FullName} uses {used.FullName}"))
            .Distinct()
            .ToList();

        Assert.True(violations.Count == 0, "Foundation types depend on higher layers:\n" + string.Join("\n", violations));
    }

    [Fact]
    public void FoundationSourceDoesNotImportHigherLayers()
    {
        var offenders = Foundation
            .SelectMany(ns => SourceFiles(Path.Combine("src", "PipelineBuilder.Core", ns["PipelineBuilder.Core.".Length..])))
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"using\s+PipelineBuilder\.Core\.(Services|Generators|Abstractions|DependencyInjection)\b"))
            .Select(Relative)
            .ToList();

        Assert.True(offenders.Count == 0, $"Foundation files that import a higher layer: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void WebComponentsInjectOnlyCoreAbstractions()
    {
        var injections = Web.GetTypes()
            .Where(t => typeof(ComponentBase).IsAssignableFrom(t))
            .SelectMany(t => t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(p => p.IsDefined(typeof(InjectAttribute), inherit: true))
                .Select(p => (Component: t.Name, Service: p.PropertyType)))
            .ToList();

        Assert.NotEmpty(injections);
        var violations = injections
            .Where(i => i.Service.Namespace?.StartsWith("PipelineBuilder.Core", StringComparison.Ordinal) == true
                        && i.Service.Namespace != "PipelineBuilder.Core.Abstractions")
            .Select(i => $"{i.Component} injects {i.Service.FullName}")
            .ToList();

        Assert.True(violations.Count == 0, "Components must inject Core services through PipelineBuilder.Core.Abstractions:\n" + string.Join("\n", violations));
    }

    [Fact]
    public void ClassesHaveAtMostFiveConstructorDependencies()
    {
        var violations = new List<string>();
        foreach (var type in Core.GetTypes().Concat(Web.GetTypes()))
        {
            if (!type.IsClass || type.IsAbstract || !InAny(type, Wired) || type.IsDefined(typeof(CompilerGeneratedAttribute)))
                continue;

            var count = type.GetConstructors().Select(c => c.GetParameters().Length).DefaultIfEmpty(0).Max();
            var limit = KnownHubs.TryGetValue(type.Name, out var hub) ? hub : MaxConstructorDependencies;
            if (count > limit)
                violations.Add($"{type.FullName}: {count} constructor dependencies (limit {limit})");
            else if (KnownHubs.ContainsKey(type.Name) && count < limit)
                violations.Add($"{type.FullName} now has {count} dependencies: lower its KnownHubs entry to {Math.Max(count, MaxConstructorDependencies)} (or remove it at {MaxConstructorDependencies} or fewer)");
        }

        Assert.True(violations.Count == 0, string.Join("\n", violations));
    }

    private static bool HasCatchThatLogsAndRethrows(string source)
    {
        for (var at = source.IndexOf("catch", StringComparison.Ordinal); at >= 0; at = source.IndexOf("catch", at + 5, StringComparison.Ordinal))
        {
            var open = source.IndexOf('{', at);
            if (open < 0)
                break;

            // The catch block: from its opening brace to the matching closing brace.
            var depth = 0;
            var end = open;
            for (; end < source.Length; end++)
            {
                if (source[end] == '{') depth++;
                else if (source[end] == '}' && --depth == 0) break;
            }

            var block = source[open..Math.Min(end + 1, source.Length)];
            if (Regex.IsMatch(block, @"\.Log(Error|Warning|Critical)\(") && Regex.IsMatch(block, @"\bthrow;"))
                return true;
        }

        return false;
    }

    internal static IEnumerable<string> SourceFiles(string relativeFolder)
    {
        var folder = Path.Combine(RepositoryPaths.Root, relativeFolder);
        var separator = Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".razor", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
                        && !f.Contains($"{separator}bin{separator}", StringComparison.Ordinal));
    }

    internal static string Relative(string path) =>
        Path.GetRelativePath(RepositoryPaths.Root, path).Replace('\\', '/');

    private static bool InAny(Type type, string[] namespaces) =>
        type.Namespace is { } ns && namespaces.Any(n => ns == n || ns.StartsWith(n + ".", StringComparison.Ordinal));

    /// <summary>Every type a type exposes or stores: base type, interfaces, fields, properties and member signatures.</summary>
    private static IEnumerable<Type> SignatureTypes(Type type)
    {
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var direct = new List<Type>();
        if (type.BaseType != null) direct.Add(type.BaseType);
        direct.AddRange(type.GetInterfaces());
        direct.AddRange(type.GetFields(all).Select(f => f.FieldType));
        direct.AddRange(type.GetProperties(all).Select(p => p.PropertyType));
        foreach (var method in type.GetMethods(all))
        {
            direct.Add(method.ReturnType);
            direct.AddRange(method.GetParameters().Select(p => p.ParameterType));
        }
        foreach (var constructor in type.GetConstructors(all))
            direct.AddRange(constructor.GetParameters().Select(p => p.ParameterType));

        return direct.SelectMany(Expand);
    }

    private static IEnumerable<Type> Expand(Type type)
    {
        if (type.IsGenericParameter)
            yield break;
        if (type.HasElementType && type.GetElementType() is { } element)
        {
            foreach (var inner in Expand(element))
                yield return inner;
            yield break;
        }

        yield return type;
        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
                foreach (var inner in Expand(argument))
                    yield return inner;
        }
    }
}
