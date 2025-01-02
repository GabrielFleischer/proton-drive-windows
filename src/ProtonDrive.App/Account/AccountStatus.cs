﻿namespace ProtonDrive.App.Account;

/// <summary>
/// User account status
/// </summary>
public enum AccountStatus
{
    /// <summary>
    /// Session is not started
    /// </summary>
    None,

    /// <summary>
    /// Setting up user account
    /// </summary>
    /// <remarks>
    /// Account switching is performed as part of account setup.
    /// </remarks>
    SettingUp,

    /// <summary>
    /// Setting up user account has succeeded
    /// </summary>
    Succeeded,

    /// <summary>
    /// Setting up user account has failed.
    /// See <see cref="AccountErrorCode"/> for a list of possible errors.
    /// </summary>
    Failed,
}
