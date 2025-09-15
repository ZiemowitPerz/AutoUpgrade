using Microsoft.Extensions.Configuration;

namespace Dzidek.Net.AutoUpgrade.Upgrader;

/// <summary>
/// Fix issue of Windows service. Set absoloute path of logs instead of relative
/// </summary>
public static class SerilogConfigurationHelper
{
    public static IConfigurationRoot FixRelativeLogPaths(IConfiguration configuration)
    {
        var settings = new Dictionary<string, string?>();

        foreach (var item in configuration.AsEnumerable())
        {
            if (item.Value != null)
            {
                settings[item.Key] = item.Value;
            }
        }

        string appDirectory = AppDomain.CurrentDomain.BaseDirectory;

        var filePathKeys = settings.Keys
            .Where(key => key.Contains("Serilog:WriteTo") && key.EndsWith(":Args:path"))
            .ToList();

        foreach (var pathKey in filePathKeys)
        {
            var currentPath = settings[pathKey];

            if (!string.IsNullOrWhiteSpace(currentPath) && !Path.IsPathRooted(currentPath))
            {
                settings[pathKey] = Path.Combine(appDirectory, currentPath);
            }
        }

        var nestedFilePathKeys = settings.Keys
            .Where(key => key.Contains("Serilog:WriteTo") &&
                         key.Contains(":Args:configure:") &&
                         key.EndsWith(":Args:path"))
            .ToList();

        foreach (var pathKey in nestedFilePathKeys)
        {
            var currentPath = settings[pathKey];

            if (!string.IsNullOrWhiteSpace(currentPath) && !Path.IsPathRooted(currentPath))
            {
                settings[pathKey] = Path.Combine(appDirectory, currentPath);
            }
        }

        return new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
    }
}
