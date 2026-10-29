namespace Commerce.Application.Interfaces;

using System;

public interface ICurrentUserService
{
    Guid? UserId { get; }
    string? Role { get; }
    bool IsAuthenticated { get; }
}
