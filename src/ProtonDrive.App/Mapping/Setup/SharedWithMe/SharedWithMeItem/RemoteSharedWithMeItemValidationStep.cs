﻿using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ProtonDrive.App.Settings;
using ProtonDrive.Client;
using ProtonDrive.Client.Contracts;
using ProtonDrive.Client.Shares.SharedWithMe;
using ProtonDrive.Shared.Extensions;

namespace ProtonDrive.App.Mapping.Setup.SharedWithMe.SharedWithMeItem;

internal class RemoteSharedWithMeItemValidationStep : IRemoteSharedWithMeItemValidationStep
{
    private readonly ISharedWithMeClient _sharedWithMeClient;
    private readonly VolumeIdentityProvider _volumeIdentityProvider;
    private readonly ILogger<RemoteSharedWithMeItemValidationStep> _logger;

    public RemoteSharedWithMeItemValidationStep(
        ISharedWithMeClient sharedWithMeClient,
        VolumeIdentityProvider volumeIdentityProvider,
        ILogger<RemoteSharedWithMeItemValidationStep> logger)
    {
        _sharedWithMeClient = sharedWithMeClient;
        _volumeIdentityProvider = volumeIdentityProvider;
        _logger = logger;
    }

    public async Task<MappingErrorCode?> ValidateAsync(RemoteToLocalMapping mapping, CancellationToken cancellationToken)
    {
        try
        {
            return await ValidateRemoteItemAsync(mapping.Remote, cancellationToken).ConfigureAwait(false);
        }
        catch (ApiException ex) when (ex.ResponseCode == ResponseCode.DoesNotExist)
        {
            _logger.LogError("Remote shared with me item does not exist: {ErrorMessage}", ex.CombinedMessage());
            return MappingErrorCode.DriveFolderDoesNotExist;
        }
        catch (Exception ex) when (ex.IsDriveClientException())
        {
            _logger.LogError("Remote shared with me item validation failed: {ErrorMessage}", ex.CombinedMessage());
            return MappingErrorCode.DriveAccessFailed;
        }
    }

    private static LinkType GetLinkType(Client.Shares.SharedWithMe.SharedWithMeItem item)
    {
        return item.IsFolder ? LinkType.Folder : LinkType.File;
    }

    private async Task<MappingErrorCode?> ValidateRemoteItemAsync(RemoteReplica replica, CancellationToken cancellationToken)
    {
        if (replica.ShareId is null)
        {
            ArgumentNullException.ThrowIfNull(replica.ShareId);
        }

        var sharedWithMeItem = await _sharedWithMeClient.GetSharedWithMeItemAsync(replica.ShareId, cancellationToken).ConfigureAwait(false);

        if (sharedWithMeItem is null)
        {
            _logger.LogError("Remote shared with me item does not exist");
            return MappingErrorCode.RemoteShareWithMeItemDoesNotExist;
        }

        var itemType = GetLinkType(sharedWithMeItem);

        if (replica.RootItemType != itemType)
        {
            _logger.LogError("Remote shared with me item type is not expected: {ItemType}", itemType);
            return MappingErrorCode.RemoteShareWithMeItemDoesNotExist;
        }

        if (sharedWithMeItem.VolumeId != replica.VolumeId
            || sharedWithMeItem.LinkId != replica.RootLinkId)
        {
            _logger.LogError("Remote shared with me item has diverged: Remote Volume ID or Link ID is not expected");
            return MappingErrorCode.RemoteShareWithMeItemDoesNotExist;
        }

        if (sharedWithMeItem.IsReadOnly != replica.IsReadOnly)
        {
            _logger.LogError(
                "Remote shared with me item permissions have diverged: Expected IsReadOnly={ExpectedReadOnlyFlag}, but found {ActualReadOnlyFlag}",
                replica.IsReadOnly,
                sharedWithMeItem.IsReadOnly);

            return MappingErrorCode.RemoteShareWithMeItemPermissionsDiverged;
        }

        replica.RootItemName = sharedWithMeItem.Name;

        AddMissingVolumeInfo(replica);

        return default;
    }

    private void AddMissingVolumeInfo(RemoteReplica replica)
    {
        if (replica.InternalVolumeId != default || string.IsNullOrEmpty(replica.VolumeId))
        {
            return;
        }

        // Shared with me folder mappings have unique internal volume IDs, which makes all remote roots belong to different virtual volumes
        replica.InternalVolumeId = replica.RootItemType is LinkType.Folder
            ? _volumeIdentityProvider.GetUniqueRemoteVolumeId()
            : _volumeIdentityProvider.GetRemoteVolumeId(replica.VolumeId);
    }
}
