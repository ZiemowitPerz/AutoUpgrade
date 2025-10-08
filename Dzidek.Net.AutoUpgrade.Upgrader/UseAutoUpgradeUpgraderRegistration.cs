using Dzidek.Net.AutoUpgrade.Common;
using Dzidek.Net.AutoUpgrade.Upgrader.FileSystemWatchers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Dzidek.Net.AutoUpgrade.Upgrader;

public static class UseAutoUpgradeUpgraderRegistration
{
    public static IHostBuilder UseAutoUpgradeUpgrader(this IHostBuilder hostBuilder, IConfiguration configuration)
    {
        AutoUpgradeUpgraderConfiguration autoUpgradeUpgraderConfiguration = configuration.GetSection("AutoUpgrade").Get<AutoUpgradeUpgraderConfiguration>()!;
        
        configuration = SerilogConfigurationHelper.FixRelativeLogPaths(configuration);
        Log.Logger = new LoggerConfiguration().ReadFrom.Configuration(configuration).CreateLogger();

        return hostBuilder.UseAutoUpgrade(autoUpgradeUpgraderConfiguration.ServiceName, autoUpgradeUpgraderConfiguration.UpgraderNameSuffix, services =>
        {
            services
                .AddSingleton<IFileWatcher, FileWatcher>()
                .AddSingleton<AutoUpgradeUpgraderConfiguration>(x => autoUpgradeUpgraderConfiguration)
                .AddHostedService<UpgraderService>();
        }).UseSerilog();
    }
}