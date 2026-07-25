using Digger.Data.Context;
using Digger.Services.Contracts;
using Digger.Services.Implementations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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

// Bootstrap: create the digger database if it doesn't exist yet
logger.LogInformation("Bootstrapping database...");
var connString = builder.Configuration.GetConnectionString("DiggerContext");
var connBuilder = new NpgsqlConnectionStringBuilder(connString);
var databaseName = connBuilder.Database; // "digger"

// Connect to the maintenance database (always exists) to create our database
connBuilder.Database = "postgres";

using (var bootstrapConn = new NpgsqlConnection(connBuilder.ConnectionString))
{
    bootstrapConn.Open();
    using (var cmd = bootstrapConn.CreateCommand())
    {
        cmd.CommandText = "SELECT 1 FROM pg_database WHERE datname = @db";
        cmd.Parameters.AddWithValue("db", databaseName);
        var exists = cmd.ExecuteScalar();
        if (exists == null)
        {
            cmd.CommandText = $"CREATE DATABASE \"{databaseName}\"";
            cmd.Parameters.Clear();
            cmd.ExecuteNonQuery();
            logger.LogInformation("Created database: {Database}", databaseName);
        }
        else
        {
            logger.LogInformation("Database already exists: {Database}", databaseName);
        }
    }
}

logger.LogInformation("Digger Worker started. Running...");
host.Run();