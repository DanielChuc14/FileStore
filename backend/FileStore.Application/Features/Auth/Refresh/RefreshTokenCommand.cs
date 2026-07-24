using FileStore.Application.Features.Auth.Common;
using MediatR;

namespace FileStore.Application.Features.Auth.Refresh;

public record RefreshTokenCommand(string RefreshToken, string? IpAddress)
    : IRequest<AuthenticationResult>;
