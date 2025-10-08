namespace Dzidek.Net.AutoUpgrade.Upgrader;

// ReSharper disable once ClassNeverInstantiated.Global
public sealed class AutoUpgradeUpgraderConfiguration
{
    public string ServiceName { get; init; }
    public string ServicePath { get; init; }
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public string? ServiceOldVersionsPath { get; init; }
    public string UpgraderNameSuffix { get; init; } = "Upgrader";
    public string ServiceNameSuffix { get; init; } = "Service";

#pragma warning disable CS8618
    public AutoUpgradeUpgraderConfiguration()
#pragma warning restore CS8618
    {
        
    }
    public AutoUpgradeUpgraderConfiguration(string serviceName, string servicePath):this()
    {
        ServiceName = serviceName;
        ServicePath = servicePath;
    }

    public string GetNewVersionPath() => Path.Combine(ServicePath, "NewVersion");
}
