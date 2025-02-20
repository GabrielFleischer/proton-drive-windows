﻿using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ProtonDrive.Shared.Extensions;
using ProtonDrive.Update.Helpers;

namespace ProtonDrive.Update.Files.Downloadable;

/// <summary>
/// Logs requests and exceptions of <see cref="DownloadableFile"/>.
/// </summary>
internal class LoggingDownloadableFile : IDownloadableFile
{
    private readonly ILogger<LoggingDownloadableFile> _logger;
    private readonly IDownloadableFile _origin;

    public LoggingDownloadableFile(ILogger<LoggingDownloadableFile> logger, IDownloadableFile origin)
    {
        _logger = logger;
        _origin = origin;
    }

    public async Task DownloadAsync(string url, string filePath)
    {
        try
        {
            var filename = Path.GetFileName(filePath);

            _logger.LogInformation("Downloading the app update file \"{filename}\"", filename);

            await _origin.DownloadAsync(url, filePath).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex.IsCommunicationException() || ex.IsFileAccessException())
        {
            _logger.LogError("Failed to download the app update: {Error}", ex.CombinedMessage());

            throw;
        }
    }
}
