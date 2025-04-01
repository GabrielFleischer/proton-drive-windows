using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using ProtonDrive.App.Account;
using ProtonDrive.App.FileExclusion;
using ProtonDrive.App.Settings;
using ProtonDrive.Client.Contracts;
using ProtonDrive.Sync.Adapter;
using ProtonDrive.Sync.Shared.FileSystem;

namespace ProtonDrive.App.FileSystem.Remote;

internal sealed class RemoteDecoratedFileSystemClientFactory
{
    private readonly Func<FileSystemClientParameters, IFileSystemClient<string>> _undecoratedClientFactory;
    private readonly IUserService _userService;
    private readonly ISwitchingToVolumeEventsHandler _switchingToVolumeEventsHandler;
    private readonly ILoggerFactory _loggerFactory;

    public RemoteDecoratedFileSystemClientFactory(
        Func<FileSystemClientParameters, IFileSystemClient<string>> undecoratedClientFactory,
        IUserService userService,
        ISwitchingToVolumeEventsHandler switchingToVolumeEventsHandler,
        ILoggerFactory loggerFactory)
    {
        _undecoratedClientFactory = undecoratedClientFactory;
        _userService = userService;
        _switchingToVolumeEventsHandler = switchingToVolumeEventsHandler;
        _loggerFactory = loggerFactory;
    }

    public IFileSystemClient<string> GetClient(
        IReadOnlyCollection<RemoteToLocalMapping> mappings,
        IRevisionUploadAttemptRepository revisionUploadAttemptRepository)
    {
        // A dummy (offline) client is created for non-successfully set up mappings.
        // If all mappings on the same remote share are non-successfully set up,
        // the remote file system client for share is still created, but gets abandoned.
        var rootToClientMap =
            GetOwnVolumeRootToClientMap(mappings, revisionUploadAttemptRepository)
                .Concat(GetForeignVolumeRootToClientMap(mappings, revisionUploadAttemptRepository))
                .ToDictionary(x => x.Root, x => x.Client);

        return
            new LoggingFileSystemClientDecorator<string>(
                _loggerFactory.CreateLogger<LoggingFileSystemClientDecorator<string>>(),
                new DispatchingFileSystemClient<string>(rootToClientMap));
    }

    private IFileSystemClient<string> CreateClientForMapping(RemoteToLocalMapping mapping, IFileSystemClient<string> clientForShare)
    {
        var exclusionClient = new FileExclusionFileSystemClient<string>(
            _loggerFactory.CreateLogger<FileExclusionFileSystemClient<string>>(),
            mapping.Filter,
            clientForShare
        );
        
        return mapping.HasSetupSucceeded
            ? new RootedFileSystemClientDecorator(new RemoteRootDirectory(mapping), exclusionClient)
            : new OfflineFileSystemClient<string>();
    }

    private IEnumerable<(RootInfo<string> Root, IFileSystemClient<string> Client)> GetOwnVolumeRootToClientMap(
        IEnumerable<RemoteToLocalMapping> mappings,
        IRevisionUploadAttemptRepository revisionUploadAttemptRepository)
    {
        return from mapping in mappings
               where mapping.Type is MappingType.CloudFiles or MappingType.ForeignDevice or MappingType.HostDeviceFolder
               group mapping by (mapping.Remote.VolumeId, mapping.Remote.ShareId) into shareMappings
               let shareClient = CreateOwnVolumeClientForShare(shareMappings.Key.VolumeId, shareMappings.Key.ShareId, revisionUploadAttemptRepository)
               from shareMapping in shareMappings
               select (
                   Root: CreateRoot(shareMapping),
                   Client: CreateClientForMapping(shareMapping, shareClient));
    }

    private IEnumerable<(RootInfo<string> Root, IFileSystemClient<string> Client)> GetForeignVolumeRootToClientMap(
        IReadOnlyCollection<RemoteToLocalMapping> mappings,
        IRevisionUploadAttemptRepository revisionUploadAttemptRepository)
    {
        var sharedWithMeRootFolderMapping = mappings.FirstOrDefault(m => m.Type is MappingType.SharedWithMeRootFolder);

        if (sharedWithMeRootFolderMapping is null)
        {
            return mappings
                .Where(x => x.Type is MappingType.SharedWithMeItem)
                .Select(mapping => (
                        Root: CreateRoot(mapping),
                        Client: (IFileSystemClient<string>)new OfflineFileSystemClient<string>()));
        }

        return from mapping in mappings
               where mapping.Type is MappingType.SharedWithMeItem
               let client = CreateClientForSharedWithMeItem(mapping, revisionUploadAttemptRepository)
               select (
                   Root: CreateRoot(mapping),
                   Client: CreateClientForMapping(mapping, client));
    }

