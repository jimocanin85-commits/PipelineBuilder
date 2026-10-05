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
// Blazor would add its own frame-ancestors policy; UseSecurityHeaders already sets a stricter one for every response.
app.MapRazorComponents<App>().AddInteractiveServerRenderMode(options => options.ContentSecurityFrameAncestorsPolicy = null);
app.Run();
public partial class Program { }
