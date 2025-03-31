﻿using ProtonDrive.App.Onboarding;

namespace ProtonDrive.App.Settings;

public sealed record OnboardingSettings
{
    public bool IsSyncFolderSelectionCompleted { get; init; }
    public bool IsAccountRootFolderSelectionCompleted { get; init; }
    public bool IsUpgradeStorageStepCompleted { get; init; }
    public bool IsOnboardingCompleted { get; init; }
    public bool IsSharedWithMeOnboardingCompleted { get; init; }
    public StorageOptimizationOnboardingStep StorageOptimizationOnboardingStep { get; init; }
}
