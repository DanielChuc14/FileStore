using System.Security.Claims;
using FileStore.Application.Abstractions;
using FileStore.Application.Common;
using FileStore.Domain.Enums;

namespace FileStore.API.Infrastructure;

/// <summary>Traduce los claims del request a la abstraccion que consume Application.</summary>
public class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserId =>
        Guid.TryParse(Principal?.FindFirstValue(AuthClaims.UserId), out var id) ? id : null;

    public Guid? ClientId =>
        Guid.TryParse(Principal?.FindFirstValue(AuthClaims.ClientId), out var id) ? id : null;

    public Guid? ApiKeyId =>
        Guid.TryParse(Principal?.FindFirstValue(AuthClaims.ApiKeyId), out var id) ? id : null;

    public UserType? UserType =>
        Enum.TryParse<UserType>(Principal?.FindFirstValue(AuthClaims.Role), out var type)
            ? type
            : null;

    public string? Email => Principal?.FindFirstValue(AuthClaims.Email);

    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent => accessor.HttpContext?.Request.Headers.UserAgent.ToString();
}
