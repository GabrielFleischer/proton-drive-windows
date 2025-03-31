﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using ProtonDrive.Shared;
using ProtonDrive.Shared.IO;
using ProtonDrive.Sync.Windows.Interop;
using Kernel32 = ProtonDrive.Sync.Windows.Interop.Kernel32;

namespace ProtonDrive.Sync.Windows.FileSystem;

public abstract class FileSystemObject : IDisposable
{
    private static readonly char[] InvalidCharacters = Path.GetInvalidFileNameChars();
    private static readonly HashSet<string> DosDeviceNames = PathExtensions.DosDeviceNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
    private static readonly int MinDosDeviceNameLength = DosDeviceNames.Select(x => x.Length).Min();
    private static readonly int MaxDosDeviceNameLength = DosDeviceNames.Select(x => x.Length).Max();

    private SafeFileHandle? _fileHandle;
    private string? _name;

    private bool _fileInformationInitialized;
    private Kernel32.BY_HANDLE_FILE_INFORMATION _fileInformation;

    protected FileSystemObject(SafeFileHandle handle, string fullPath, FileSystemFileAccess access, bool isAsync)
    {
        _fileHandle = handle;
        FullPath = fullPath;
        Access = access;
        IsAsync = isAsync;
    }

    public string FullPath { get; private set; }

    public SafeFileHandle FileHandle => _fileHandle ?? throw new InvalidOperationException("Handle ownership has been transferred to another object");

    public string Name => _name ??= GetFileName();

    public FileAttributes Attributes
    {
        get => FileInformation.dwFileAttributes;
        set => SetAttributes(value);
    }

    public DateTime CreationTimeUtc
    {
        get => FileInformation.ftCreationTime.ToDateTimeUtc();
        set => SetCreationTime(LONG_FILETIME.FromDateTimeUtc(value));
    }

    public DateTime LastWriteTimeUtc
    {
        get => FileInformation.ftLastWriteTime.ToDateTimeUtc();
        set => SetLastWriteTime(LONG_FILETIME.FromDateTimeUtc(value));
    }

    public long Size => (Attributes & FileAttributes.Directory) == 0
        ? unchecked((long)(((ulong)FileInformation.nFileSizeHigh << 32) + FileInformation.nFileSizeLow))
        : 0;

    public long ObjectId => unchecked((long)(((ulong)FileInformation.nFileIndexHigh << 32) + FileInformation.nFileIndexLow));

    public uint VolumeSerialNumber => FileInformation.dwVolumeSerialNumber;

    public long NumberOfLinks => FileInformation.nNumberOfLinks;

    protected bool IsAsync { get; init; }
    protected FileSystemFileAccess Access { get; init; }

    private ref Kernel32.BY_HANDLE_FILE_INFORMATION FileInformation
    {
        get
        {
            if (!_fileInformationInitialized)
            {
                _fileInformation = GetFileInformation();
                _fileInformationInitialized = true;
            }

            return ref _fileInformation;
        }
    }

    public static void ExposePlaceholders()
    {
        NtDll.RtlSetProcessPlaceholderCompatibilityMode(NtDll.PHCM.PHCM_EXPOSE_PLACEHOLDERS);
    }

    public static FileSystemObject Open(string path, FileMode mode, FileSystemFileAccess access, FileShare share, FileOptions options)
    {
        Ensure.NotNullOrEmpty(path, nameof(path));

        Validate(mode, access, share, attributes: default, options);

        if (!Path.IsPathFullyQualified(path))
        {
            // Path.GetFullPath checks if the path is valid, converts short path to full path if file path exists
            // on the file system, and strips trailing ' ' and '.' characters.
            path = Path.GetFullPath(path);
        }

        // FILE_FLAG_BACKUP_SEMANTICS is required for opening directory handles
        options |= (FileOptions)Kernel32.FileFlags.FILE_FLAG_BACKUP_SEMANTICS;

        var handle = Internal.FileSystem.CreateHandle(path, FileMode.Open, access, share, attributes: default, options);
        try
        {
            var isAsync = (options & FileOptions.Asynchronous) != 0;

            var information = Internal.FileSystem.GetFileInformation(handle);
            var isDirectory = (information.dwFileAttributes & FileAttributes.Directory) > 0;
            return isDirectory ? new FileSystemDirectory(handle, path, access, isAsync) : new FileSystemFile(handle, path, access, isAsync);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Validates new file name against Windows file naming limitations.
    /// </summary>
    /// <remarks>
    /// See https://docs.microsoft.com/en-us/windows/win32/fileio/naming-a-file for more details
    /// on Windows file naming conventions.
    /// </remarks>
    /// <param name="name">The file or folder name to validate</param>
    /// <returns><see cref="FileSystemNameValidationResult.Valid"/> if the name is valid, otherwise the corresponding issue.</returns>
    public static FileSystemNameValidationResult ValidateName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return FileSystemNameValidationResult.Empty;
        }

        if (name.Length > 255)
        {
            return FileSystemNameValidationResult.TooLong;
        }

        if (name.IndexOfAny(InvalidCharacters) >= 0)
        {
            return FileSystemNameValidationResult.ContainsInvalidCharacter;
        }

        if (name.EndsWith(' '))
        {
            return FileSystemNameValidationResult.EndsWithSpace;
        }

        if (name.EndsWith('.'))
        {
            return FileSystemNameValidationResult.EndsWithPeriod;
        }

        if (name.Length >= MinDosDeviceNameLength && name.Length <= MaxDosDeviceNameLength && DosDeviceNames.Contains(name))
        {
            return FileSystemNameValidationResult.Reserved;
        }

        if (name.Length >= MinDosDeviceNameLength && name.IndexOf('.') is var dotIndex && dotIndex >= MinDosDeviceNameLength && DosDeviceNames.Contains(name[..dotIndex]))
        {
            return FileSystemNameValidationResult.Reserved;
        }

        return FileSystemNameValidationResult.Valid;
    }

