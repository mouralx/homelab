using Digger.Data.Context;
using Digger.Services.Contracts;
using Digger.Services.Implementations;
using Microsoft.EntityFrameworkCore;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHostedService<Worker>();

builder.Services.AddDbContext<DiggerContext>(delegate (DbContextOptionsBuilder options)
{
    options.UseSqlite(builder.Configuration.GetConnectionString("DiggerContext"));
}, 
ServiceLifetime.Singleton);

builder.Services.AddSingleton<IYtsService, YtsService>();

builder.Services.AddSingleton<ITransmissionService, TransmissionService>();

IHost host = builder.Build();

host.Run();