﻿using System.Threading;
using System.Threading.Tasks;

namespace ProtonDrive.App.Sync;

public interface IRemoteIdsFromNodeIdProvider
{
    Task<RemoteIds?> GetRemoteIdsOrDefaultAsync(int mappingId, long remoteNodeId, CancellationToken cancellationToken);
}
