﻿using System.Threading;
using System.Threading.Tasks;

namespace ProtonDrive.Client.Authentication.Sessions;

internal sealed class SessionClient : ISessionClient
{
    private readonly IAuthenticationSessionApiClient _apiClient;

    public SessionClient(IAuthenticationSessionApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public async Task<string> ForkSessionAsync(SessionForkingParameters parameters, CancellationToken cancellationToken)
    {
        var result = await _apiClient.ForkSessionAsync(parameters, cancellationToken).ThrowOnFailure().ConfigureAwait(false);

        return result.Selector;
    }
}
