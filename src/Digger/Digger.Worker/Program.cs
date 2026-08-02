using Digger.Data.Context;
using Digger.Services.Contracts;
using Digger.Services.Implementations;
using HealthChecks.Sqlite;
using Microsoft.EntityFrameworkCore;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("DiggerContext");

var logger = LoggerFactory.Create(config => config.AddConsole()).CreateLogger("Program");
logger.LogInformation("Digger Worker initializing...");

builder.Services.AddHostedService<Worker>();
logger.LogInformation("Worker service registered");

builder.Services.AddHostedService<Cleaner>();
logger.LogInformation("Cleaner service registered");

logger.LogInformation("Configuring database context...");
builder.Services.AddDbContext<DiggerContext>(delegate (DbContextOptionsBuilder options)
{
    options.UseSqlite(connectionString);
}, 
ServiceLifetime.Scoped);

logger.LogInformation("Registering Yts service");
builder.Services.AddSingleton<IYtsService, YtsService>();

logger.LogInformation("Registering Transmission service");
builder.Services.AddSingleton<ITransmissionService, TransmissionService>();

logger.LogInformation("Configuring health checks");
builder.Services.AddHealthChecks()
    .AddSqlite(connectionString, name: "sqlite", tags: new[] { "db" });

logger.LogInformation("Building host...");
IHost host = builder.Build();

logger.LogInformation("Digger Worker started. Running...");
host.Run();