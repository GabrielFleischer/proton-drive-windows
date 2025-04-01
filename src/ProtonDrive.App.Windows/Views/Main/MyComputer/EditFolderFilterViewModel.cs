using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ProtonDrive.App.Mapping.SyncFolders;
using ProtonDrive.App.Sync;

namespace ProtonDrive.App.Windows.Views.Main.MyComputer;

internal sealed class EditFolderFilterViewModel : ObservableObject, IEquatable<SyncFolder>, IDialogViewModel
{
    private readonly ISyncFolderService _syncFolderService;
    private readonly ISyncService _syncService;

    private SyncFolder? _syncFolder;

    private string _includes = PadLines("");
    private string _excludes = PadLines("");
    
    private bool _isFilterEdited;
    
    public ICommand SaveFilterCommand { get; }

    public EditFolderFilterViewModel(ISyncFolderService syncFolderService, ISyncService syncService)
    {
        _syncFolderService = syncFolderService;
        _syncService = syncService;
        
        SaveFilterCommand = new AsyncRelayCommand(SaveFilter);
    }

    public string Includes
    {
        get => _includes;
        set
        {
            value = PadLines(value);
            SetProperty(ref _includes, value);
        }
    }

    public string Excludes
    {
        get => _excludes;
        set
        {
            value = PadLines(value);
            SetProperty(ref _excludes, value);
        }
    }

    private static string PadLines(string value)
    {
        var curLines = value.Count(c => c == '\n');
        for (var i = curLines; i < 5; i++)
        {
            value += "\n";
        }

        return value;
    }

    public void SetSyncFolder(SyncFolder syncFolder)
    {
        _syncFolder = syncFolder;
        Includes = syncFolder.Filter.Includes;
        Excludes = syncFolder.Filter.Excludes;
    }
    
    
    public bool FilterEdited
    {
        get => _isFilterEdited;
        private set => SetProperty(ref _isFilterEdited, value);
    }

    private async Task SaveFilter(CancellationToken cancellationToken)
    {
        await _syncFolderService.EditFolderFilter(_syncFolder!, _includes, _excludes, cancellationToken);
        FilterEdited = true;

        // This is probably overkill, but I did not find a better way to forcefully apply the new filters
        await _syncService.RestartAsync();
    }
    
    public bool Equals(SyncFolder? other)
    {
        // ReSharper disable once PossibleUnintendedReferenceComparison
        return other is not null && _syncFolder == other;
    }

    public string Title => Resources.Strings.Main_Window_Title;
}
