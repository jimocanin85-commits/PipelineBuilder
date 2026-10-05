using PipelineBuilder.Core.DependencyInjection;
using PipelineBuilder.Web.Components;
using PipelineBuilder.Web.Security;
using PipelineBuilder.Web.State;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddPipelineBuilderCore(options =>
    options.TemplatesFile = builder.Configuration["PipelineBuilder:TemplatesFile"]);
builder.Services.AddScoped<WizardState>();
var windowsLogin = builder.AddPipelineBuilderAuthentication();

var app = builder.Build();
app.UseSecurityHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
    app.UseHttpsRedirection();
}

if (windowsLogin)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
public partial class Program { }
