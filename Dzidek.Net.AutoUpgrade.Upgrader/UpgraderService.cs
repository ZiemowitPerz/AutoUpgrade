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

    public Task StartAsync(CancellationToken cancellationToken)
    {
       string binPath = _configuration.ServicePath;
        
        string newVersionPath = Path.Combine(binPath, "NewVersion");
        if (!IsProperConfiguration(binPath))
        {
            throw new ArgumentException("Invalid service path");
        }
        Upgrade(newVersionPath, binPath, _configuration.ServiceOldVersionsPath);
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
        return Task.CompletedTask;
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

    private void Upgrade(string newVersionPath, string binPath, string? serviceOldVersionsPath)
    {
        if (!Directory.Exists(newVersionPath))
        {
            Directory.CreateDirectory(newVersionPath);
        }

        Repeat(StopAction);
        
        UnzipAndCopyFiles(newVersionPath, binPath, serviceOldVersionsPath);

        Repeat(StartAction);
    }

    private string GetServiceName()
    {
        return ServiceName.GetServiceName(_configuration.ServiceName, _configuration.ServiceNameSuffix);
    }

	private void Repeat(Action<TimeSpan> action)
	{
		TimeSpan time = TimeSpan.FromSeconds(10);
		int i = 4;
		while (i >= 0)
		{
			action(time);
			time *= 2;
			i--;
		}
	}

	private void StartAction(TimeSpan wait)
    {
        ServiceController appDriver = new ServiceController(GetServiceName());
        if (appDriver.Status == ServiceControllerStatus.Stopped)
        {
            appDriver.Start();
            appDriver.WaitForStatus(ServiceControllerStatus.Running, wait);
        }
    }

	private void StopAction(TimeSpan wait)
	{
		string serviceName = GetServiceName();

		try
		{
			using ServiceController appDriver = new ServiceController(serviceName);

			if (appDriver.Status == ServiceControllerStatus.Stopped || appDriver.Status == ServiceControllerStatus.StopPending)
			{
				return;
			}

			int processId = GetServiceProcessId(serviceName);
			var stopwatch = Stopwatch.StartNew();

			appDriver.Stop();
			appDriver.WaitForStatus(ServiceControllerStatus.Stopped, wait);

			if (processId != -1)
			{
				TryKillProcess(processId, wait - stopwatch.Elapsed);
			}
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

	private void TryKillProcess(int processId, TimeSpan remainingWait)
	{
		try
		{
			Process process = Process.GetProcessById(processId);
			if (!process.HasExited)
			{
				if (remainingWait > TimeSpan.Zero)
					process.WaitForExit((int)remainingWait.TotalMilliseconds);

				if (!process.HasExited)
				{
					process.Kill();
					process.WaitForExit();
				}
			}
		}
		catch (ArgumentException)
		{
            return;
		}
	}

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _fileWatcher.OnStop();
        return Task.CompletedTask;
    }

    private void UnzipAndCopyFiles(string sourcePath, string servicePath, string? serviceOldVersionsPath)
    {
        string[] files = Directory.GetFiles(sourcePath);

        if (files.Length > 0)
        {
            ZipOldVersion(servicePath, serviceOldVersionsPath);
        }

        foreach (string file in files)
        {
            _logger.LogDebug("Starting unzipping '{0}'", file);
            string fileName = Path.GetFileName(file);
            string zipPath = Path.Combine(sourcePath, fileName);
            ZipFile.ExtractToDirectory(zipPath, servicePath, true);
            File.Delete(file);
            _logger.LogInformation("The new version has been copied '{0}'", file);
        }
    }
    
    private void ZipOldVersion(string sourcePath, string? destPath)
    {
        if (destPath != null)
        {
            if (!Directory.Exists(destPath))
            {
                Directory.CreateDirectory(destPath);
            }
            string destFile = Path.Combine(destPath, $"{DateTime.UtcNow.ToString("o").Replace(":","_").Replace(".","_")}.zip");
            _logger.LogDebug("Starting zipping '{0}'", sourcePath);
            ZipFile.CreateFromDirectory(sourcePath, destFile);
            _logger.LogInformation("The old version has been copied '{0}'", destFile);
        }
    }
}