using System.Diagnostics;
using System.IO.Compression;
using System.Management;
using System.ServiceProcess;
using Dzidek.Net.AutoUpgrade.Common;
using Dzidek.Net.AutoUpgrade.Upgrader.FileSystemWatchers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dzidek.Net.AutoUpgrade.Upgrader;

public sealed class UpgraderService : IHostedService
{
    private readonly IFileWatcher _fileWatcher;
    private readonly AutoUpgradeUpgraderConfiguration _configuration;
    private readonly ILogger<UpgraderService> _logger;

    public UpgraderService(IFileWatcher fileWatcher, AutoUpgradeUpgraderConfiguration configuration, ILogger<UpgraderService> logger)
    {
        _fileWatcher = fileWatcher;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
       string binPath = _configuration.ServicePath;
        
        string newVersionPath = Path.Combine(binPath, "NewVersion");
        if (!IsProperConfiguration(binPath))
        {
            throw new ArgumentException("Invalid service path");
        }
        await Upgrade(newVersionPath, binPath, _configuration.ServiceOldVersionsPath);
        _fileWatcher.OnStart(
            new FileSystemWatcherConfiguration()
            {
                Enabled = true,
                Path = newVersionPath
            },
            new FileSystemWatcherActions()
            {
                Created = (sender, args) => { Upgrade(newVersionPath, binPath, _configuration.ServiceOldVersionsPath); }
            });
    }

    private bool IsProperConfiguration(string binPath)
    {
        return Directory.GetFiles(binPath).Any() && Directory.GetFiles(GetServicePath()).Any();
    }

    private string GetServicePath()
    {
        ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Service");
        ManagementObjectCollection collection = searcher.Get();

        foreach (ManagementObject obj in collection)
        {    
            string name = obj["Name"] as string;
            string pathName = obj["PathName"] as string;
            if (name == GetServiceName() && !string.IsNullOrEmpty(pathName))
            {
                return Path.GetDirectoryName(pathName);
            }
        }

        throw new ArgumentException("Invalid service name");
    }

    private async Task Upgrade(string newVersionPath, string binPath, string? serviceOldVersionsPath)
    {
        if (!Directory.Exists(newVersionPath))
        {
            Directory.CreateDirectory(newVersionPath);
        }

        await StopAction(TimeSpan.FromSeconds(20));

        await UnzipAndCopyFiles(newVersionPath, binPath, serviceOldVersionsPath);

        await RepeatAsync(StartAction);
    }

    private string GetServiceName()
    {
        return ServiceName.GetServiceName(_configuration.ServiceName, _configuration.ServiceNameSuffix);
    }

    private async Task RepeatAsync(Func<TimeSpan, Task> func)
	{
		TimeSpan time = TimeSpan.FromSeconds(10);
		int i = 4;
		while (i >= 0)
		{
			await func(time);
			time *= 2;
			i--;
		}
	}

	private async Task StartAction(TimeSpan wait)
    {
        _logger.LogDebug($"11 StartAction waitTime = {wait.TotalSeconds.ToString()}");
        ServiceController appDriver = new ServiceController(GetServiceName());
        if (appDriver.Status == ServiceControllerStatus.Stopped)
        {
            try
            {
                appDriver.Start();
                appDriver.WaitForStatus(ServiceControllerStatus.Running, wait);
            }
            catch
            {
                _logger.LogError($"Failed to start service {GetServiceName()}");
            }
        }
    }

	private async Task StopAction(TimeSpan wait)
	{
		string serviceName = GetServiceName();

        _logger.LogDebug($"toping service : {serviceName}");
        try
		{
			using ServiceController appDriver = new ServiceController(serviceName);

            if (appDriver.Status == ServiceControllerStatus.Stopped || appDriver.Status == ServiceControllerStatus.StopPending)
			{
                _logger.LogDebug($"Service '{serviceName}' is already pending stop or is stopped");
                return;
			}

			int processId = GetServiceProcessId(serviceName);
			var stopwatch = Stopwatch.StartNew();

            _logger.LogDebug($"Gracefully stopping service '{serviceName}' processId = {processId}");
            appDriver.Stop();
            appDriver.WaitForStatus(ServiceControllerStatus.Stopped, wait);

			if (processId != -1)
            {
                await TryKillProcess(processId, wait - stopwatch.Elapsed);
			}
            return;
        }
		catch (InvalidOperationException ex) when (ex.InnerException is System.ComponentModel.Win32Exception win32Ex && win32Ex.NativeErrorCode == 1062)
		{
            return;
		}
		catch (Exception ex)
		{
            return;
		}
	}

