using EOQuoter.Data;
using EOQuoter.Web;
using EOQuoter.Web.Components;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Read-only viewer over both databases: the quoter DB (audit trail, jobs, DLQ) and the separate
// execution-tracking DB (sproc runs). DbContextFactory because Blazor components outlive scopes.
builder.Services.AddDbContextFactory<QuoterDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Quoter")));
builder.Services.AddDbContextFactory<ExecutionTrackingDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Tracking")));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseMiddleware<BasicAuthMiddleware>(); // internal-only: everything behind the gate
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
