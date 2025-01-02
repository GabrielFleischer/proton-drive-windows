﻿namespace ProtonDrive.Client.Contracts;

public sealed record UserResponse : ApiResponse
{
    private readonly User? _user;

    public required User User
    {
        get => _user ?? throw new ApiException("User is not set");
        init => _user = value;
    }
}