	private int GetServiceProcessId(string serviceName)
	{
		try
		{
			using (var searcher = new ManagementObjectSearcher($"SELECT ProcessId FROM Win32_Service WHERE Name = '{serviceName}'"))
			{
				foreach (ManagementObject obj in searcher.Get())
				{
					return Convert.ToInt32(obj["ProcessId"]);
				}
			}
		}
		catch
		{
			return -1;
		}
		return -1;
	}

	private async Task TryKillProcess(int processId, TimeSpan remainingWait)
	{
		try
		{
            Process process = Process.GetProcessById(processId);
			if (!process.HasExited)
			{
				if (remainingWait > TimeSpan.Zero)
                    _logger.LogDebug($"Awaiting for processId = {processId} to Exit; wait {remainingWait.TotalSeconds.ToString()} sec");
					process.WaitForExit((int)remainingWait.TotalMilliseconds);

				if (!process.HasExited)
				{
                    _logger.LogDebug($"Process refuses to exit: Killing processId = {processId}");
                    process.Kill();
					process.WaitForExit();
				}
            }
            return;
        }
		catch (ArgumentException ex)
		{
            _logger.LogDebug($"Error while trying to kill processid = {processId}: {ex.Message}");
            return;
		}

    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _fileWatcher.OnStop();
        return Task.CompletedTask;
    }

    private async Task WaitForFileUnlock(string filePath, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            try
            {
                using (FileStream stream = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    return;
                }
            }
            catch (IOException)
            {
                await Task.Delay(200);
            }
        }

        throw new System.ServiceProcess.TimeoutException($"File '{filePath}' is still locked after {timeout.TotalSeconds} seconds.");
    }

    private async Task UnzipAndCopyFiles(string sourcePath, string servicePath, string? serviceOldVersionsPath)
    {
        string[] files = Directory.GetFiles(sourcePath);

        if (files.Length > 0)
        {
            await ZipOldVersion(files, servicePath, serviceOldVersionsPath);
        }
        await ReplaceFilesFromZip(sourcePath, servicePath, files);

        return;
    }

    private async Task ReplaceFilesFromZip(string sourcePath, string servicePath, string[] files)
    {
        foreach (string file in files)
        {
            _logger.LogDebug("Starting unzipping '{0}'", file);
            string fileName = Path.GetFileName(file);
            string zipPath = Path.Combine(sourcePath, fileName);

            await WaitForFileUnlock(zipPath, TimeSpan.FromSeconds(5));

            ZipFile.ExtractToDirectory(zipPath, servicePath, true);
            File.Delete(file);
            _logger.LogInformation("The new version has been copied '{0}'", file);
        }
    }

    private async Task ZipOldVersion(string[] files, string sourcePath, string? destPath)
    {
        if (destPath != null)
        {
            if (!Directory.Exists(destPath))
            {
                Directory.CreateDirectory(destPath);
            }

            foreach (string file in files)
            {
                string fileName = Path.GetFileName(file);
                string zipPath = Path.Combine(sourcePath, fileName);
                await WaitForFileUnlock(zipPath, TimeSpan.FromSeconds(5));
            }

            string destFile = Path.Combine(destPath, $"{DateTime.UtcNow.ToString("o").Replace(":","_").Replace(".","_")}.zip");
            _logger.LogDebug("Starting zipping '{0}'", sourcePath);
            ZipFile.CreateFromDirectory(sourcePath, destFile);
            _logger.LogInformation("The old version has been copied '{0}'", destFile);
        }
        return;
    }
}