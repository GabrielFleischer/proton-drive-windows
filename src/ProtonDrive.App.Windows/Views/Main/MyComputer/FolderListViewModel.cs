﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ProtonDrive.App.Features;
using ProtonDrive.App.Mapping;
using ProtonDrive.App.Mapping.SyncFolders;
using ProtonDrive.App.Settings;
using ProtonDrive.App.SystemIntegration;
using ProtonDrive.App.Windows.Extensions;
using ProtonDrive.App.Windows.Services;
using ProtonDrive.App.Windows.SystemIntegration;
using ProtonDrive.Shared.Features;
using ProtonDrive.Shared.Threading;

namespace ProtonDrive.App.Windows.Views.Main.MyComputer;

internal sealed class FolderListViewModel : ObservableObject, ISyncFoldersAware, IFeatureFlagsAware
{
    private readonly IFileSystemDisplayNameAndIconProvider _fileSystemDisplayNameAndIconProvider;
    private readonly ISyncFolderService _syncFolderService;
    private readonly ILocalFolderService _localFolderService;
    private readonly IDialogService _dialogService;
    private readonly Func<AddFoldersViewModel> _addFoldersViewModelFactory;
    private readonly Func<RemoveClassicFolderConfirmationViewModel> _removeClassicFolderConfirmationViewModelFactory;
    private readonly Func<RemoveOnDemandFolderConfirmationViewModel> _removeOnDemandFolderConfirmationViewModelFactory;
    private readonly Func<EditFolderFilterViewModel> _editFilterViewModelFactory;
    private readonly Func<StorageOptimizationTurnedOffNotificationViewModel> _storageOptimizationTurnedOffNotificationViewModelFactory;
    private readonly Func<StorageOptimizationUnavailableNotificationViewModel> _storageOptimizationUnavailableNotificationViewModelFactory;
    private readonly IScheduler _scheduler;

    private bool _isStorageOptimizationFeatureEnabled = true;
    private bool _isShowingStorageOptimizationNotificationDialog;

    public FolderListViewModel(
        IFileSystemDisplayNameAndIconProvider fileSystemDisplayNameAndIconProvider,
        ISyncFolderService syncFolderService,
        ILocalFolderService localFolderService,
        IDialogService dialogService,
        Func<AddFoldersViewModel> addFoldersViewModelFactory,
        Func<RemoveClassicFolderConfirmationViewModel> removeClassicFolderConfirmationViewModelFactory,
        Func<RemoveOnDemandFolderConfirmationViewModel> removeOnDemandFolderConfirmationViewModelFactory,
        Func<EditFolderFilterViewModel> editFilterViewModelFactory,
        Func<StorageOptimizationTurnedOffNotificationViewModel> storageOptimizationTurnedOffNotificationViewModelFactory,
        Func<StorageOptimizationUnavailableNotificationViewModel> storageOptimizationUnavailableNotificationViewModelFactory,
        [FromKeyedServices("Dispatcher")] IScheduler scheduler)
    {
        _fileSystemDisplayNameAndIconProvider = fileSystemDisplayNameAndIconProvider;
        _syncFolderService = syncFolderService;
        _localFolderService = localFolderService;
        _dialogService = dialogService;
        _addFoldersViewModelFactory = addFoldersViewModelFactory;
        _removeClassicFolderConfirmationViewModelFactory = removeClassicFolderConfirmationViewModelFactory;
        _removeOnDemandFolderConfirmationViewModelFactory = removeOnDemandFolderConfirmationViewModelFactory;
        _editFilterViewModelFactory = editFilterViewModelFactory;
        _storageOptimizationTurnedOffNotificationViewModelFactory = storageOptimizationTurnedOffNotificationViewModelFactory;
        _storageOptimizationUnavailableNotificationViewModelFactory = storageOptimizationUnavailableNotificationViewModelFactory;
        _scheduler = scheduler;

        AddFoldersCommand = new RelayCommand(AddFolders);
        OpenFolderCommand = new AsyncRelayCommand<FolderViewModel?>(OpenFolderAsync);
        RemoveFolderCommand = new AsyncRelayCommand<FolderViewModel?>(RemoveFolderAsync);
        EditFolderFilterCommand = new RelayCommand<FolderViewModel?>(EditFolderFilter);
        ToggleStorageOptimizationCommand = new AsyncRelayCommand<FolderViewModel?>(ToggleStorageOptimizationAsync, CanToggleStorageOptimization);
    }