    public void Rename(string newName, bool replaceIfExists = false)
    {
        var result = ValidateName(newName);

        if (result is not FileSystemNameValidationResult.Valid)
        {
            throw new ArgumentException(result.ToString(), nameof(newName));
        }

        Internal.FileSystem.Rename(FileHandle, newName, replaceIfExists);

        _name = newName;
        FullPath = Path.Combine(Path.GetDirectoryName(FullPath) ?? string.Empty, newName);
    }

    public void Move(FileSystemDirectory newParent, string newName, bool replaceIfExists = false)
    {
        var result = ValidateName(newName);

        if (result is not FileSystemNameValidationResult.Valid)
        {
            throw new ArgumentException(result.ToString(), nameof(newName));
        }

        Internal.FileSystem.Move(FileHandle, newParent.FileHandle, newName, replaceIfExists);

        _name = newName;
        FullPath = Path.Combine(newParent.FullPath, newName);
    }

    public void Refresh()
    {
        _fileInformationInitialized = false;

        _fileInformation = GetFileInformation();
        _fileInformationInitialized = true;
    }

    public void Delete()
    {
        Internal.FileSystem.Delete(FileHandle, true);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        var fileHandle = Interlocked.Exchange(ref _fileHandle, null);
        if (fileHandle is { IsClosed: false })
        {
            fileHandle.Dispose();
        }
    }

    protected static void Validate(
        FileMode mode,
        FileSystemFileAccess access,
        FileShare share,
        FileAttributes attributes,
        FileOptions options)
    {
        if (mode < FileMode.CreateNew || mode > FileMode.Append)
        {
            throw new ArgumentOutOfRangeException(nameof(mode), "FileMode value is out of allowed range");
        }

        var tempShare = share & ~FileShare.Inheritable;
        if (tempShare < FileShare.None || tempShare > (FileShare.ReadWrite | FileShare.Delete))
        {
            throw new ArgumentOutOfRangeException(nameof(share), "FileShare value is out of allowed range");
        }

        if ((attributes & ~(
                FileAttributes.Normal |
                FileAttributes.Hidden |
                FileAttributes.Directory |
                FileAttributes.ReadOnly |
                FileAttributes.System |
                FileAttributes.Temporary)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attributes), "FileAttributes value is out of allowed range");
        }

        if ((options & ~(
                FileOptions.WriteThrough |
                FileOptions.Asynchronous |
                FileOptions.DeleteOnClose |
                FileOptions.SequentialScan |
                FileOptions.Encrypted |
                (FileOptions)Kernel32.FileFlags.FILE_FLAG_NO_BUFFERING)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "FileOptions value is out of allowed range");
        }

        //if ((access & FileAccess.Write) == 0)
        //{
        //    if (mode == FileMode.Truncate || mode == FileMode.CreateNew || mode == FileMode.Create || mode == FileMode.Append)
        //    {
        //        throw new ArgumentException($"Combining FileMode: {mode} with FileAccess: {access} is invalid", nameof(access));
        //    }
        //}

        //if ((access & FileAccess.Read) != 0 && mode == FileMode.Append)
        //{
        //    throw new ArgumentException("Append mode can be requested only with write-only access", nameof(access));
        //}
    }

    protected T WithTransferredHandleOwnership<T>(Func<SafeFileHandle, T> function, bool ownsHandle)
    {
        var handle = ownsHandle ? Interlocked.Exchange(ref _fileHandle, null) : _fileHandle;
        if (handle == null)
        {
            throw new InvalidOperationException("Handle ownership has been transferred to another object");
        }

        try
        {
            return function(handle);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private string GetFileName()
    {
        return Path.GetFileName(FullPath);
    }

    private Kernel32.BY_HANDLE_FILE_INFORMATION GetFileInformation()
    {
        return Internal.FileSystem.GetFileInformation(FileHandle);
    }

    private void SetAttributes(FileAttributes value)
    {
        if (value == default)
        {
            return;
        }

        Kernel32.FILE_BASIC_INFO data = default;
        data.FileAttributes = (uint)value;

        Internal.FileSystem.SetFileInformation(FileHandle, data);

        _fileInformationInitialized = false;
    }

    private void SetCreationTime(LONG_FILETIME value)
    {
        if (value.IsDefault)
        {
            return;
        }

        Kernel32.FILE_BASIC_INFO data = default;
        data.CreationTime = value;

        Internal.FileSystem.SetFileInformation(FileHandle, data);

        if (_fileInformationInitialized)
        {
            _fileInformation.ftCreationTime = value.ToFileTime();
        }
    }

    private void SetLastWriteTime(LONG_FILETIME value)
    {
        if (value.IsDefault)
        {
            return;
        }

        Kernel32.FILE_BASIC_INFO data = default;
        data.LastWriteTime = value;

        Internal.FileSystem.SetFileInformation(FileHandle, data);

        if (_fileInformationInitialized)
        {
            _fileInformation.ftLastWriteTime = value.ToFileTime();
        }
    }
}
