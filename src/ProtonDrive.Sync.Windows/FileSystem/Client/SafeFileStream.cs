﻿using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using ProtonDrive.Shared.IO;

namespace ProtonDrive.Sync.Windows.FileSystem.Client;

internal class SafeFileStream : MappingExceptionsStream
{
    private readonly long _id;

    public SafeFileStream(Stream origin, long id)
        : base(origin)
    {
        _id = id;
    }

    protected override bool TryMapException(Exception exception, [MaybeNullWhen(false)] out Exception mappedException)
    {
        return ExceptionMapping.TryMapException(exception, _id, out mappedException);
    }
}