    public ObservableCollection<FolderViewModel> Items { get; } = [];

    public ICommand AddFoldersCommand { get; }
    public IAsyncRelayCommand<FolderViewModel?> OpenFolderCommand { get; }
    public IAsyncRelayCommand<FolderViewModel?> RemoveFolderCommand { get; }
    public IRelayCommand<FolderViewModel?> EditFolderFilterCommand { get; }
    public IAsyncRelayCommand<FolderViewModel?> ToggleStorageOptimizationCommand { get; }

    public bool IsStorageOptimizationFeatureEnabled
    {
        get => _isStorageOptimizationFeatureEnabled;
        private set => SetProperty(ref _isStorageOptimizationFeatureEnabled, value);
    }

    void IFeatureFlagsAware.OnFeatureFlagsChanged(IReadOnlyCollection<(Feature Feature, bool IsEnabled)> features)
    {
        IsStorageOptimizationFeatureEnabled = !features.IsEnabled(Feature.DriveWindowsStorageOptimizationDisabled);
        Schedule(ToggleStorageOptimizationCommand.NotifyCanExecuteChanged);
    }

    void ISyncFoldersAware.OnSyncFolderChanged(SyncFolderChangeType changeType, SyncFolder folder)
    {
        if (folder.Type is not SyncFolderType.HostDeviceFolder)
        {
            return;
        }

        Schedule(HandleSyncFolderChange);

        return;

        void HandleSyncFolderChange()
        {
            switch (changeType)
            {
                case SyncFolderChangeType.Added:

                    if (!_fileSystemDisplayNameAndIconProvider.TryGetDisplayNameAndIcon(folder.LocalPath, ShellIconSize.Small, out var name, out var icon))
                    {
                        name = Path.GetFileName(folder.LocalPath);
                        icon = _fileSystemDisplayNameAndIconProvider.GetFolderIconWithoutAccess(folder.LocalPath, ShellIconSize.Small);
                    }

                    Items.Add(new FolderViewModel(folder, name, icon));
                    break;

                case SyncFolderChangeType.Updated:
                    var item = Items.FirstOrDefault(syncedFolder => syncedFolder.Equals(folder));
                    HandleStorageOptimizationUnavailability(item, folder);
                    HandleStorageOptimizationTurnedOff(item, folder);
                    item?.Update();
                    ToggleStorageOptimizationCommand.NotifyCanExecuteChanged();
                    break;

                case SyncFolderChangeType.Removed:
                    Items.RemoveFirst(syncedFolder => syncedFolder.Equals(folder));
                    break;

                default:
                    throw new InvalidEnumArgumentException(nameof(changeType), (int)changeType, typeof(SyncFolderChangeType));
            }
        }
    }

    private void HandleStorageOptimizationUnavailability(FolderViewModel? folderViewModel, SyncFolder syncFolder)
    {
        if (_isShowingStorageOptimizationNotificationDialog)
        {
            return;
        }

        // The folder view model is not yet updated to reflect the latest status of SyncFolder,
        // so we get the previous state from the view model and the new state from the SyncFolder.
        if (folderViewModel?.StorageOptimizationStatus is not StorageOptimizationStatus.Pending || !folderViewModel.IsStorageOptimizationEnabled)
        {
            return;
        }

        if (syncFolder is not
            {
                Status: MappingSetupStatus.Succeeded,
                SyncMethod: SyncMethod.Classic,
                IsStorageOptimizationEnabled: false,
                StorageOptimizationErrorCode: not StorageOptimizationErrorCode.None,
            })
        {
            return;
        }

        var dialogViewModel = _storageOptimizationUnavailableNotificationViewModelFactory.Invoke();
        dialogViewModel.SetArguments(folderViewModel.Name, syncFolder.StorageOptimizationErrorCode, syncFolder.ConflictingProviderName);

        try
        {
            _isShowingStorageOptimizationNotificationDialog = true;
            _dialogService.ShowConfirmationDialog(dialogViewModel);
        }
        finally
        {
            _isShowingStorageOptimizationNotificationDialog = false;
        }
    }

