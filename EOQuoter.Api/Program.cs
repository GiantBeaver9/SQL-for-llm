using EOQuoter.Api;
using EOQuoter.Api.Endpoints;
using EOQuoter.Api.Messaging;
using EOQuoter.Data;
using EOQuoter.Data.Idempotency;
using EOQuoter.Data.Messaging;
using EOQuoter.Data.Seed;
using EOQuoter.Domain;
using EOQuoter.Intake;
using EOQuoter.Underwriting;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Ops logging (Serilog) is the TECHNICAL stream — request traces, worker execution, errors.
// The DOMAIN decision trail lives in audit_events / rating ledgers, persisted and queryable.
// Two streams, two audiences, deliberately separate.
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddDbContext<QuoterDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Quoter")));
builder.Services.AddDbContext<ExecutionTrackingDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Tracking")));

builder.Services.AddScoped<IRatingDataSource, SqlRatingDataSource>();
builder.Services.AddScoped<IEligibilityDataSource, SqlEligibilityDataSource>();
builder.Services.AddScoped<EligibilityEngine>();
builder.Services.AddScoped<RatingEngine>();

builder.Services.AddSingleton<ISubmissionParser, AcordSubmissionParser>();
// The degraded-input seam: zero IDegradedInputHandler registrations ship. Unparseable → human.
builder.Services.AddScoped<IntakePipeline>();

builder.Services.AddScoped<IdempotencyService>();
builder.Services.AddScoped<IdempotentHandler>();

builder.Services.AddScoped<IOutboxConsumer, PolicyBoundConsumer>();
builder.Services.AddScoped<OutboxProcessor>();
builder.Services.AddHostedService<OutboxDispatcherService>();

var app = builder.Build();

// Migrate both databases, install the checked-in sprocs, seed the effective-dated reference data.
using (var scope = app.Services.CreateScope())
{
    var quoter = scope.ServiceProvider.GetRequiredService<QuoterDbContext>();
    await quoter.Database.MigrateAsync();
    await SqlObjectInstaller.ApplyAsync(quoter);
    await DbSeeder.SeedAsync(quoter);

    var tracking = scope.ServiceProvider.GetRequiredService<ExecutionTrackingDbContext>();
    await tracking.Database.MigrateAsync();
}

app.UseSerilogRequestLogging();

SubmissionEndpoints.Map(app);
PolicyEndpoints.Map(app);
JobEndpoints.Map(app);

app.MapGet("/", () => Results.Ok(new
{
    service = "EOQuoter.Api",
    pipeline = "submission → intake → eligibility → rating → quote → bind → issue → bordereau",
}));

app.Run();

public partial class Program; // exposes the entry point to WebApplicationFactory-style tests
