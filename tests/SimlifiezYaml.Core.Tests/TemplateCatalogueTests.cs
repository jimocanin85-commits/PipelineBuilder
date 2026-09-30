using Microsoft.Extensions.DependencyInjection;
using SimlifiezYaml.Core.Abstractions;
using SimlifiezYaml.Core.DependencyInjection;
using SimlifiezYaml.Core.Enums;
using SimlifiezYaml.Core.Models;
using SimlifiezYaml.Core.Services;
using SimlifiezYaml.Web.State;
using Xunit;

namespace SimlifiezYaml.Core.Tests;

public class TemplateCatalogueTests
{
    private readonly IPipelineGeneratorService _generator =
        new ServiceCollection().AddSimlifiezYamlCore().BuildServiceProvider().GetRequiredService<IPipelineGeneratorService>();

    public static IEnumerable<object[]> BuiltInTemplateIds() =>
        TemplateMarketplaceService.LoadBuiltIn().Select(t => new object[] { t.Id });

    [Fact]
    public void BuiltInCatalogueLoads()
    {
        var templates = TemplateMarketplaceService.LoadBuiltIn();

        Assert.True(templates.Count >= 7);
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

        Assert.True(new TemplateMarketplaceService().ApplyTo(templateId, definition));
        var result = _generator.Generate(definition); // throws if blocking errors or invalid YAML

        Assert.Equal(templateId, definition.TemplateId);
        Assert.Contains("- stage: Build", result.Yaml);
    }

    [Fact]
    public void ApplyingATemplateOnlyChangesTheSettingsItDefines()
    {
        var definition = WizardState.CreateDefault();
        definition.Name = "keep-me";

        new TemplateMarketplaceService().ApplyTo("iis-onprem", definition);

        Assert.Equal("keep-me", definition.Name);
        Assert.Equal(DeploymentKind.Iis, definition.Deployment.Kind);
        Assert.Equal(DeploymentTarget.OnPrem, definition.DeploymentTarget);
        Assert.Equal(new[] { "test", "prod" }, definition.Environments);
    }

    [Fact]
    public void CustomTemplatesFileAddsAndReplacesTemplates()
    {
        var path = Path.Combine(Path.GetTempPath(), $"templates-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """
            [
              // replaces the built-in template with the same id
              { "id": "iis-onprem", "name": "Our IIS", "description": "Company standard", "category": "Iis",
                "settings": { "deploymentKind": "Iis", "environments": ["dev", "prod"] } },
              { "id": "file-share", "name": "File share", "description": "Copy to a share", "category": "FileShare",
                "settings": { "deploymentTarget": "OnPrem", "deploymentKind": "FileShare" } },
            ]
            """);
        try
        {
            var service = new TemplateMarketplaceService(new SimlifiezYamlOptions { TemplatesFile = path });

            Assert.Equal("Our IIS", service.GetById("iis-onprem")!.Name);
            Assert.NotNull(service.GetById("file-share"));
            Assert.Equal(TemplateMarketplaceService.LoadBuiltIn().Count + 1, service.GetAllTemplates().Count);

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
            Assert.Throws<InvalidDataException>(() => new TemplateMarketplaceService(new SimlifiezYamlOptions { TemplatesFile = path }));
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
            new TemplateMarketplaceService(new SimlifiezYamlOptions { TemplatesFile = "/no/such/templates.json" }));
        Assert.Contains("was not found", ex.Message);
    }
}
