using Digger.Data.Context;
using Digger.Services.Contracts;
using Digger.Services.Implementations;
using Microsoft.EntityFrameworkCore;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

var logger = LoggerFactory.Create(config => config.AddConsole()).CreateLogger("Program");
logger.LogInformation("Digger Worker initializing...");

builder.Services.AddHostedService<Worker>();
logger.LogInformation("Worker service registered");

builder.Services.AddHostedService<Cleaner>();
logger.LogInformation("Cleaner service registered");

logger.LogInformation("Configuring database context...");
builder.Services.AddDbContext<DiggerContext>(delegate (DbContextOptionsBuilder options)
{
    var connectionString = builder.Configuration.GetConnectionString("DiggerContext");
    logger.LogInformation("Using database connection string: {ConnectionString}", connectionString);
    options.UseNpgsql(connectionString);
}, 
ServiceLifetime.Singleton);

logger.LogInformation("Registering Yts service");
builder.Services.AddSingleton<IYtsService, YtsService>();

logger.LogInformation("Registering Transmission service");
builder.Services.AddSingleton<ITransmissionService, TransmissionService>();

logger.LogInformation("Building host...");
IHost host = builder.Build();

logger.LogInformation("Digger Worker started. Running...");
host.Run();