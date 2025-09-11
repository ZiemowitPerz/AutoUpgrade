
namespace Dzidek.Net.AutoUpgrade.Upgrader.FileUtils;

public static class FileSearcher
{
    public static List<FileMd5> GetFilesRecursively(string sourceDirectory, List<string>? includeOnly = null)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory))
            throw new ArgumentException("Source directory path is not given.", nameof(sourceDirectory));

        if (!Directory.Exists(sourceDirectory))
            throw new DirectoryNotFoundException($"The directory '{sourceDirectory}' does not exist.");

        var allFiles = GetAllFiles(sourceDirectory);

        if (includeOnly != null)
        {
            allFiles = allFiles.Where(includeOnly.Contains).ToList();
        }

        var result = new List<FileMd5>();
        foreach (var file in allFiles)
        {
            result.Add(new FileMd5
            {
                FileRelativePath = file,
                Md5 = FileMd5Retriever.GetMd5(Path.Combine(sourceDirectory, file))
            });
        }

        return result;
    }

    private static List<string> GetAllFiles(string sourceDirectory)
    {
        var files = Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories).ToList();
        var result = new List<string>();

        foreach (var file in files)
        {
            result.Add(Path.GetRelativePath(sourceDirectory, file));
        }

        return result;
    }
}