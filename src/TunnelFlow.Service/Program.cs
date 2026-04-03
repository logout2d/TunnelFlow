using TunnelFlow.Core;
using TunnelFlow.Service;
using TunnelFlow.Service.Configuration;
using TunnelFlow.Service.Ipc;
using TunnelFlow.Service.SingBox;
using TunnelFlow.Service.Tun;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
    options.ServiceName = "TunnelFlow");

builder.Services.AddSingleton<ConfigStore>();
builder.Services.AddSingleton<SingBoxConfigBuilder>();
builder.Services.AddSingleton<ISingBoxManager, SingBoxManager>();
builder.Services.AddSingleton<ITunOrchestrator, WintunTunOrchestrator>();
builder.Services.AddSingleton<PipeServer>();
builder.Services.AddHostedService<OrchestratorService>();

var host = builder.Build();
host.Run();
