﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using ProtonDrive.App.FileSystem.Local.SpecialFolders;
using ProtonDrive.App.FileSystem.Remote;
using ProtonDrive.App.Settings;
using ProtonDrive.App.SystemIntegration;
using ProtonDrive.Client.Contracts;
using ProtonDrive.Shared;
using ProtonDrive.Shared.Threading;
using ProtonDrive.Shared.Volume;
using ProtonDrive.Sync.Adapter;
using ProtonDrive.Sync.Shared;
using ProtonDrive.Sync.Shared.FileSystem;

namespace ProtonDrive.App.FileSystem.Local;

internal sealed class LocalDecoratedFileSystemClientFactory
{
    private readonly ILocalVolumeInfoProvider _volumeInfoProvider;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Func<IFileSystemClient<long>> _undecoratedClassicClientFactory;
    private readonly Func<IFileSystemClient<long>> _undecoratedOnDemandHydrationClientFactory;
    private readonly IFileTransferAbortionStrategy<long> _fileTransferAbortionStrategy;
    private readonly ISyncFolderStructureProtector _folderStructureProtector;

    public LocalDecoratedFileSystemClientFactory(
        ILocalVolumeInfoProvider volumeInfoProvider,
        ILoggerFactory loggerFactory,
        Func<IFileSystemClient<long>> undecoratedClassicClientFactory,
        Func<IFileSystemClient<long>> undecoratedOnDemandHydrationClientFactory,
        IFileTransferAbortionStrategy<long> fileTransferAbortionStrategy,
        ISyncFolderStructureProtector folderStructureProtector)
    {
        _volumeInfoProvider = volumeInfoProvider;
        _loggerFactory = loggerFactory;
        _undecoratedClassicClientFactory = undecoratedClassicClientFactory;
        _undecoratedOnDemandHydrationClientFactory = undecoratedOnDemandHydrationClientFactory;
        _fileTransferAbortionStrategy = fileTransferAbortionStrategy;
        _folderStructureProtector = folderStructureProtector;
    }

    public IFileSystemClient<long> GetClient(IReadOnlyCollection<RemoteToLocalMapping> mappings, LocalAdapterSettings settings)
    {
        int lastMoveScope = 0;
        var shareIdToMoveScopeMap = new Dictionary<string, int>();

        var cloudFilesMapping = mappings.FirstOrDefault(m => m.Type is MappingType.CloudFiles);

        // A local file system client is created per sync folder mapping
        var rootClientPairs = mappings
            .Where(m => m.Type is MappingType.CloudFiles or MappingType.ForeignDevice or MappingType.HostDeviceFolder)
            .Select(m => (
                Root: CreateRoot(m),
                Client: CreateClientForMapping(m, settings)));

        var sharedWithMeRootFolderMapping = mappings.FirstOrDefault(m => m.Type is MappingType.SharedWithMeRootFolder);

        // A single local file system client is created for all "shared with me" items.
        // An aggregating decorator aggregates requests from multiple sync roots
        // and dispatches file hydration demand to sync roots.
        var aggregatingClient = sharedWithMeRootFolderMapping is not null
            ? new AggregatingFileSystemClientDecorator<long>(
                new LocalRootDirectory(sharedWithMeRootFolderMapping.Local),
                CreateUndecoratedClient(sharedWithMeRootFolderMapping.SyncMethod))
            : null;

        var sharedWithMeRootToClientMap = mappings
            .Where(m => m.Type is MappingType.SharedWithMeItem)
            .Select(m => (
                Root: CreateRoot(m, sharedWithMeRootFolderMapping),
                Client: CreateClientForSharedWithMeItemMapping(m, parentMapping: sharedWithMeRootFolderMapping, settings, aggregatingClient)));

        var rootToClientMap = rootClientPairs.Concat(sharedWithMeRootToClientMap).ToDictionary(x => x.Root, x => x.Client);

        return
            new LoggingFileSystemClientDecorator<long>(
                _loggerFactory.CreateLogger<LoggingFileSystemClientDecorator<long>>(),
                new DispatchingFileSystemClient<long>(rootToClientMap));

        RootInfo<long> CreateRoot(RemoteToLocalMapping mapping, RemoteToLocalMapping? parentMapping = default)
        {
            var volumeId = mapping.Local.InternalVolumeId;

            var rootFolderId = mapping.Type is MappingType.SharedWithMeItem && mapping.Remote.RootItemType is LinkType.File
                ? parentMapping?.Local.RootFolderId ?? default
                : mapping.Local.RootFolderId;

            return new RootInfo<long>(
                Id: mapping.Id,
                volumeId,
                NodeId: rootFolderId)
            {
                EventScope = GetEventScope(mapping),
                MoveScope = GetMoveScope(mapping.Remote.ShareId),
                IsOnDemand = mapping.SyncMethod is SyncMethod.OnDemand,
                LocalPath = GetLocalPath(mapping),
                IsEnabled = mapping.HasSetupSucceeded,
            };
        }

        string GetEventScope(RemoteToLocalMapping mapping)
        {
            // Local events of host device folders are monitored on each host device folder using mapping ID as an event scope
            if (mapping.Type is MappingType.HostDeviceFolder)
            {
                return mapping.Id.ToString();
            }

            // The rest of events are monitored on the account root folder using cloud files mapping ID as an event scope.
            return (cloudFilesMapping?.Id ?? 0).ToString();
        }

        int GetMoveScope(string? shareId)
        {
            Ensure.NotNull(shareId, nameof(shareId));

            // Moving between remote shares is not supported
            if (shareIdToMoveScopeMap.TryGetValue(shareId, out var moveScope))
            {
                return moveScope;
            }

            moveScope = ++lastMoveScope;
            shareIdToMoveScopeMap.Add(shareId, moveScope);

            return moveScope;
        }

        string GetLocalPath(RemoteToLocalMapping mapping)
        {
            return mapping.Remote.RootItemType is LinkType.File
                ? GetVirtualRootPath(mapping)
                : mapping.Local.Path;
        }

        string GetVirtualRootPath(RemoteToLocalMapping mapping)
        {
            // Every shared with me file is placed in a virtual root folder
            return Path.GetDirectoryName(mapping.Local.Path.AsSpan()).ToString();
        }
    }