    private RootInfo<string> CreateRoot(RemoteToLocalMapping mapping)
    {
        var isUsingOwnVolumeEvents = _switchingToVolumeEventsHandler.HasSwitched;
        var isOwnVolume = mapping.Type is MappingType.CloudFiles or MappingType.HostDeviceFolder or MappingType.ForeignDevice;
        var nodeId = mapping.Remote.RootItemType is LinkType.Folder
            ? mapping.Remote.RootLinkId ?? throw new InvalidOperationException()
            : RootPropertyProvider.GetVirtualRootFolderId(mapping.Id);

        return new RootInfo<string>(
            Id: mapping.Id,
            VolumeId: mapping.Remote.InternalVolumeId,
            nodeId)
        {
            // Remote events are retrieved per volume, remote InternalVolumeId serves as an event scope.
            // Except for own volume if switching to volume events has not succeeded,
            // then remote ShareId serves as an event scope.
            EventScope = (isOwnVolume && !isUsingOwnVolumeEvents
                    ? mapping.Remote.ShareId
                    : RootPropertyProvider.GetEventScope(mapping.Remote.InternalVolumeId))
                ?? throw new InvalidOperationException(),

            // Moving between local sync folders is not currently supported
            MoveScope = mapping.Id,
            LocalPath = mapping.Remote.RootItemType is LinkType.File
                ? Path.GetDirectoryName(mapping.Local.Path.AsSpan()).ToString()
                : mapping.Local.Path,
            IsEnabled = mapping.HasSetupSucceeded,
        };
    }

    private IFileSystemClient<string> CreateOwnVolumeClientForShare(
        string volumeId,
        string shareId,
        IRevisionUploadAttemptRepository revisionUploadAttemptRepository)
    {
        var parameters = new FileSystemClientParameters(volumeId, shareId);

        var undecoratedClient = CreateUndecoratedClient(parameters);

        var draftCleaningClient = new DraftCleaningFileSystemClientDecorator(revisionUploadAttemptRepository, undecoratedClient);

        return new RemoteSpaceCheckingFileSystemClientDecorator(_userService, new StorageReservationHandler(), draftCleaningClient);
    }

    private IFileSystemClient<string> CreateClientForSharedWithMeItem(
        RemoteToLocalMapping mapping,
        IRevisionUploadAttemptRepository revisionUploadAttemptRepository)
    {
        var volumeId = mapping.Remote.VolumeId ?? throw new InvalidOperationException("Remote volume ID is not specified");
        var shareId = mapping.Remote.ShareId ?? throw new InvalidOperationException("Remote share ID is not specified");

        var undecoratedClient = mapping.Remote.RootItemType is LinkType.File
            ? CreateUndecoratedClientForFile()
            : CreateUndecoratedClientForFolder();

        IFileSystemClient<string> decoratedClient = mapping.Remote.IsReadOnly
            ? new ReadOnlyFileSystemClientDecorator(undecoratedClient)
            : new DraftCleaningFileSystemClientDecorator(revisionUploadAttemptRepository, undecoratedClient);

        if (mapping.Remote.RootItemType is LinkType.File)
        {
            decoratedClient = new FilteringSingleFileFileSystemClientDecorator(decoratedClient);
        }

        return decoratedClient;

        IFileSystemClient<string> CreateUndecoratedClientForFile()
        {
            var virtualFolderId = RootPropertyProvider.GetVirtualRootFolderId(mapping.Id);
            var linkId = mapping.Remote.RootLinkId;
            var linkName = Path.GetFileName(mapping.Local.Path.AsSpan()).ToString();
            var parameters = new FileSystemClientParameters(volumeId, shareId, virtualFolderId, linkId, linkName);

            return CreateUndecoratedClient(parameters);
        }

        IFileSystemClient<string> CreateUndecoratedClientForFolder()
        {
            return CreateUndecoratedClient(new FileSystemClientParameters(volumeId, shareId));
        }
    }

    private IFileSystemClient<string> CreateUndecoratedClient(FileSystemClientParameters parameters)
    {
        return _undecoratedClientFactory.Invoke(parameters);
    }
}
