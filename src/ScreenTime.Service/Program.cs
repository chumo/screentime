using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ScreenTime.Service;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "ScreenTimeService";
});
builder.Services.AddHostedService<ScreenTimeWorker>();

var host = builder.Build();
host.Run();
