﻿using CommunityToolkit.Mvvm.ComponentModel;
using ProtonDrive.App.Windows.Toolkit.Behaviors;
using ProtonDrive.App.Windows.Views.Shared.Navigation;

namespace ProtonDrive.App.Windows.Views.Main;

internal sealed class MainWindowViewModel : ObservableObject, IVisibilityListener
{
    private ObservableObject _content;

    public MainWindowViewModel(INavigationService<DetailsPageViewModel> detailsPages, MainViewModel mainContent)
    {
        DetailsPages = detailsPages;

        _content = mainContent;
    }

    public INavigationService<DetailsPageViewModel> DetailsPages { get; }

    public ObservableObject Content
    {
        get => _content;
        private set => SetProperty(ref _content, value);
    }

    public void OnVisibilityChanged(bool isVisible)
    {
        if (!isVisible)
        {
            return;
        }

        if (_content is MainViewModel mainContent)
        {
            mainContent.CurrentMenuItem = ApplicationPage.Activity;
        }
    }
}
