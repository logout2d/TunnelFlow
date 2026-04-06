using TunnelFlow.Core;
using TunnelFlow.Service;
using TunnelFlow.Service.Configuration;
using TunnelFlow.Service.Ipc;
using TunnelFlow.Service.Logging;
using TunnelFlow.Service.SingBox;
using TunnelFlow.Service.Tun;

var builder = Host.CreateApplicationBuilder(args);

var serviceLogPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "TunnelFlow",
    "logs",
    "service.log");

builder.Services.AddWindowsService(options =>
    options.ServiceName = "TunnelFlow");

builder.Logging.AddProvider(new ServiceFileLoggerProvider(serviceLogPath));

builder.Services.AddSingleton<ConfigStore>();
builder.Services.AddSingleton<SingBoxConfigBuilder>();
builder.Services.AddSingleton<ISingBoxManager, SingBoxManager>();
builder.Services.AddSingleton<ITunOrchestrator, WintunTunOrchestrator>();
builder.Services.AddSingleton<PipeServer>();
builder.Services.AddHostedService<OrchestratorService>();

var host = builder.Build();
host.Run();
