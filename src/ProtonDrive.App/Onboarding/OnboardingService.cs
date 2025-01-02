﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ProtonDrive.App.Account;
using ProtonDrive.App.Services;
using ProtonDrive.App.Settings;
using ProtonDrive.Shared.Configuration;
using ProtonDrive.Shared.Repository;

namespace ProtonDrive.App.Onboarding;

internal sealed class OnboardingService : IOnboardingService, IStartableService, IAccountSwitchingAware
{
    private readonly FeatureFlags _featureFlags;
    private readonly Lazy<IEnumerable<IOnboardingStateAware>> _onboardingStateAware;
    private readonly Lazy<IEnumerable<ISharedWithMeOnboardingStateAware>> _sharedWithMeOnboardingStateAware;
    private readonly IRepository<OnboardingSettings> _settings;
    private readonly ILogger<OnboardingService> _logger;

    private OnboardingState _state = OnboardingState.Initial;

    public OnboardingService(
        FeatureFlags featureFlags,
        Lazy<IEnumerable<IOnboardingStateAware>> onboardingStateAware,
        Lazy<IEnumerable<ISharedWithMeOnboardingStateAware>> sharedWithMeOnboardingStateAware,
        IRepository<OnboardingSettings> settings,
        ILogger<OnboardingService> logger)
    {
        _featureFlags = featureFlags;
        _onboardingStateAware = onboardingStateAware;
        _sharedWithMeOnboardingStateAware = sharedWithMeOnboardingStateAware;
        _settings = settings;
        _logger = logger;
    }

    Task IStartableService.StartAsync(CancellationToken cancellationToken)
    {
        LoadSettingsAndRefreshState();

        return Task.CompletedTask;
    }

    void IAccountSwitchingAware.OnAccountSwitched()
    {
        LoadSettingsAndRefreshState();
    }

    public void CompleteStep(OnboardingStep step)
    {
        var settings = GetSettings();

        switch (step)
        {
            case OnboardingStep.SyncFolderSelection:
                settings = settings with { IsSyncFolderSelectionCompleted = true };
                break;
            case OnboardingStep.AccountRootFolderSelection:
                settings = settings with { IsAccountRootFolderSelectionCompleted = true };
                break;
            case OnboardingStep.UpgradeStorage:
                settings = settings with { IsUpgradeStorageStepCompleted = true };
                break;
            default:
                throw new InvalidEnumArgumentException(nameof(step), (int)step, typeof(OnboardingStep));
        }

        SaveSettings(settings);
        RefreshState(settings);

        // It is intentional to notify onboarding completion separately from the onboarding step change
        settings = TryCompleteOnboarding(settings);
        RefreshState(settings);
    }

    public void CompleteSharedWithMeOnboarding()
    {
        var settings = GetSettings() with
        {
            IsSharedWithMeOnboardingCompleted = true,
        };

        SaveSettings(settings);
        OnSharedWithMeOnboardingStateChanged(OnboardingStatus.Completed);
    }

    internal Task WaitForCompletionAsync()
    {
        return Task.CompletedTask;
    }

    private OnboardingSettings TryCompleteOnboarding(OnboardingSettings settings)
    {
        if (settings.IsOnboardingCompleted || _state.Step is not OnboardingStep.None)
        {
            return settings;
        }

        return CompleteOnboarding(settings);
    }

    private void LoadSettingsAndRefreshState()
    {
        var settings = GetSettings();
        var state = ToOnboardingState(settings);

        if (IsEligibleForCompletion())
        {
            settings = CompleteOnboarding(settings);
            state = ToOnboardingState(settings);
        }

        SetState(state);

        var status = settings.IsSharedWithMeOnboardingCompleted ? OnboardingStatus.Completed : OnboardingStatus.NotStarted;

        OnSharedWithMeOnboardingStateChanged(status);

        return;

        bool IsEligibleForCompletion()
        {
            return state.Status is OnboardingStatus.Onboarding && state.Step is OnboardingStep.None or OnboardingStep.UpgradeStorage;
        }
    }

    private OnboardingSettings CompleteOnboarding(OnboardingSettings settings)
    {
        settings = settings with
        {
            IsOnboardingCompleted = true,
        };

        SaveSettings(settings);

        return settings;
    }

    private void RefreshState(OnboardingSettings settings)
    {
        var state = ToOnboardingState(settings);

        SetState(state);
    }

    private OnboardingState ToOnboardingState(OnboardingSettings settings)
    {
        var status = ToOnboardingStatus(settings);
        var step = ToOnboardingStep(settings);

        return new OnboardingState(status, step);
    }

    private OnboardingStatus ToOnboardingStatus(OnboardingSettings settings)
    {
        return settings.IsOnboardingCompleted ? OnboardingStatus.Completed : OnboardingStatus.Onboarding;
    }

    private OnboardingStep ToOnboardingStep(OnboardingSettings settings)
    {
        if (settings.IsOnboardingCompleted)
        {
            return OnboardingStep.None;
        }

        if (!settings.IsSyncFolderSelectionCompleted)
        {
            return OnboardingStep.SyncFolderSelection;
        }

        if (!settings.IsAccountRootFolderSelectionCompleted)
        {
            return OnboardingStep.AccountRootFolderSelection;
        }

        if (!settings.IsUpgradeStorageStepCompleted && _featureFlags.UpgradeStorageOnboardingStepEnabled)
        {
            return OnboardingStep.UpgradeStorage;
        }

        return OnboardingStep.None;
    }

    private void SetState(OnboardingState state)
    {
        if (_state == state)
        {
            return;
        }

        _state = state;

        OnOnboardingStateChanged(state);
    }

    private void OnOnboardingStateChanged(OnboardingState state)
    {
        _logger.LogInformation("Onboarding state changed to {Status}, step {Step}", state.Status, state.Step);

        foreach (var listener in _onboardingStateAware.Value)
        {
            listener.OnboardingStateChanged(state);
        }
    }

    private void OnSharedWithMeOnboardingStateChanged(OnboardingStatus value)
    {
        foreach (var listener in _sharedWithMeOnboardingStateAware.Value)
        {
            listener.SharedWithMeOnboardingStateChanged(value);
        }
    }

    private OnboardingSettings GetSettings()
    {
        return _settings.Get() ?? new OnboardingSettings();
    }

    private void SaveSettings(OnboardingSettings settings)
    {
        _settings.Set(settings);
    }
}
