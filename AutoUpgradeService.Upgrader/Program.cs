using Dzidek.Net.AutoUpgrade.Upgrader;
using Microsoft.Extensions.Hosting.WindowsServices;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : default
});

builder.Host
    .UseAutoUpgradeUpgrader(builder.Configuration);
var app = builder.Build();
app.Run();