﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using ProtonDrive.Sync.Adapter.NodeCopying;
using ProtonDrive.Sync.Adapter.Shared;
using ProtonDrive.Sync.Adapter.Trees.Adapter;
using ProtonDrive.Sync.Shared;
using ProtonDrive.Sync.Shared.FileSystem;
using ProtonDrive.Sync.Shared.Trees.FileSystem;

namespace ProtonDrive.Sync.Adapter.UpdateDetection.StateBased.Enumeration;

internal sealed class RootEnumerationSuccessStep<TId, TAltId> : SuccessStep<TId, TAltId>
    where TId : IEquatable<TId>
    where TAltId : IEquatable<TAltId>
{
    private readonly AdapterTree<TId, TAltId> _adapterTree;
    private readonly Dictionary<TId, RootInfo<TAltId>> _syncRoots;
    private readonly RootMigrationStep<TId, TAltId> _rootMigration;
    private readonly ILogger<RootEnumerationSuccessStep<TId, TAltId>> _logger;

    public RootEnumerationSuccessStep(
        AdapterTree<TId, TAltId> adapterTree,
        IDirtyNodes<TId, TAltId> dirtyNodes,
        IIdentitySource<TId> idSource,
        Dictionary<TId, RootInfo<TAltId>> syncRoots,
        ICopiedNodes<TId, TAltId> copiedNodes,
        NodeUpdateDetection<TId, TAltId> nodeUpdateDetection,
        IItemExclusionFilter itemExclusionFilter,
        RootMigrationStep<TId, TAltId> rootMigration,
        ILogger<RootEnumerationSuccessStep<TId, TAltId>> logger)
        : base(logger, adapterTree, dirtyNodes, idSource, nodeUpdateDetection, syncRoots, copiedNodes, itemExclusionFilter)
    {
        _adapterTree = adapterTree;
        _syncRoots = syncRoots;
        _rootMigration = rootMigration;
        _logger = logger;
    }

    public void Execute(
        RootInfo<TAltId> rootInfo,
        IDictionary<TId, AdapterTreeNode<TId, TAltId>> unprocessedSyncRootNodes)
    {
        var incomingNodeModel = new IncomingAdapterTreeNodeModel<TId, TAltId>
        {
            Type = NodeType.Directory,
            Name = rootInfo.Id.ToString(),
            AltId = (rootInfo.VolumeId, rootInfo.NodeId),
            ParentId = _adapterTree.Root.Id,
            Status = AdapterNodeStatus.DirtyDescendants,
        };

        var existingNode = ExistingSyncRootNode(incomingNodeModel);

        if (existingNode != null)
        {
            // Existing node
            unprocessedSyncRootNodes.Remove(existingNode.Id);

            if (existingNode.AltId.VolumeId != rootInfo.VolumeId)
            {
                if (existingNode.AltId.VolumeId == 0)
                {
                    // Migrate sync root branch to the desired volume
                    _rootMigration.MigrateSyncRootToVolume(existingNode, rootInfo.VolumeId);
                }
                else
                {
                    // Existing sync root belongs to an unexpected volume
                    throw new InvalidOperationException(
                        $"Adapter Tree sync root node \"{rootInfo.Id}\" with Id={existingNode.Id} belongs to a volume with Id={existingNode.AltId.VolumeId}, " +
                        $"but root VolumeId is {rootInfo.VolumeId}");
                }
            }
        }
        else
        {
            // A new node
            ValidateAndUpdate(null, incomingNodeModel, _adapterTree.Root);

            existingNode = ExistingNode(incomingNodeModel) ?? throw new InvalidOperationException();
        }

        _syncRoots.Add(existingNode.Id, rootInfo);
    }

    private AdapterTreeNode<TId, TAltId>? ExistingSyncRootNode(IncomingAdapterTreeNodeModel<TId, TAltId> incomingNodeModel)
    {
        var existingNode = ExistingNode(incomingNodeModel.AltId, incomingNodeModel.Type) ??
                           ExistingSyncRootNodeByName(incomingNodeModel) ??
                           /* Looking for node not migrated to the specific volume */
                           ExistingNode((0, incomingNodeModel.AltId.ItemId), incomingNodeModel.Type);

        if (existingNode is null)
        {
            return null;
        }

        if (!existingNode.IsSyncRoot())
        {
            // Not a sync root node cannot become a sync root.
            // The node is marked as deleted.
            var nodeModel = IncomingAdapterTreeNodeModel<TId, TAltId>
                .FromNodeModel(existingNode.Model)
                .WithAltId(default)
                .WithAppendedDirtyFlags(AdapterNodeStatus.DirtyDeleted);

            DetectNodeUpdate(existingNode, nodeModel);

            return null;
        }

        // Sync root nodes are additionally matched by name
        var nodeName = existingNode.Name;
        if (!nodeName.Equals(incomingNodeModel.Name, StringComparison.Ordinal))
        {
            // Not matching root names means that sync root was replaced with
            // a new one based on the same file system folder, therefore AltId
            // has not changed.
            var nodeModel = IncomingAdapterTreeNodeModel<TId, TAltId>
                .FromNodeModel(existingNode.Model)
                .WithAltId(default);

            DetectNodeUpdate(existingNode, nodeModel);

            return null;
        }

        return existingNode;
    }

    private AdapterTreeNode<TId, TAltId>? ExistingSyncRootNodeByName(IncomingAdapterTreeNodeModel<TId, TAltId> incomingNodeModel)
    {
        var existingNode = _adapterTree.Root.ChildrenByName(incomingNodeModel.Name)
            .SingleOrDefault(r => r.AltId.VolumeId != 0);

        if (existingNode == null)
        {
            return null;
        }

        // Sync root nodes are additionally matched by AltId
        if (existingNode.AltId.Equals(incomingNodeModel.AltId))
        {
            return existingNode;
        }

        // Same AltId must not exist in the tree
        var node = ExistingNode(incomingNodeModel.AltId);
        if (node != null)
        {
            // The node is marked as deleted
            var nodeModel = IncomingAdapterTreeNodeModel<TId, TAltId>
                .FromNodeModel(node.Model)
                .WithAltId(default)
                .WithAppendedDirtyFlags(AdapterNodeStatus.DirtyDeleted);

            DetectNodeUpdate(node, nodeModel);
        }

        _logger.LogWarning(
            "Updating Adapter Tree sync root node with Id={Id} (name={Name}) AltId value from {PrevAltId} to {NewAltId}",
            existingNode.Id,
            existingNode.Name,
            existingNode.AltId,
            incomingNodeModel.AltId);

        // Not matching AltId means that sync root folder was replaced with a new one,
        // but the content remains same. Therefore, we update AltId on the sync root node.
        DetectNodeUpdate(existingNode, incomingNodeModel);

        return existingNode;
    }
}
