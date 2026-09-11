using System.Buffers.Text;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ImageChain.Web.Auth;

public sealed class AdminPasswordHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IOptionsMonitor<ImageChain.Core.Configuration.ImageChainOptions> _options;

    public AdminPasswordHandler(
        IOptionsMonitor<ImageChain.Core.Configuration.ImageChainOptions> options,
        IOptionsMonitor<AuthenticationSchemeOptions> authOptions,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(authOptions, logger, encoder)
    {
        _options = options;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Cookies.TryGetValue("ImageChainAdmin", out var sessionId) ||
            string.IsNullOrEmpty(sessionId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var expected = _options.CurrentValue.AdminPassword;
        if (string.IsNullOrEmpty(expected) ||
            !AdminSessionStore.IsValid(sessionId, AdminSessionStore.DeriveSecret(expected)))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid session"));
        }

        var claims = new[] { new Claim(ClaimTypes.Name, "Admin") };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

public static class AdminSessionStore
{
    public static string DeriveSecret(string adminPassword)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(adminPassword))).ToLowerInvariant();

    private static string Sign(string payload, string secret)
        => Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

    public static string CreateSession(string secret)
    {
        var payload = $"{DateTime.UtcNow:o}|{Guid.NewGuid():N}";
        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payload)) + "." + Sign(payload, secret);
    }

    public static bool IsValid(string token, string secret)
    {
        var dot = token.IndexOf('.');
        if (dot <= 0 || dot == token.Length - 1) return false;
        try
        {
            var payload = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(token.AsSpan(0, dot)));
            var expected = Sign(payload, secret).AsSpan();
            var actual = token.AsSpan(dot + 1);
            return expected.SequenceEqual(actual);
        }
        catch
        {
            return false;
        }
    }
}
