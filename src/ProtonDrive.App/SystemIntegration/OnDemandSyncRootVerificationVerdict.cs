﻿namespace ProtonDrive.App.SystemIntegration;

public enum OnDemandSyncRootVerificationVerdict
{
    /// <summary>
    /// Provided folder path does not belong to on-demand sync root
    /// </summary>
    NotRegistered,

    /// <summary>
    /// Provided folder path belongs to on-demand sync root with expected characteristics
    /// </summary>
    Valid,

    /// <summary>
    /// Provided folder path belongs to on-demand sync root with same ID but characteristics different from expected ones
    /// </summary>
    Invalid,

    /// <summary>
    /// Provided folder path belongs to on-demand sync root with different ID
    /// </summary>
    ConflictingRootExists,

    /// <summary>
    /// Provided folder path is ancestor of existing on-demand sync root
    /// </summary>
    ConflictingDescendantRootExists,

    /// <summary>
    /// Failed to obtain and verify on-demand sync root for the provided folder path
    /// </summary>
    VerificationFailed,
}
