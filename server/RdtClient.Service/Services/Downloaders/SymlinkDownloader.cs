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
        _logger.Debug($"Starting download of {_download.RemoteId}...");
        var filePath = new DirectoryInfo(_filePath);
        _logger.Debug($"Writing to path: ${filePath}");
        var fileName = filePath.Name;
        var fileExtension = filePath.Extension;
        var fileDirectory = Path.GetFileName(Path.GetDirectoryName(filePath.FullName));

        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        var fileDirectoryWithoutExtension = Path.GetFileNameWithoutExtension(fileDirectory);

        string[] folders = { fileNameWithoutExtension, fileDirectoryWithoutExtension, fileName, fileDirectory };


        List<string> unWantedExtensions = new()
        {
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

        FileInfo? file = null;
        var tries = 0;
        while (file == null && tries <= 10)
        {
            _logger.Debug($"Searching {Settings.Get.DownloadClient.RcloneMountPath} for {fileName} ({tries})...");
            file = TryGetFileFromFolders(folders, fileName);
            await Task.Delay(1000);
            tries++;
        }

        if (file != null)
        {

            var result = TryCreateSymbolicLink(file.FullName, filePath.FullName);

            if (result)
            {
                if (!String.IsNullOrWhiteSpace(Settings.Get.General.CopyAddedTorrents))
                {
                    try
                    {
                        var baseSymlinkPath = Path.Combine(Settings.Get.General.CopyAddedTorrents);

                        if (!Directory.Exists(baseSymlinkPath))
                        {
                            Directory.CreateDirectory(baseSymlinkPath);
                        }

                        var finalSymlinkPath = Path.Combine(baseSymlinkPath, fileName);

                        if (!fileName.Equals(fileDirectory, StringComparison.OrdinalIgnoreCase))
                        {

                            var nestedFolderPath = Path.Combine(baseSymlinkPath, fileDirectory);

                            if (!Directory.Exists(nestedFolderPath))
                            {
                                Directory.CreateDirectory(nestedFolderPath);
                            }
                            finalSymlinkPath = Path.Combine(nestedFolderPath, fileName);
                        }


                        // file.FullName au lieu de fileName ? (ou actualPath (ancien fix))
                        if (TryCreateAdditionalSymbolicLink(file.FullName, finalSymlinkPath))
                        {
                            _logger.Information($"Successfully created both symbolic links for {fileName}");
                        }
                        else
                        {
                            _logger.Warning($"Only the primary symbolic link was created for {fileName}. Additional symlink failed.");
                        }


                        var sourceFilePath = Path.Combine(Settings.Get.DownloadClient.MappedPath, "tempTorrentsFiles", $"{fileDirectory}.torrent");
                        var targetFilePath = Path.Combine(Settings.Get.General.CopyAddedTorrents, $"{fileDirectory}.torrent");

                        _logger.Information($"Tentative de déplacement du fichier {fileDirectory}.torrent");

                        if (File.Exists(sourceFilePath))
                        {
                            if (File.Exists(targetFilePath))
                            {
                                File.Delete(targetFilePath);
                            }
                            File.Move(sourceFilePath, targetFilePath);
                            _logger.Information($"Moved {fileDirectory}.torrent from tempTorrentsFiles to the final directory.");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"An unexpected error occurred while attempting to create additionnal symbolic links or move the torrent file for {fileDirectory}: {ex.Message}. Error details: {ex.StackTrace}");
                    }
                }

                DownloadComplete?.Invoke(this, new DownloadCompleteEventArgs());

                return file.FullName;
            }
        }

        DownloadComplete?.Invoke(this, new DownloadCompleteEventArgs
        {
            Error = "Could not find file from rclone mount!"
        });

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
    private bool TryCreateAdditionalSymbolicLink(string sourcePath, string additionalSymlinkPath)
    {
        try
        {
            var directoryPath = Path.GetDirectoryName(additionalSymlinkPath);
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            File.CreateSymbolicLink(additionalSymlinkPath, sourcePath);

            if (File.Exists(additionalSymlinkPath))
            {
                _logger.Information($"Created additional symbolic link from {sourcePath} to {additionalSymlinkPath}");
                return true;
            }
            else
            {
                _logger.Error($"Failed to create additional symbolic link from {sourcePath} to {additionalSymlinkPath}");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Error creating additional symbolic link from {sourcePath} to {additionalSymlinkPath}: {ex.Message}");
            return false;
        }
    }
    private static FileInfo? TryGetFileFromFolders(string[] Folders, string File)
    {
        var dirInfo = new DirectoryInfo(Settings.Get.DownloadClient.RcloneMountPath);
        return dirInfo.EnumerateDirectories()
            .FirstOrDefault(dir => Folders.Contains(dir.Name), null)?
                .EnumerateFiles()
                .FirstOrDefault(x => x.Name == File, null);
    }
}
