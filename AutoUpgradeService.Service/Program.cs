using AutoUpgradeService.Service;
using Dzidek.Net.AutoUpgrade.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.OpenApi.Models;

var builder = Host.CreateApplicationBuilder(args);

//  Ensure it runs as a Windows Service
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "AutoUpgrade"; // Set your service name
});

//  Register Hosted Service for long-running background task
builder.Services.AddHostedService<WindowsBackgroundService>();

//  Add API & Swagger Support
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Service", Version = "v1" });
    options.OperationFilter<SwaggerFileOperationFilter>();
});

var host = builder.Build();
await host.RunAsync();

///  Background Service Class to Keep Service Alive
public class WindowsBackgroundService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var appBuilder = WebApplication.CreateBuilder();
        appBuilder.WebHost.UseKestrel();
        var app = appBuilder.Build();

        app.UseSwagger();
        app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Service v1"));

        app.MapGet("/", () => "Hello from Windows Service!");
        await app.RunAsync(stoppingToken);
    }
}