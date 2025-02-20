﻿using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using ProtonDrive.App.Configuration;
using ProtonDrive.App.Settings;
using ProtonDrive.App.SystemIntegration;
using ProtonDrive.Client.Contracts;
using ProtonDrive.Shared.Repository;
using ProtonDrive.Shared.Text.Serialization;

namespace ProtonDrive.App.Mapping.Teardown;

public sealed class LocalMappedFoldersTeardownService
{
    private readonly ISyncFolderStructureProtector _syncFolderStructureProtector;
    private readonly IPlaceholderToRegularItemConverter _placeholderConverter;
    private readonly IReadOnlyFileAttributeRemover _readOnlyFileAttributeRemover;

    private LocalMappedFoldersTeardownService(
        ISyncFolderStructureProtector syncFolderStructureProtector,
        IPlaceholderToRegularItemConverter placeholderConverter,
        IReadOnlyFileAttributeRemover readOnlyFileAttributeRemover)
    {
        _syncFolderStructureProtector = syncFolderStructureProtector;
        _placeholderConverter = placeholderConverter;
        _readOnlyFileAttributeRemover = readOnlyFileAttributeRemover;
    }

    public static bool TryTearDownLocalFolders(
        ISyncFolderStructureProtector syncFolderStructureProtector,
        IPlaceholderToRegularItemConverter placeholderConverter,
        IReadOnlyFileAttributeRemover readOnlyFileAttributeRemover)
    {
        return new LocalMappedFoldersTeardownService(syncFolderStructureProtector, placeholderConverter, readOnlyFileAttributeRemover).TryTearDownLocalFolders();
    }

    public bool TryTearDownLocalFolders()
    {
        try
        {
            var mappingRepository = GetMappingRepository();

            var mappingSettings = mappingRepository.Get();

            if (mappingSettings is null)
            {
                return true;
            }

            var succeeded = true;

            // Mappings are torn down in a hierarchical order
            var mappings = mappingSettings.Mappings
                .Where(m => m.Status is not MappingStatus.TornDown)
                .OrderDescending(HierarchicalMappingComparer.Instance);

            foreach (var mapping in mappings)
            {
                if (mapping.Status is not MappingStatus.Deleted)
                {
                    mapping.Status = MappingStatus.Deleted;

                    // Persisting mapping status change before further processing. If the app uninstall fails,
                    // and the user starts the app again, keeping the mapping status not saved could lead to
                    // detecting local files as deleted, and deleting them on Proton Drive.
                    mappingRepository.Set(mappingSettings);
                }

                var localPath = mapping.Local.Path;

                switch (mapping.Type)
                {
                    case MappingType.HostDeviceFolder:
                        // No protection was added
                        if (mapping.SyncMethod is SyncMethod.OnDemand || mapping.SyncMethodUpdateStatus is not SyncMethodUpdateStatus.None)
                        {
                            succeeded &= _placeholderConverter.TryConvertToRegularFolder(localPath, skipRoot: true);
                        }

                        break;

                    case MappingType.CloudFiles:
                        var parentFolderPath = Path.GetDirectoryName(localPath) ?? string.Empty;
                        succeeded &= _syncFolderStructureProtector.UnprotectFolder(parentFolderPath, FolderProtectionType.Ancestor);
                        succeeded &= _syncFolderStructureProtector.UnprotectFolder(localPath, FolderProtectionType.Leaf);
                        succeeded &= _placeholderConverter.TryConvertToRegularFolder(localPath, skipRoot: true);
                        break;

                    case MappingType.ForeignDevice:
                        parentFolderPath = Path.GetDirectoryName(localPath) ?? string.Empty;
                        succeeded &= _syncFolderStructureProtector.UnprotectFolder(parentFolderPath, FolderProtectionType.Ancestor);
                        succeeded &= _syncFolderStructureProtector.UnprotectFolder(localPath, FolderProtectionType.Leaf);
                        succeeded &= _placeholderConverter.TryConvertToRegularFolder(localPath, skipRoot: true);
                        break;

                    case MappingType.SharedWithMeRootFolder:
                        succeeded &= _syncFolderStructureProtector.UnprotectFolder(localPath, FolderProtectionType.AncestorWithFiles);
                        break;

                    case MappingType.SharedWithMeItem when mapping.Remote.RootItemType is LinkType.Folder:
                        succeeded &= _syncFolderStructureProtector.UnprotectFolder(localPath, FolderProtectionType.Leaf);

                        if (mapping.Remote.IsReadOnly)
                        {
                            succeeded &= _syncFolderStructureProtector.UnprotectBranch(localPath, FolderProtectionType.ReadOnly, FileProtectionType.ReadOnly);
                            succeeded &= _readOnlyFileAttributeRemover.TryRemoveFileReadOnlyAttributeInFolder(localPath);
                        }

                        succeeded &= _placeholderConverter.TryConvertToRegularFolder(localPath, skipRoot: false);
                        break;

                    case MappingType.SharedWithMeItem when mapping.Remote.RootItemType is LinkType.File:
                        succeeded &= _syncFolderStructureProtector.UnprotectFolder(localPath, FolderProtectionType.Leaf);

                        if (mapping.Remote.IsReadOnly)
                        {
                            succeeded &= _syncFolderStructureProtector.UnprotectFile(localPath, FileProtectionType.ReadOnly);
                            succeeded &= _readOnlyFileAttributeRemover.TryRemoveFileReadOnlyAttribute(localPath);
                        }

                        succeeded &= _placeholderConverter.TryConvertToRegularFile(localPath);
                        break;

                    default:
                        throw new InvalidEnumArgumentException(nameof(mapping.Type), (int)mapping.Type, typeof(MappingType));
                }
            }

            return succeeded;
        }
        catch
        {
            return false;
        }
    }

    private static IRepository<MappingSettings> GetMappingRepository()
    {
        var localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var filePath = Path.Combine(
            localAppDataPath,
            AppRuntimeConfigurationSource.ProtonFolderName,
            AppRuntimeConfigurationSource.ProtonDriveFolderName,
            AppRuntimeConfigurationSource.SyncFoldersMappingFilename);

        return new SafeRepository<MappingSettings>(
            new FileRepository<MappingSettings>(
                new JsonUtf8Serializer(),
                filePath));
    }
}
