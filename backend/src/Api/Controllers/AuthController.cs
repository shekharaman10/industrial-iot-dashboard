using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace IotDashboard.Api.Controllers;

/// <summary>
/// Issues short-lived JWT tokens in exchange for a valid API key.
///
/// Flow:
///   POST /api/auth/token  { "apiKey": "..." }
///   → 200 { "token": "eyJ...", "expiresIn": 3600 }
///
/// The API key is configured via Jwt:ApiKey (env var Jwt__ApiKey).
/// The token is then passed to SignalR as ?access_token=... in the hub URL.
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IConfiguration config, ILogger<AuthController> logger)
    {
        _config = config;
        _logger = logger;
    }

    [HttpPost("token")]
    public IActionResult GetToken([FromBody] TokenRequest req)
    {
        var expectedKey = _config["Jwt:ApiKey"];
        if (string.IsNullOrEmpty(expectedKey))
        {
            _logger.LogWarning("[Auth] Jwt:ApiKey not configured — token endpoint disabled");
            return StatusCode(503, new { error = "Authentication not configured." });
        }

        if (!string.Equals(req.ApiKey, expectedKey, StringComparison.Ordinal))
        {
            _logger.LogWarning("[Auth] Invalid API key attempt from {IP}",
                HttpContext.Connection.RemoteIpAddress);
            return Unauthorized(new { error = "Invalid API key." });
        }

        var secret    = _config["Jwt:Secret"]!;
        var issuer    = _config["Jwt:Issuer"]   ?? "iot-dashboard";
        var audience  = _config["Jwt:Audience"] ?? "iot-dashboard-clients";
        var expiresIn = 3600; // 1 hour

        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer:   issuer,
            audience: audience,
            claims:
            [
                new Claim(ClaimTypes.Role, "dashboard"),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            ],
            expires:  DateTime.UtcNow.AddSeconds(expiresIn),
            signingCredentials: creds);

        _logger.LogInformation("[Auth] Token issued to {IP}",
            HttpContext.Connection.RemoteIpAddress);

        return Ok(new
        {
            token     = new JwtSecurityTokenHandler().WriteToken(token),
            expiresIn,
        });
    }

    public sealed record TokenRequest(string ApiKey);
}
