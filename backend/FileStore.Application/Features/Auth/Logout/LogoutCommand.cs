using MediatR;

namespace FileStore.Application.Features.Auth.Logout;

public record LogoutCommand(string? RefreshToken) : IRequest;
