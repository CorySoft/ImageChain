using ImageChain.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ImageChain.Web.Controllers;

[ApiController]
[Route("api/admin")]
public class AdminAuthController : ControllerBase
{
    private readonly IOptionsMonitor<ImageChain.Core.Configuration.ImageChainOptions> _options;

    public AdminAuthController(IOptionsMonitor<ImageChain.Core.Configuration.ImageChainOptions> options)
    {
        _options = options;
    }

    public sealed class LoginRequest
    {
        public string Password { get; set; } = "";
    }

    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        var expected = _options.CurrentValue.AdminPassword;
        if (string.IsNullOrEmpty(expected))
            return BadRequest(new { error = "Admin password not configured" });

        if (!string.Equals(request.Password, expected, StringComparison.Ordinal))
            return Unauthorized(new { error = "Invalid password" });

        var sessionId = AdminSessionStore.CreateSession(AdminSessionStore.DeriveSecret(expected));
        Response.Cookies.Append("ImageChainAdmin", sessionId, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            MaxAge = TimeSpan.FromDays(365)
        });

        return Ok(new { message = "Login successful" });
    }

    [Authorize(AuthenticationSchemes = "AdminPassword")]
    [HttpPost("logout")]
    public IActionResult Logout()
    {
        Response.Cookies.Delete("ImageChainAdmin");
        return Ok(new { message = "Logged out" });
    }

    [Authorize(AuthenticationSchemes = "AdminPassword")]
    [HttpGet("me")]
    public IActionResult Me()
    {
        return Ok(new { authenticated = true, name = "Admin" });
    }
}
