using PipelineBuilder.Core.DependencyInjection;
using PipelineBuilder.Web.Components;
using PipelineBuilder.Web.State;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddPipelineBuilderCore(options =>
    options.TemplatesFile = builder.Configuration["PipelineBuilder:TemplatesFile"]);
builder.Services.AddScoped<WizardState>();

var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
public partial class Program { }
