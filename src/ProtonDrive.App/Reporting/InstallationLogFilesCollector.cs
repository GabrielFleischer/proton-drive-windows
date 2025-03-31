﻿using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ProtonDrive.App.Services;
using ProtonDrive.Shared.Configuration;
using ProtonDrive.Shared.Extensions;

namespace ProtonDrive.App.Reporting;

internal sealed class InstallationLogFilesCollector : IStartableService
{
    private static readonly SearchValues<string> ProtonSearchValues = SearchValues.Create(["Proton Drive"], StringComparison.OrdinalIgnoreCase);

    private readonly string _tempFolderPath = Path.GetTempPath();
    private readonly string _installationLogFolderPath;
    private readonly DateTime _recentThresholdTimeUtc;
    private readonly ILogger<InstallationLogFilesCollector> _logger;

    public InstallationLogFilesCollector(AppConfig appConfig, ILogger<InstallationLogFilesCollector> logger)
    {
        _logger = logger;
        var logFolderPath = Path.Combine(appConfig.AppDataPath, "Logs");
        _installationLogFolderPath = Path.Combine(logFolderPath, "Installation");
        _recentThresholdTimeUtc = DateTime.UtcNow.AddMonths(-appConfig.NumberOfMonthsBeforeRemovingInstallationLogFiles);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Task.Run(CollectInstallationLogFiles, cancellationToken);

        return Task.CompletedTask;
    }

    public void CollectInstallationLogFiles()
    {
        var logFiles = GetRecentInstallationLogFiles();

        CopyInstallationLogFiles(logFiles);

        DeleteOldInstallationLogFiles();
    }

    private static void EnsureFolderExists(string path)
    {
        Directory.CreateDirectory(path);
    }

    private static bool IsInstallationLogFile(string filePath)
    {
        try
        {
            using var fileStream = File.Open(filePath, FileMode.Open);
            using var streamReader = new StreamReader(fileStream, detectEncodingFromByteOrderMarks: true);

            Span<char> chars = stackalloc char[1024];
            return streamReader.ReadBlock(chars) > 0 && chars.IndexOfAny(ProtonSearchValues) > -1;
        }
        catch (Exception ex) when (ex.IsFileAccessException())
        {
            return false;
        }
    }

    private bool FileCreatedRecently(string filePath)
    {
        return File.GetCreationTimeUtc(filePath) >= _recentThresholdTimeUtc;
    }

    private bool FileNotCreatedRecently(string filePath)
    {
        return File.GetCreationTimeUtc(filePath) < _recentThresholdTimeUtc;
    }

    private IEnumerable<string> GetRecentInstallationLogFiles()
    {
        try
        {
            var installerLogFiles = Directory.EnumerateFiles(_tempFolderPath, "Proton_Drive*.log", SearchOption.TopDirectoryOnly)
                .Where(FileCreatedRecently);

            var msiLogFiles = Directory.EnumerateFiles(_tempFolderPath, "MSI*.log", SearchOption.TopDirectoryOnly)
                .Where(FileCreatedRecently)
                .Where(IsInstallationLogFile);

            return installerLogFiles.Concat(msiLogFiles);
        }
        catch (Exception ex) when (ex.IsFileAccessException())
        {
            _logger.LogWarning("Failed to get recent installation log files: {Message}", ex.Message);
            return [];
        }
    }

    private void CopyInstallationLogFiles(IEnumerable<string> filePaths)
    {
        EnsureFolderExists(_installationLogFolderPath);

        foreach (var sourceFilePath in filePaths)
        {
            var filename = Path.GetFileName(sourceFilePath);
            var destinationFilePath = Path.Combine(_installationLogFolderPath, filename);

            if (File.Exists(destinationFilePath))
            {
                continue;
            }

            try
            {
                File.Copy(sourceFilePath, destinationFilePath);
                File.SetCreationTimeUtc(destinationFilePath, File.GetCreationTimeUtc(sourceFilePath));
            }
            catch (Exception ex) when (ex.IsFileAccessException())
            {
                _logger.LogWarning("Failed to copy installation log file: {Message}", ex.Message);
            }
        }
    }

    private void DeleteOldInstallationLogFiles()
    {
        try
        {
            foreach (var filePath in Directory.EnumerateFiles(_installationLogFolderPath).Where(FileNotCreatedRecently))
            {
                try
                {
                    File.Delete(filePath);
                }
                catch (Exception ex) when (ex.IsFileAccessException())
                {
                    _logger.LogWarning("Failed to delete old installation log file: {Message}", ex.Message);
                }
            }
        }
        catch (Exception ex) when (ex.IsFileAccessException())
        {
            _logger.LogWarning("Failed to enumerate installation log files: {Message}", ex.Message);
        }
    }
}
