using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ProtonDrive.Sync.Shared.FileSystem;

namespace ProtonDrive.App.FileExclusion;

public class FileExclusionEventLogClient<TId> : IEventLogClient<TId>
{
    private readonly ILogger<FileExclusionEventLogClient<TId>> _logger;
    private readonly FileFilter _filter;
    private readonly IEventLogClient<TId> _decoratedInstance;

    public FileExclusionEventLogClient(ILogger<FileExclusionEventLogClient<TId>> logger, FileFilter filter, IEventLogClient<TId> decoratedInstance)
    {
        _logger = logger;
        _filter = filter;
        _decoratedInstance = decoratedInstance;
        
        _decoratedInstance.LogEntriesReceived += OnDecoratedInstanceLogEntriesReceived;
    }

    private void OnDecoratedInstanceLogEntriesReceived(object? sender, EventLogEntriesReceivedEventArgs<TId> eventArgs)
    {
        var filteredEntries = eventArgs.Entries
            .Select(TransformEntry)
            // Drop filtered out (nulls)
            .OfType<EventLogEntry<TId>>()
            .ToList();

        var newEvent = new EventLogEntriesReceivedEventArgs<TId>(filteredEntries, eventArgs.ConsiderEventsProcessed)
        {
            VolumeId = eventArgs.VolumeId,
            Scope = eventArgs.Scope,
        };
        
        LogEntriesReceived?.Invoke(this, newEvent);
    }

    private EventLogEntry<TId>? TransformEntry(EventLogEntry<TId> entry)
    {
        if (entry.Path == null)
        {
            return entry;
        }

        if (entry is not { ChangeType: EventLogChangeType.Moved, OldPath: not null })
        {
            if (!_filter.ShouldExcludeFile(entry.Path))
            {
                return entry;
            }

            LogEntry("Excluding move event", entry);
                
            return null;
        }

        // If the file is moved, we might need to transform the operation
        var wasExcluded = _filter.ShouldExcludeFile(entry.OldPath);
        var isExcluded = _filter.ShouldExcludeFile(entry.Path);

        var newEntry = ((wasExcluded, isExcluded)) switch
        {
            (true, true) => null,
            (false, false) => entry,
            (true, false) => WithChangeType(entry, EventLogChangeType.CreatedOrMovedTo),
            (false, true) => WithChangeType(entry, EventLogChangeType.DeletedOrMovedFrom),
        };

        if (newEntry == null)
        {
            LogEntry("Excluding move event", entry);
        }
        else if (newEntry != entry)
        {
            LogEntry("Transforming move entry", newEntry);
        }
        
        return newEntry;
    }

    private static EventLogEntry<TId> WithChangeType(EventLogEntry<TId> e, EventLogChangeType changeType) =>
        new(changeType)
        {
            Name = e.Name,
            Path = e.Path,
            OldPath = null,
            Id = e.Id,
            ParentId = e.ParentId,
            RevisionId = e.RevisionId,
            Attributes = e.Attributes,
            LastWriteTimeUtc = e.LastWriteTimeUtc,
            Size = e.Size,
            SizeOnStorage = e.SizeOnStorage,
            PlaceholderState = e.PlaceholderState,
        };

    public event EventHandler<EventLogEntriesReceivedEventArgs<TId>>? LogEntriesReceived;
    
    public void Enable() => _decoratedInstance.Enable();

    public void Disable() => _decoratedInstance.Disable();

    public Task GetEventsAsync() => _decoratedInstance.GetEventsAsync();

    private void LogEntry(string message, EventLogEntry<TId> entry)
    {
        _logger.LogDebug("{log}: {changeType} {Type} \"{path}\"/{ParentId}/{Id}, Attributes=({Attributes}), PlaceholderState=({PlaceholderState}), LastWriteTime={LastWriteTime:O}, Size={Size}, Revision={RevisionId}",
            message,
            entry.ChangeType,
            entry.Attributes.HasFlag(FileAttributes.Directory) ? "Directory" : "File",
            entry.Path,
            entry.ParentId,
            entry.Id,
            entry.Attributes,
            entry.PlaceholderState,
            entry.LastWriteTimeUtc,
            entry.Size ?? entry.SizeOnStorage,
            entry.RevisionId);
    }
}
