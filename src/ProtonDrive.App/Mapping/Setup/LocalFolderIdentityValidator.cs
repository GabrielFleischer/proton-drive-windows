﻿using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using ProtonDrive.App.Settings;
using ProtonDrive.App.SystemIntegration;
using ProtonDrive.Client.Contracts;

namespace ProtonDrive.App.Mapping.Setup;

internal sealed class LocalFolderIdentityValidator : IMappingsAware
{
    private readonly ILogger<LocalFolderIdentityValidator> _logger;
    private IReadOnlyCollection<RemoteToLocalMapping> _activeMappings = [];

    public LocalFolderIdentityValidator(ILogger<LocalFolderIdentityValidator> logger)
    {
        _logger = logger;
    }

    public MappingErrorCode? ValidateFolderIdentity(LocalFolderInfo folderInfo, LocalReplica replica, LinkType remoteRootType)
    {
        return ValidateFolderIdentity(folderInfo.VolumeInfo.VolumeSerialNumber, folderInfo.Id, replica, remoteRootType);
    }

    void IMappingsAware.OnMappingsChanged(
        IReadOnlyCollection<RemoteToLocalMapping> activeMappings,
        IReadOnlyCollection<RemoteToLocalMapping> deletedMappings)
    {
        _activeMappings = activeMappings;
    }

    private MappingErrorCode? ValidateFolderIdentity(int volumeSerialNumber, long folderId, LocalReplica replica, LinkType remoteRootType)
    {
        if ((replica.RootFolderId != 0 && replica.RootFolderId != folderId)
            || (replica.VolumeSerialNumber != 0 && replica.VolumeSerialNumber != volumeSerialNumber))
        {
            _logger.LogWarning("The local sync folder identity has diverged");
            return MappingErrorCode.LocalFolderDiverged;
        }

        if (remoteRootType is LinkType.Folder)
        {
            var mappingWithDuplicateFolder = _activeMappings
                .FirstOrDefault(m => m.Local != replica && m.Local.VolumeSerialNumber == volumeSerialNumber && m.Local.RootFolderId == folderId);

            if (mappingWithDuplicateFolder is not null)
            {
                _logger.LogWarning("The local sync folder identity is already used in sync folder mapping '{MappingId}'", mappingWithDuplicateFolder.Id);
                return MappingErrorCode.LocalFolderIncludedByAnAlreadySyncedFolder;
            }
        }

        return null;
    }
}
