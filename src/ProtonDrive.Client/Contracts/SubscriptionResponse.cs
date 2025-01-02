﻿namespace ProtonDrive.Client.Contracts;

public sealed record SubscriptionResponse : ApiResponse
{
    private readonly UserSubscription? _subscription;

    public UserSubscription Subscription
    {
        get => _subscription ?? throw new ApiException("Subscription is not set");
        init => _subscription = value;
    }
}
