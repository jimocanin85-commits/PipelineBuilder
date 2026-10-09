using PipelineBuilder.Core.DependencyInjection;
using PipelineBuilder.Web.Components;
using PipelineBuilder.Web.Security;
using PipelineBuilder.Web.State;
using PipelineBuilder.Web.Storage;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddPipelineBuilderCore(options =>
    options.TemplatesFile = builder.Configuration["PipelineBuilder:TemplatesFile"]);
builder.Services.AddScoped<WizardState>();
// The team's saved pipelines and the activity log, when a database is set up (the install script does it).
var database = builder.Configuration.GetConnectionString(SqlPipelineStore.ConnectionStringName);
builder.Services.AddSingleton<IPipelineStore>(string.IsNullOrWhiteSpace(database) ? new NoPipelineStore() : new SqlPipelineStore(database));
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
