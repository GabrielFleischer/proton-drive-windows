﻿using System.Net;

namespace ProtonDrive.Client;

public enum ResponseCode
{
    Unknown = 0,

    Unauthorized = HttpStatusCode.Unauthorized,
    Forbidden = HttpStatusCode.Forbidden,
    TooManyRequests = HttpStatusCode.TooManyRequests,

    Success = 1000,
    MultipleResponses = 1001,

    /// <summary>
    /// One of (more possible):
    /// <list type="bullet">
    /// <item>Missing field is required.</item>
    /// <item>Cannot create file at the root of a device.</item>
    /// </list>
    /// </summary>
    InvalidRequirements = 2000,

    InvalidValue = 2001,
    NotAllowed = 2011,
    InvalidEncryptedIdFormat = 2061,
    Timeout = 2503,
    AlreadyExists = 2500,
    DoesNotExist = 2501,
    InvalidApp = 5002,
    OutdatedApp = 5003,
    Offline = 7001,
    IncorrectLoginCredentials = 8002,

    /// <summary>
    /// Account is disabled
    /// </summary>
    AccountDeleted = 10002,

    /// <summary>
    /// Account is disabled due to abuse or fraud
    /// </summary>
    AccountDisabled = 10003,

    /// <summary>
    /// The session has expired and failed to refresh.
    /// Either the refresh token has expired or the session was revoked on the backend.
    /// </summary>
    InvalidRefreshToken = 10013,

    /// <summary>
    /// Free account
    /// </summary>
    NoActiveSubscription = 22110,

    AddressInvalid = 33101,
    AddressMissing = 33102,
    AddressDomainExternal = 33103,
    AddressInvalidKeyTransparency = 33104,

    InsufficientQuota = 200001,
    InsufficientSpace = 200002,

    /// <summary>
    /// Max allowed number of folder children is reached. Adding new children is not allowed.
    /// </summary>
    /// <remarks>
    /// Trashed or permanently deleted, but not yet garbage collected, children are included.
    /// </remarks>
    TooManyChildren = 200300,

    /// <summary>
    /// The verification token for a file block upload was rejected as invalid
    /// </summary>
    InvalidVerificationToken = 200501,

    CustomCode = 10000000,

    /// <summary>
    /// Network error that is purely on the client side.
    /// For example, client is offline.
    /// </summary>
    NetworkError = CustomCode + 1,

    /// <summary>
    /// Network errors that might be blamed on our server.
    /// For example, timeout, API not reachable but otherwise client has connection, API responding with non-JSON output.
    /// </summary>
    ServerError = CustomCode + 2,

    SessionRefreshFailed = CustomCode + 3,
    SrpError = CustomCode + 4,
}