    private IFileSystemClient<long> CreateClientForMapping(RemoteToLocalMapping mapping, LocalAdapterSettings settings)
    {
        // A dummy (offline) client is created for non-successfully set up mappings
        return mapping.HasSetupSucceeded
            ? CreateClientForMapping(mapping.SyncMethod, mapping, settings)
            : new OfflineFileSystemClient<long>();
    }

    private IFileSystemClient<long> CreateClientForSharedWithMeItemMapping(
        RemoteToLocalMapping mapping,
        RemoteToLocalMapping? parentMapping,
        LocalAdapterSettings settings,
        IFileSystemClient<long>? aggregatingClient)
    {
        if (!mapping.HasSetupSucceeded || parentMapping == null || aggregatingClient == null)
        {
            // A dummy (offline) client is created for non-successfully set up mappings
            // and when the shared with me root folder (parent) mapping is non successfully setup
            return new OfflineFileSystemClient<long>();
        }

        return mapping.Remote.RootItemType switch
        {
            LinkType.Folder when !mapping.Remote.IsReadOnly => CreateClientForSharedWithMeWritableFolderMapping(mapping, settings, aggregatingClient),
            LinkType.Folder when mapping.Remote.IsReadOnly => CreateClientForSharedWithMeReadOnlyFolderMapping(mapping, settings, aggregatingClient),
            LinkType.File when !mapping.Remote.IsReadOnly => CreateClientForSharedWithMeWritableFileMapping(mapping, parentMapping, settings, aggregatingClient),
            LinkType.File when mapping.Remote.IsReadOnly => CreateClientForSharedWithMeReadOnlyFileMapping(mapping, parentMapping, aggregatingClient),
            _ => throw new InvalidEnumArgumentException(nameof(mapping.Remote.RootItemType), (int)mapping.Remote.RootItemType, typeof(LinkType)),
        };
    }

    private IFileSystemClient<long> CreateClientForSharedWithMeWritableFolderMapping(
        RemoteToLocalMapping mapping,
        LocalAdapterSettings settings,
        IFileSystemClient<long> aggregatingClient)
    {
        var rootedClient = new RootedFileSystemClientDecorator(
            new LocalRootDirectory(mapping.Local),
            aggregatingClient);

        return AddCommonDecorators(mapping, settings, rootedClient);
    }

    private IFileSystemClient<long> CreateClientForSharedWithMeReadOnlyFolderMapping(
        RemoteToLocalMapping mapping,
        LocalAdapterSettings settings,
        IFileSystemClient<long> aggregatingClient)
    {
        var protectingClient = new ProtectingFolderFileSystemClientDecorator(_folderStructureProtector, aggregatingClient);

        var rootedClient = new RootedFileSystemClientDecorator(
            new LocalRootDirectory(mapping.Local),
            protectingClient);

        var readOnlyClient = new ReadOnlyFileSystemClientDecorator(rootedClient);

        return AddCommonDecorators(mapping, settings, readOnlyClient);
    }

    private IFileSystemClient<long> CreateClientForSharedWithMeWritableFileMapping(
        RemoteToLocalMapping mapping,
        RemoteToLocalMapping parentMapping,
        LocalAdapterSettings settings,
        IFileSystemClient<long> aggregatingClient)
    {
        var localFileName = Path.GetFileName(mapping.Local.Path);
        var parentFolderId = parentMapping.Local.RootFolderId;
        var parentFolderPath = parentMapping.Local.Path;

        Ensure.NotNullOrEmpty(localFileName, nameof(localFileName));

        var rootedClient = new RootedFileSystemClientDecorator(
            new LocalRootDirectory(parentFolderPath, parentFolderId),
            aggregatingClient);

        var virtualRootClient = new VirtualFileRootFileSystemClientDecorator(
            parentFolderId,
            localFileName,
            rootedClient);

        var backingUpClient = new BackingUpFileSystemClientDecorator<long>(
            _loggerFactory.CreateLogger<BackingUpFileSystemClientDecorator<long>>(),
            new FileNameFactory<long>(settings.EditConflictNamePattern),
            virtualRootClient);

        var transferAbortionClient = new TransferAbortionCapableFileSystemClientDecorator<long>(
            _fileTransferAbortionStrategy,
            backingUpClient);

        return transferAbortionClient;
    }

