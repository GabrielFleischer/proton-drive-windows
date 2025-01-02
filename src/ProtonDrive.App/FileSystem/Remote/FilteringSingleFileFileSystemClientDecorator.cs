﻿using System;
using System.Threading;
using System.Threading.Tasks;
using ProtonDrive.Shared.IO;
using ProtonDrive.Sync.Shared.FileSystem;

namespace ProtonDrive.App.FileSystem.Remote;

internal sealed class FilteringSingleFileFileSystemClientDecorator : FileSystemClientDecoratorBase<string>
{
    public FilteringSingleFileFileSystemClientDecorator(IFileSystemClient<string> instanceToDecorate)
        : base(instanceToDecorate)
    {
    }

    public override Task<NodeInfo<string>> CreateDirectory(NodeInfo<string> info, CancellationToken cancellationToken)
    {
        throw GetException();
    }

    public override Task Delete(NodeInfo<string> info, CancellationToken cancellationToken)
    {
        throw GetException();
    }

    public override Task DeletePermanently(NodeInfo<string> info, CancellationToken cancellationToken)
    {
        throw GetException();
    }

    public override Task<IRevisionCreationProcess<string>> CreateFile(NodeInfo<string> info, string? tempFileName, IThumbnailProvider thumbnailProvider, Action<Progress>? progressCallback, CancellationToken cancellationToken)
    {
        throw GetException();
    }

    public override Task Move(NodeInfo<string> info, NodeInfo<string> destinationInfo, CancellationToken cancellationToken)
    {
        throw GetException();
    }

    private static FileSystemClientException GetException()
    {
        return new FileSystemClientException(string.Empty, FileSystemErrorCode.MissingIndividuallySharedFile);
    }
}
