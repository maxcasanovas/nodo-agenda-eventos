using Nodo.Api.Infrastructure;
using Nodo.Api.OpenApi;
using Nodo.BuildingBlocks.Application;
using Nodo.BuildingBlocks.Infrastructure;
using Nodo.External.Simulated;
using Nodo.EventLifecycle.Application;
using Nodo.EventLifecycle.Infrastructure;
using Nodo.HallSchedule.Application;
using Nodo.HallSchedule.Infrastructure;
using Nodo.Quotes.Application;
using Nodo.Quotes.Infrastructure;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddNodoOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddHealthChecks();

builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins("http://localhost:5173")
    .AllowAnyHeader()
    .AllowAnyMethod()));

// One DemoClock holds the demo's time travel. Use cases see it through a scoped clock, which can add an offset that only
// that scope sees (the invariant probes use it to simulate time passing without touching the demo).
builder.Services.AddSingleton<DemoClock>();
builder.Services.AddScoped<ClockOffset>();
builder.Services.AddScoped<IClock, ScopedClock>();

builder.Services.AddMediatR(cfg =>
{
    // Optional: the app works without a license key.
    cfg.LicenseKey = builder.Configuration["MediatR:LicenseKey"];
    cfg.RegisterServicesFromAssemblies(
        typeof(Program).Assembly,
        typeof(Nodo.Quotes.Application.AssemblyMarker).Assembly,
        typeof(Nodo.EventLifecycle.Application.AssemblyMarker).Assembly,
        typeof(Nodo.HallSchedule.Application.AssemblyMarker).Assembly);
    cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
    cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
});

builder.Services.AddQuotesApplication();
builder.Services.AddEventLifecycleApplication();
builder.Services.AddHallScheduleApplication();

var connectionString = builder.Configuration.GetConnectionString("Nodo")
    ?? throw new InvalidOperationException("ConnectionStrings:Nodo is not configured.");

builder.Services.AddMessagingInfrastructure(
    connectionString,
    [
        typeof(Nodo.HallSchedule.Contracts.SlotReleasedIntegrationEvent).Assembly,
        typeof(Nodo.Quotes.Contracts.QuoteIssuedIntegrationEvent).Assembly,
        typeof(Nodo.EventLifecycle.Contracts.EventRequestCreatedIntegrationEvent).Assembly
    ],
    ["hall_schedule", "quotes", "event_lifecycle"]);
builder.Services.AddHallScheduleInfrastructure(connectionString);
builder.Services.AddQuotesInfrastructure(connectionString);
builder.Services.AddEventLifecycleInfrastructure(connectionString);

// The simulated external systems (in memory) and the way the payment gateway reaches our webhook: over HTTP, against this API.
builder.Services.AddExternalSimulated(
    typeof(Nodo.Quotes.Contracts.QuoteIssuedIntegrationEvent).Assembly,
    typeof(Nodo.EventLifecycle.Contracts.EventRequestCreatedIntegrationEvent).Assembly,
    typeof(Nodo.HallSchedule.Contracts.SlotReleasedIntegrationEvent).Assembly);
builder.Services.AddHttpClient("self", client =>
    client.BaseAddress = new Uri(builder.Configuration["Api:SelfBaseUrl"] ?? "http://localhost:5080"));
builder.Services.AddSingleton<Nodo.Api.Invariants.InvariantProbeRunner>();

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
    await app.MigrateDatabasesAsync();

app.UseExceptionHandler();
app.UseCors();

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Docker"))
{
    app.MapOpenApi();
    app.MapScalarApiReference("/scalar", options => options
        .WithTitle("Nodo · Agenda y Eventos API")
        .WithTheme(ScalarTheme.DeepSpace));
}

app.MapHealthChecks("/health");
app.MapControllers();

app.Run();