    private void HandleStorageOptimizationTurnedOff(FolderViewModel? folderViewModel, SyncFolder syncFolder)
    {
        if (_isShowingStorageOptimizationNotificationDialog)
        {
            return;
        }

        // The folder view model is not yet updated to reflect the latest status of SyncFolder,
        // so we get the previous state from the view model and the new state from the SyncFolder.
        if (folderViewModel?.StorageOptimizationStatus is not StorageOptimizationStatus.Pending || folderViewModel.IsStorageOptimizationEnabled)
        {
            return;
        }

        if (syncFolder is not
            {
                Status: MappingSetupStatus.Succeeded,
                SyncMethod: SyncMethod.OnDemand,
                IsStorageOptimizationEnabled: false,
                StorageOptimizationErrorCode: StorageOptimizationErrorCode.None,
            })
        {
            return;
        }

        var dialogViewModel = _storageOptimizationTurnedOffNotificationViewModelFactory.Invoke();
        dialogViewModel.SetArguments(folderViewModel.Name);

        try
        {
            _isShowingStorageOptimizationNotificationDialog = true;
            _dialogService.ShowConfirmationDialog(dialogViewModel);
        }
        finally
        {
            _isShowingStorageOptimizationNotificationDialog = false;
        }
    }

    private void AddFolders()
    {
        var dialogViewModel = _addFoldersViewModelFactory.Invoke();
        dialogViewModel.RefreshSyncedFolders(Items.Select(x => x.Path).ToHashSet());
        _dialogService.ShowDialog(dialogViewModel);
    }

    private Task OpenFolderAsync(FolderViewModel? viewModel)
    {
        if (viewModel is null)
        {
            return Task.CompletedTask;
        }

        return _localFolderService.OpenFolderAsync(viewModel.Path);
    }

    private Task RemoveFolderAsync(FolderViewModel? viewModel, CancellationToken cancellationToken)
    {
        if (viewModel is null)
        {
            return Task.CompletedTask;
        }

        var confirmationViewModel = GetConfirmationViewModel(viewModel.DataItem.SyncMethod, viewModel.Name);

        var confirmationResult = _dialogService.ShowConfirmationDialog(confirmationViewModel);

        if (confirmationResult != ConfirmationResult.Confirmed)
        {
            return Task.CompletedTask;
        }

        return _syncFolderService.RemoveHostDeviceFolderAsync(viewModel.DataItem, cancellationToken);
    }

    private void EditFolderFilter(FolderViewModel? viewModel)
    {
        if (viewModel == null)
        {
            return;
        }
        
        var editFolderFilterViewModel = _editFilterViewModelFactory.Invoke();
        editFolderFilterViewModel.SetSyncFolder(viewModel.DataItem);
        _dialogService.ShowDialog(editFolderFilterViewModel);
    }

    private RemoveFolderConfirmationViewModelBase GetConfirmationViewModel(SyncMethod syncMethod, string folderName)
    {
        RemoveFolderConfirmationViewModelBase viewModel = syncMethod switch
        {
            SyncMethod.Classic => _removeClassicFolderConfirmationViewModelFactory.Invoke(),
            SyncMethod.OnDemand => _removeOnDemandFolderConfirmationViewModelFactory.Invoke(),
            _ => throw new ArgumentOutOfRangeException(nameof(syncMethod), syncMethod, message: null),
        };

        viewModel.SetFolderName(folderName);

        return viewModel;
    }

    private bool CanToggleStorageOptimization(FolderViewModel? viewModel)
    {
        return
            IsStorageOptimizationFeatureEnabled &&
            viewModel?.Status is MappingSetupStatus.Succeeded;
    }

    private Task ToggleStorageOptimizationAsync(FolderViewModel? viewModel, CancellationToken cancellationToken)
    {
        if (viewModel is null)
        {
            return Task.CompletedTask;
        }

        return _syncFolderService.SetStorageOptimizationAsync(viewModel.DataItem, !viewModel.IsStorageOptimizationEnabled, cancellationToken);
    }

    private void Schedule(Action action)
    {
        _scheduler.Schedule(action);
    }
}
