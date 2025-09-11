using Dzidek.Net.AutoUpgrade.Common;
using Dzidek.Net.AutoUpgrade.Upgrader.FileSystemWatchers;
using Dzidek.Net.AutoUpgrade.Upgrader.FileUtils;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.IO.Compression;
using System.Management;
using System.ServiceProcess;

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
        string servicePath = _configuration.ServicePath;
        string newVersionPath = _configuration.GetNewVersionPath();

        if (!IsProperConfiguration(servicePath))
        {
            throw new ArgumentException("Invalid service path");
        }
        await Upgrade(newVersionPath, servicePath, _configuration.ServiceOldVersionsPath);
        _fileWatcher.OnStart(
            new FileSystemWatcherConfiguration()
            {
                Enabled = true,
                Path = newVersionPath
            },
            new FileSystemWatcherActions()
            {
                Created = (sender, args) => { Upgrade(newVersionPath, servicePath, _configuration.ServiceOldVersionsPath); }
            });
    }

    private bool IsProperConfiguration(string binPath)
    {
        return (Directory.GetFiles(binPath).Any() || Directory.GetDirectories(binPath).Any()) && Directory.GetFiles(GetServicePath()).Any();
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

    private async Task Upgrade(string newVersionPath, string servicePath, string? serviceOldVersionsPath)
    {
        if (!Directory.Exists(newVersionPath))
        {
            Directory.CreateDirectory(newVersionPath);
        }

        string servicePathParent = Directory.GetParent(servicePath)?.FullName
                ?? throw new DirectoryNotFoundException($"Given path is incorrect: {servicePath}");
        string extractionPath = Path.Combine(servicePathParent, "ExtractionDirectory");

        await RetryPolicyRetriever.GetRetryAsyncForever(_logger, "Some problem appeared while unzipping new version.")
            .ExecuteAsync(() => ExtractZipStep(newVersionPath, servicePath, serviceOldVersionsPath, extractionPath));

        RetryPolicyRetriever.GetRetryForever(_logger, "Some problem appeared while stopping process")
            .Execute(() => StopServiceStep(TimeSpan.FromSeconds(10)));

        RetryPolicyRetriever.GetRetryForever(_logger, "Some problem appeared while copying new version files.")
            .Execute(() => ReplaceFilesWithNewVersionStep(extractionPath, servicePath));

        RetryPolicyRetriever.GetRetryForever(_logger, "Some problem appeared while starting process")
            .Execute(() => StartServiceStep(TimeSpan.FromSeconds(10)));
    }

    private void ReplaceFilesWithNewVersionStep(string extractionPath, string servicePath)
    {
        var newVersionFiles = FileSearcher.GetFilesRecursively(extractionPath);
        var currentVersionFiles = FileSearcher.GetFilesRecursively(servicePath, newVersionFiles.Select(x => x.FileRelativePath).ToList());

        var filesToReplace = GetFilesToReplace(newVersionFiles, currentVersionFiles);

        if (!filesToReplace.Any())
        {
            return;
        }

        ReplaceFiles(filesToReplace, extractionPath, servicePath);
    }

    private void ReplaceFiles(List<FileMd5> filesToReplace, string extractionPath, string servicePath)
    {
        var notCopiedFiles = filesToReplace;

        RetryPolicyRetriever.GetForeverWhenListNotEmpty<FileMd5>(_logger, "An error occurred while copying file")
            .Execute(() =>
            {
                notCopiedFiles = TryReplaceFiles(notCopiedFiles, extractionPath, servicePath);
                return notCopiedFiles;
            });
    }

    private List<FileMd5> TryReplaceFiles(List<FileMd5> filesToReplace, string extractionPath, string servicePath)
    {
        var notCopiedFiles = new List<FileMd5>();
        foreach (var fileToReplace in filesToReplace)
        {
            var fullFilePath = Path.Combine(extractionPath, fileToReplace.FileRelativePath);
            var destinationFilePath = Path.Combine(servicePath,fileToReplace.FileRelativePath);
            try
            {
                File.Copy(fullFilePath, destinationFilePath, true);
            }
            catch (Exception ex) {
                notCopiedFiles.Add(fileToReplace);
                _logger.LogError($"An error occurred while copying file: {fullFilePath}.", ex);
            }
        }
        return notCopiedFiles;
    }

    private List<FileMd5> GetFilesToReplace(List<FileMd5> newVersionFiles, List<FileMd5> currentVersionFiles)
    {
        var result = new List<FileMd5>();
        foreach (var newFile in newVersionFiles)
        {
            if (!currentVersionFiles.Any(x =>
                x.FileRelativePath == newFile.FileRelativePath &&
                x.Md5 == newFile.Md5))
            {
                result.Add(newFile);
            }
        }
        return result;
    }

    private string GetServiceName()
    {
        return ServiceName.GetServiceName(_configuration.ServiceName, _configuration.ServiceNameSuffix);
    }

    private void StartServiceStep(TimeSpan wait)
    {
        _logger.LogDebug($"11 StartAction waitTime = {wait.TotalSeconds.ToString()}");
        ServiceController appDriver = new ServiceController(GetServiceName());
        if (appDriver.Status == ServiceControllerStatus.Stopped)
        {
            appDriver.Start();
        }

        if (appDriver.Status != ServiceControllerStatus.Running)
        {
            appDriver.WaitForStatus(ServiceControllerStatus.Running, wait);
        }
    }

    private void StopServiceStep(TimeSpan wait)
    {
        string serviceName = GetServiceName();

        using ServiceController appDriver = new ServiceController(serviceName);

        if (appDriver.Status == ServiceControllerStatus.Stopped)
        {
            return;
        }

        if (appDriver.Status == ServiceControllerStatus.Running)
        {
            appDriver.Stop();
        }

        appDriver.WaitForStatus(ServiceControllerStatus.Stopped, wait);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _fileWatcher.OnStop();
        return Task.CompletedTask;
    }

    private async Task ExtractZipStep(string sourcePath, string servicePath, string? serviceOldVersionsPath, string extractionPath)
    {
        string[] files = Directory.GetFiles(sourcePath);

        if (!files.Any()) return;

        // TODO: EXECUTE IF NOT EXECUTED
        await ZipOldVersion(files, servicePath, serviceOldVersionsPath);
        await ExtractZip(sourcePath, files, extractionPath);
    }

    private async Task ExtractZip(string sourcePath, string[] files, string extractionPath)
    {
        // file is 1 whole zip
        foreach (string zipFile in files)
        {
            _logger.LogDebug("Starting unzipping '{0}'", zipFile);
            string zipFileName = Path.GetFileName(zipFile);
            string zipPath = Path.Combine(sourcePath, zipFileName);

            await PrepareDirectory(extractionPath);

            ZipFile.ExtractToDirectory(zipPath, extractionPath, true);

            File.Delete(zipFile);
        }
    }

    private async Task PrepareDirectory(string extractionPath)
    {
        await Task.Run(() =>
        {
            if (Directory.Exists(extractionPath))
            {
                Directory.Delete(extractionPath, recursive: true);
            }

            Directory.CreateDirectory(extractionPath);
        });
    }

    private async Task ZipOldVersion(string[] files, string sourcePath, string? destPath)
    {
        if (destPath == null)
        {
            return;
        }

        if (!Directory.Exists(destPath))
        {
            Directory.CreateDirectory(destPath);
        }

        string destFile = Path.Combine(destPath, $"{DateTime.UtcNow.ToString("o").Replace(":", "_").Replace(".", "_")}.zip");
        _logger.LogDebug("Starting zipping '{0}'", sourcePath);

        ZipFile.CreateFromDirectory(sourcePath, destFile);

        _logger.LogInformation("The old version has been copied '{0}'", destFile);
    }
}