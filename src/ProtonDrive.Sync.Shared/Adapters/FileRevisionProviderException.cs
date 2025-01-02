﻿using System;
using System.Diagnostics.CodeAnalysis;
using ProtonDrive.Shared.Extensions;
using ProtonDrive.Sync.Shared.FileSystem;

namespace ProtonDrive.Sync.Shared.Adapters;

public class FileRevisionProviderException : Exception, IFormattedErrorCodeProvider, IFileSystemErrorCodeProvider
{
    public FileRevisionProviderException()
    {
    }

    public FileRevisionProviderException(string message)
        : base(message)
    {
    }

    public FileRevisionProviderException(string message, FileRevisionProviderErrorCode providerErrorCode)
        : base(message)
    {
        ProviderErrorCode = providerErrorCode;
    }

    public FileRevisionProviderException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    public FileRevisionProviderException(string message, FileSystemErrorCode errorCode, Exception? innerException = default)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    public FileSystemErrorCode ErrorCode { get; }

    public FileRevisionProviderErrorCode? ProviderErrorCode { get; }

    public bool TryGetRelevantFormattedErrorCode([MaybeNullWhen(false)] out string formattedErrorCode)
    {
        formattedErrorCode = ProviderErrorCode is null ? ErrorCode.ToString() : $"{ErrorCode}/{ProviderErrorCode}";

        return true;
    }
}
