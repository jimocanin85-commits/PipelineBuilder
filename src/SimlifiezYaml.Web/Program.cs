using SimlifiezYaml.Core.DependencyInjection;
using SimlifiezYaml.Web.Components;
using SimlifiezYaml.Web.State;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSimlifiezYamlCore();
builder.Services.AddScoped<WizardState>();

var app = builder.Build();
if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error", createScopeForErrors: true);

app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();
public partial class Program { }
