using FileStore.Application.Features.Auth.Common;
using MediatR;

namespace FileStore.Application.Features.Auth.Login;

public record LoginCommand(string Email, string Password, string? IpAddress)
    : IRequest<AuthenticationResult>;
