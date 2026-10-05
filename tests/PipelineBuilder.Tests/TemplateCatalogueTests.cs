using Microsoft.Extensions.DependencyInjection;
using PipelineBuilder.Core.Abstractions;
using PipelineBuilder.Core.DependencyInjection;
using PipelineBuilder.Core.Enums;
using PipelineBuilder.Core.Models;
using PipelineBuilder.Core.Services;
using PipelineBuilder.Web.State;
using Xunit;

namespace PipelineBuilder.Tests;

public class TemplateCatalogueTests
{
    private readonly IPipelineGeneratorService _generator =
        new ServiceCollection().AddPipelineBuilderCore().BuildServiceProvider().GetRequiredService<IPipelineGeneratorService>();

    public static IEnumerable<object[]> BuiltInTemplateIds() =>
        TemplateCatalogue.LoadBuiltIn().Select(t => new object[] { t.Id });

    [Fact]
    public void BuiltInCatalogueLoads()
    {
        var templates = TemplateCatalogue.LoadBuiltIn();

        Assert.True(templates.Count >= 5);
        Assert.All(templates, t =>
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Name));
            Assert.False(string.IsNullOrWhiteSpace(t.Description));
        });
    }

    [Theory]
    [MemberData(nameof(BuiltInTemplateIds))]
    public void EveryTemplateProducesAValidPipeline(string templateId)
    {
        var definition = WizardState.CreateDefault();

        Assert.True(new TemplateCatalogue().ApplyTo(templateId, definition));
        var result = _generator.Generate(definition); // throws if blocking errors or invalid YAML

        Assert.Equal(templateId, definition.TemplateId);
        Assert.Contains("- stage: Build", result.Yaml);
    }

    /// <summary>A deployment kind without a template cannot be chosen in the wizard.</summary>
    [Fact]
    public void EveryDeploymentKindHasATemplate()
    {
        var kinds = TemplateCatalogue.LoadBuiltIn().Select(t => t.Settings.DeploymentKind).OfType<DeploymentKind>().ToHashSet();

        Assert.All(Enum.GetValues<DeploymentKind>(), kind => Assert.Contains(kind, kinds));
    }

    [Fact]
    public void ApplyingATemplateOnlyChangesTheSettingsItDefines()
    {
        var definition = WizardState.CreateDefault();
        definition.Name = "keep-me";

        new TemplateCatalogue().ApplyTo("iis-onprem", definition);

        Assert.Equal("keep-me", definition.Name);
        Assert.Equal(DeploymentKind.Iis, definition.Deployment.Kind);
        Assert.Equal(new[] { "test", "prod" }, definition.Environments);
    }

    [Fact]
    public void CustomTemplatesFileAddsAndReplacesTemplates()
    {
        var path = Path.Combine(Path.GetTempPath(), $"templates-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """
            [
              // replaces the built-in template with the same id
              { "id": "iis-onprem", "name": "Our IIS", "description": "Company standard",
                "settings": { "deploymentKind": "Iis", "environments": ["dev", "prod"] } },
              { "id": "file-share", "name": "File share", "description": "Copy to a share",
                "settings": { "deploymentKind": "FileShare" } },
            ]
            """);
        try
        {
            var service = new TemplateCatalogue(new PipelineBuilderOptions { TemplatesFile = path });

            Assert.Equal("Our IIS", service.GetById("iis-onprem")!.Name);
            Assert.NotNull(service.GetById("file-share"));
            Assert.Equal(TemplateCatalogue.LoadBuiltIn().Count + 1, service.GetAllTemplates().Count);

            var definition = WizardState.CreateDefault();
            service.ApplyTo("iis-onprem", definition);
            Assert.Equal(new[] { "dev", "prod" }, definition.Environments);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("[{\"id\": \"\", \"name\": \"x\"}]")]
    [InlineData("[{\"id\": \"a\", \"name\": \"x\"}, {\"id\": \"A\", \"name\": \"y\"}]")]
    public void InvalidTemplatesFileGivesAClearError(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"templates-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content);
        try
        {
            Assert.Throws<InvalidDataException>(() => new TemplateCatalogue(new PipelineBuilderOptions { TemplatesFile = path }));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MissingTemplatesFileGivesAClearError()
    {
        var ex = Assert.Throws<InvalidDataException>(() =>
            new TemplateCatalogue(new PipelineBuilderOptions { TemplatesFile = "/no/such/templates.json" }));
        Assert.Contains("was not found", ex.Message);
    }
}
