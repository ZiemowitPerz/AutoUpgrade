using Dzidek.Net.AutoUpgrade.Common;
using Dzidek.Net.AutoUpgrade.Upgrader.FileSystemWatchers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Dzidek.Net.AutoUpgrade.Upgrader;

public static class UseAutoUpgradeUpgraderRegistration
{
    public static IHostBuilder UseAutoUpgradeUpgrader(this IHostBuilder hostBuilder,
        AutoUpgradeUpgraderConfiguration configuration)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File("logs/log.txt", rollingInterval: RollingInterval.Day, fileSizeLimitBytes: 1L * 1024 * 1024)
            .CreateLogger(); 
        return hostBuilder.UseAutoUpgrade(configuration.ServiceName, configuration.UpgraderNameSuffix, services =>
        {
            services
                .AddSingleton<IFileWatcher, FileWatcher>()
                .AddSingleton<AutoUpgradeUpgraderConfiguration>(x => configuration)
                .AddHostedService<UpgraderService>();
        }).UseSerilog();
    }
}