    private IFileSystemClient<long> CreateClientForSharedWithMeReadOnlyFileMapping(
        RemoteToLocalMapping mapping,
        RemoteToLocalMapping parentMapping,
        IFileSystemClient<long> aggregatingClient)
    {
        var localFileName = Path.GetFileName(mapping.Local.Path);
        var parentFolderId = parentMapping.Local.RootFolderId;
        var parentFolderPath = parentMapping.Local.Path;

        Ensure.NotNullOrEmpty(localFileName, nameof(localFileName));

        var protectingClient = new ProtectingFileFileSystemClientDecorator(_folderStructureProtector, aggregatingClient);

        var rootedClient = new RootedFileSystemClientDecorator(
            new LocalRootDirectory(parentFolderPath, parentFolderId),
            protectingClient);

        var readOnlyClient = new ReadOnlyFileSystemClientDecorator(rootedClient);

        var virtualRootClient = new VirtualFileRootFileSystemClientDecorator(
            parentFolderId,
            localFileName,
            readOnlyClient);

        var transferAbortionClient = new TransferAbortionCapableFileSystemClientDecorator<long>(
            _fileTransferAbortionStrategy,
            virtualRootClient);

        return transferAbortionClient;
    }

    private IFileSystemClient<long> CreateClientForMapping(SyncMethod syncMethod, RemoteToLocalMapping mapping, LocalAdapterSettings settings)
    {
        var localRootDirectory = new LocalRootDirectory(mapping.Local);

        if (!_volumeInfoProvider.IsNtfsFileSystem(localRootDirectory.Path))
        {
            throw new InvalidFileSystemException("File system is not NTFS");
        }

        var client = syncMethod switch
        {
            SyncMethod.Classic => new RootedFileSystemClientDecorator(
                localRootDirectory,
                new LocalSpaceCheckingFileSystemClientDecorator<long>(
                    localRootDirectory.Path,
                    _volumeInfoProvider,
                    CreateUndecoratedClient(syncMethod))),

            // Local volume space checking decorator is not needed when using
            // the on-demand hydration file system client
            SyncMethod.OnDemand => new RootedFileSystemClientDecorator(
                localRootDirectory,
                CreateUndecoratedClient(syncMethod)),

            _ => throw new InvalidEnumArgumentException(nameof(syncMethod), (int)syncMethod, typeof(SyncMethod)),
        };

        return AddCommonDecorators(mapping, settings, client);
    }

    private IFileSystemClient<long> AddCommonDecorators(RemoteToLocalMapping mapping, LocalAdapterSettings settings, IFileSystemClient<long> client)
    {
        var rootFolder = new SpecialRootFolder<long>(mapping.Local.RootFolderId);

        var localTempFolder = new HiddenSpecialFolder<long>(
            settings.TempFolderName,
            rootFolder,
            client,
            _loggerFactory.CreateLogger<HiddenSpecialFolder<long>>());

        var localTrashFolder = new LocalTrash<long>(
            settings.TrashFolderName,
            localTempFolder,
            client,
            new ThreadPoolScheduler(),
            TimeSpan.FromMinutes(5),
            _loggerFactory.CreateLogger<LocalTrash<long>>());

        if (!mapping.Remote.IsReadOnly)
        {
            client = new BackingUpFileSystemClientDecorator<long>(
                _loggerFactory.CreateLogger<BackingUpFileSystemClientDecorator<long>>(),
                new FileNameFactory<long>(settings.EditConflictNamePattern),
                client);
        }

        var deletionFallbackClient = new PermanentDeletionFallbackFileSystemClientDecorator<long>(
            _loggerFactory.CreateLogger<PermanentDeletionFallbackFileSystemClientDecorator<long>>(),
            localTrashFolder,
            new FileNameFactory<long>(settings.DeletedNamePattern),
            client);

        var transferAbortionClient = new TransferAbortionCapableFileSystemClientDecorator<long>(
            _fileTransferAbortionStrategy,
            deletionFallbackClient);

        return transferAbortionClient;
    }

    private IFileSystemClient<long> CreateUndecoratedClient(SyncMethod syncMethod)
    {
        return syncMethod switch
        {
            SyncMethod.Classic => CreateUndecoratedClassicClient(),
            SyncMethod.OnDemand => CreateUndecoratedOnDemandHydrationClient(),
            _ => throw new InvalidEnumArgumentException(nameof(syncMethod), (int)syncMethod, typeof(SyncMethod)),
        };
    }

    private IFileSystemClient<long> CreateUndecoratedClassicClient()
    {
        return _undecoratedClassicClientFactory.Invoke();
    }

    private IFileSystemClient<long> CreateUndecoratedOnDemandHydrationClient()
    {
        return _undecoratedOnDemandHydrationClientFactory.Invoke();
    }
}
