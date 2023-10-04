using System.Diagnostics;
using Microsoft.AspNetCore.Routing.Constraints;
using RdtClient.Data.Models.Data;
using Serilog;

namespace RdtClient.Service.Services.Downloaders;

public class SymlinkDownloader : IDownloader
{
    public event EventHandler<DownloadCompleteEventArgs>? DownloadComplete;
    public event EventHandler<DownloadProgressEventArgs>? DownloadProgress;

    private readonly Download _download;
    private readonly string _filePath;
    
    private readonly CancellationTokenSource _cancellationToken = new();
    
    private readonly ILogger _logger;
    
    public SymlinkDownloader(Download download, string filePath)
    {
        _logger = Log.ForContext<SymlinkDownloader>();
        _download = download;
        _filePath = filePath;
    }

    public async Task<string?> Download()
    {
        var filePath = _filePath;
        _logger.Debug($"Starting download of {_download.RemoteId}, writing to path: {filePath}");

        var fileName = Path.GetFileName(filePath);
        var fileExtension = Path.GetExtension(filePath);

        List<string> unWantedExtensions = new List<string>{
            "zip", "rar", "tar" 
        };

        if (unWantedExtensions.Any(unwanted => "." + fileExtension == unwanted))
        {
            DownloadComplete?.Invoke(this, new DownloadCompleteEventArgs
            {
                Error = $"Cant handle compressed files with symlink downloader"
            });
            return null;
        }

        DownloadProgress?.Invoke(this, new DownloadProgressEventArgs
        {
            BytesDone = 0,
            BytesTotal = 0,
            Speed = 0
        });


        _logger.Debug($"Searching {Settings.Get.DownloadClient.RcloneMountPath} for {fileName}");

        string[] foundFiles = Array.Empty<string>();
        var tries = 0;
        while (foundFiles.Length == 0 && tries <= Settings.Get.Integrations.Default.DownloadRetryAttempts)
        {
            foundFiles = TryGetFiles(fileName);
            await Task.Delay(500);
            tries++;
        }

        var actualFilePath = foundFiles.First();

        var result = TryCreateSymbolicLink(actualFilePath, filePath);

        if (result)
        {
            DownloadComplete?.Invoke(this, new DownloadCompleteEventArgs());

            return actualFilePath;
        }

        return null;
    }

    public Task Cancel()
    {
        _logger.Debug($"Cancelling download {_download.RemoteId}");

        _cancellationToken.Cancel(false);

        return Task.CompletedTask;
    }

    public Task Pause()
    {
        return Task.CompletedTask;
    }

    public Task Resume()
    {
        return Task.CompletedTask;
    }

    private bool TryCreateSymbolicLink(string sourcePath, string symlinkPath)
    {
        try
        {
            File.CreateSymbolicLink(symlinkPath, sourcePath);
            if (File.Exists(symlinkPath))  // Double-check that the link was created
            {
                _logger.Information($"Created symbolic link from {sourcePath} to {symlinkPath}");
                return true;
            }
            else
            {
                _logger.Error($"Failed to create symbolic link from {sourcePath} to {symlinkPath}");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Error creating symbolic link from {sourcePath} to {symlinkPath}: {ex.Message}");
            return false;
        }
    }

    private static string[] TryGetFiles(string Name)
    {
        return Directory.GetFiles(Settings.Get.DownloadClient.RcloneMountPath, Name, SearchOption.AllDirectories);
    }
}
