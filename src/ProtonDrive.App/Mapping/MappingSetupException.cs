﻿using System;
using System.Diagnostics.CodeAnalysis;
using ProtonDrive.App.Settings;
using ProtonDrive.Shared.Extensions;

namespace ProtonDrive.App.Mapping;

public sealed class MappingSetupException : Exception, IFormattedErrorCodeProvider
{
    public MappingSetupException()
    {
    }

    public MappingSetupException(string message)
        : base(message)
    {
    }

    public MappingSetupException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public MappingSetupException(RemoteToLocalMapping mapping, MappingErrorCode errorCode)
    {
        MappingType = mapping.Type;
        MappingStatus = mapping.Status;
        SyncMethod = mapping.SyncMethod;
        ErrorCode = errorCode;
    }

    private MappingType? MappingType { get; }
    private MappingStatus? MappingStatus { get; }
    private SyncMethod? SyncMethod { get; }
    private MappingErrorCode? ErrorCode { get; }

    public bool TryGetRelevantFormattedErrorCode([MaybeNullWhen(false)] out string formattedErrorCode)
    {
        formattedErrorCode = string.Empty;

        if (MappingType is null || ErrorCode is null)
        {
            return false;
        }

        formattedErrorCode = $"{MappingType}:{SyncMethod}:{MappingStatus}/{ErrorCode}";

        return true;
    }
}
