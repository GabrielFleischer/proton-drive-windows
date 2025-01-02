﻿using System;

namespace ProtonDrive.App.Notifications.Offers;

public sealed record Offer
{
    public required string Id { get; init; }
    public required DateTime StartTimeUtc { get; init; }
    public required DateTime EndTimeUtc { get; init; }
    public required string ClickUrl { get; init; }
    public required string ImageFilePath { get; init; }
    public required string Title { get; init; }
    public NotificationMessage? NotificationMessage { get; init; }
}
