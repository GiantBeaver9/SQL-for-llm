using EOQuoter.Data;
using EOQuoter.Jobs;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();
builder.Logging.ClearProviders();
builder.Services.AddSerilog();

builder.Services.AddDbContext<QuoterDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Quoter")));
// The tracking context gets its own connection string — separate DB on purpose (see SprocRunner).
builder.Services.AddDbContext<ExecutionTrackingDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Tracking")));

builder.Services.AddScoped<SprocRunner>();
builder.Services.AddHostedService<JobWorker>();

var host = builder.Build();

// Defensive: migrate on start so the worker can run standalone (the API also migrates).
using (var scope = host.Services.CreateScope())
{
    var quoter = scope.ServiceProvider.GetRequiredService<QuoterDbContext>();
    await quoter.Database.MigrateAsync();
    await SqlObjectInstaller.ApplyAsync(quoter);
    var tracking = scope.ServiceProvider.GetRequiredService<ExecutionTrackingDbContext>();
    await tracking.Database.MigrateAsync();
}

host.Run();
