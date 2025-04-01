using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using ProtonDrive.Sync.Shared.FileSystem;

namespace ProtonDrive.App.FileExclusion;

public class FileExclusionFileSystemClient<TId> : FileSystemClientDecoratorBase<TId>
    where TId : IEquatable<TId>
{
    private readonly ILogger<FileExclusionFileSystemClient<TId>> _logger;
    private readonly FileFilter _fileFilter;

    public FileExclusionFileSystemClient(ILogger<FileExclusionFileSystemClient<TId>> logger,
        FileFilter fileFilter,
        IFileSystemClient<TId> instanceToDecorate) 
        : base(instanceToDecorate)
    {
        _logger = logger;
        _fileFilter = fileFilter;
    }

    public override IAsyncEnumerable<NodeInfo<TId>> Enumerate(NodeInfo<TId> info, CancellationToken cancellationToken)
    {
        return base.Enumerate(info, cancellationToken)
            .Where(ApplyFilter);
    }

    private bool ApplyFilter(NodeInfo<TId> info)
    {
        if (!_fileFilter.ShouldExcludeFile(info.Path))
        {
           return true;
        }

        _logger.LogDebug("File excluded from enumeration : \"{Root}\"/\"{Path}\"/{ParentId}/{Id}",
            info.Root?.Id,
            info.Path,
            info.ParentId,
            info.Id);
        
        return false;
    }
}
