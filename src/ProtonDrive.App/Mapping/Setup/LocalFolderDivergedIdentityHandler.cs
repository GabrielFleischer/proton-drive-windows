﻿using System;
using System.IO;
using Microsoft.Extensions.Logging;
using ProtonDrive.App.Settings;
using ProtonDrive.App.SystemIntegration;

namespace ProtonDrive.App.Mapping.Setup;

/// <summary>
/// There are cases, when Windows upgrade (to 24H2) replaces the known system folder with a new one,
/// and preserves the content. Detects such case and updates folder identity in the mapping.
/// </summary>
internal sealed class LocalFolderDivergedIdentityHandler
{
    private readonly ILocalFolderService _localFolderService;
    private readonly ILogger<LocalFolderDivergedIdentityHandler> _logger;

    public LocalFolderDivergedIdentityHandler(
        ILocalFolderService localFolderService,
        ILogger<LocalFolderDivergedIdentityHandler> logger)
    {
        _localFolderService = localFolderService;
        _logger = logger;
    }

    public bool TryAcceptDivergedIdentity(LocalFolderInfo folderInfo, RemoteToLocalMapping mapping)
    {
        if (mapping.Type is not MappingType.HostDeviceFolder)
        {
            _logger.LogWarning("Mapping type is not {ExpectedMappingType}", MappingType.HostDeviceFolder);
            return false;
        }

        var replica = mapping.Local;

        if (replica.VolumeSerialNumber != 0 && replica.VolumeSerialNumber != folderInfo.VolumeInfo.VolumeSerialNumber)
        {
            _logger.LogWarning("Volume serial number has diverged");
            return false;
        }

        var path = replica.Path;

        if (!_localFolderService.NonEmptyFolderExists(path))
        {
            _logger.LogWarning("Local sync folder is empty");
            return false;
        }

        var parentPath = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(parentPath))
        {
            _logger.LogWarning("Unable to obtain the parent path of local sync folder");
            return false;
        }

        var userProfilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(userProfilePath))
        {
            _logger.LogWarning("Unable to obtain the user profile folder");
            return false;
        }

        if (!parentPath.Equals(userProfilePath, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("The local sync folder is not a subfolder of user profile");
            return false;
        }

        _logger.LogWarning(
            "Accepted new identity of local sync folder (diverged from {PreviousId} to {NewId})",
            mapping.Local.RootFolderId,
            folderInfo.Id);

        replica.RootFolderId = folderInfo.Id;

        return true;
    }
}
