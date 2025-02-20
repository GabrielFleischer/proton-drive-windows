﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProtonDrive.App.SystemIntegration;
using ProtonDrive.Shared.Extensions;
using ProtonDrive.Shared.IO;

namespace ProtonDrive.App.Mapping;

internal sealed class LocalSyncFolderValidator : ILocalSyncFolderValidator
{
    private readonly ILocalVolumeInfoProvider _localVolumeInfoProvider;
    private readonly INonSyncablePathProvider _nonSyncablePathProvider;
    private readonly ILocalFolderService _localFolderService;

    public LocalSyncFolderValidator(
        ILocalVolumeInfoProvider localVolumeInfoProvider,
        INonSyncablePathProvider nonSyncablePathProvider,
        ILocalFolderService localFolderService)
    {
        _localVolumeInfoProvider = localVolumeInfoProvider;
        _nonSyncablePathProvider = nonSyncablePathProvider;
        _localFolderService = localFolderService;
    }

    public SyncFolderValidationResult ValidatePath(string path, IReadOnlySet<string> otherPaths)
    {
        path = PathComparison.EnsureTrailingSeparator(path);

        if (_nonSyncablePathProvider.Paths
            .Select(PathComparison.EnsureTrailingSeparator)
            .Any(prohibitedPath => path.StartsWith(prohibitedPath, StringComparison.OrdinalIgnoreCase) ||
                                   prohibitedPath.StartsWith(path, StringComparison.OrdinalIgnoreCase)))
        {
            return SyncFolderValidationResult.NonSyncableFolder;
        }

        foreach (var otherPath in otherPaths.Select(PathComparison.EnsureTrailingSeparator))
        {
            if (path.StartsWith(otherPath, StringComparison.OrdinalIgnoreCase))
            {
                return SyncFolderValidationResult.FolderIncludedByAnAlreadySyncedFolder;
            }

            if (otherPath.StartsWith(path, StringComparison.OrdinalIgnoreCase))
            {
                return SyncFolderValidationResult.FolderIncludesAnAlreadySyncedFolder;
            }
        }

        return SyncFolderValidationResult.Succeeded;
    }

    public SyncFolderValidationResult ValidatePathAndDrive(string path, IReadOnlySet<string> otherPaths)
    {
        var driveValidationResult = ValidateDrive(path);

        if (driveValidationResult is not SyncFolderValidationResult.Succeeded)
        {
            return driveValidationResult;
        }

        return ValidatePath(path, otherPaths);
    }

    public SyncFolderValidationResult ValidateFolder(string path, bool shouldBeEmpty)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return SyncFolderValidationResult.LocalFolderDoesNotExist;
            }

            if (!_localVolumeInfoProvider.IsNtfsFileSystem(path))
            {
                return SyncFolderValidationResult.LocalVolumeNotSupported;
            }

            if (shouldBeEmpty && _localFolderService.NonEmptyFolderExists(path))
            {
                return SyncFolderValidationResult.LocalFolderNotEmpty;
            }

            return SyncFolderValidationResult.Succeeded;
        }
        catch (Exception e) when (e.IsFileAccessException())
        {
            return SyncFolderValidationResult.LocalFileSystemAccessFailed;
        }
    }

    private SyncFolderValidationResult ValidateDrive(string path)
    {
        var driveType = _localVolumeInfoProvider.GetDriveType(path);

        return driveType switch
        {
            DriveType.Fixed => SyncFolderValidationResult.Succeeded,
            DriveType.Network => SyncFolderValidationResult.NetworkFolderNotSupported,
            DriveType.Unknown => SyncFolderValidationResult.LocalFileSystemAccessFailed,
            _ => SyncFolderValidationResult.LocalVolumeNotSupported,
        };
    }
}
