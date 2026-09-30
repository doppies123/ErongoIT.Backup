using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ErongoIT.Backup.Application.Devices;
using ErongoIT.Backup.Application.Users;
using ErongoIT.Backup.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace ErongoIT.Backup.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly PasswordHasher<User> _passwordHasher;
    private readonly IConfiguration _configuration;
    private readonly IDeviceService _deviceService;

    public AuthController(
        IUserService userService,
        PasswordHasher<User> passwordHasher,
        IConfiguration configuration,
        IDeviceService deviceService)
    {
        _userService = userService;
        _passwordHasher = passwordHasher;
        _configuration = configuration;
        _deviceService = deviceService;
    }

    /// <summary>
    /// Login for an enrolled PC (Agent service / Agent GUI) using its
    /// device ID and secret device key.
    /// </summary>
    [HttpPost("device")]
    public async Task<ActionResult<LoginResponse>> DeviceLogin(
        [FromBody] DeviceLoginRequest request,
        CancellationToken cancellationToken)
    {
        var device = await _deviceService.ValidateApiKeyAsync(
            request.DeviceId,
            request.DeviceKey,
            cancellationToken);

        if (device is null)
            return Unauthorized();

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, device.Id.ToString()),
            new Claim(ClaimTypes.NameIdentifier, device.Id.ToString()),
            new Claim(ClaimTypes.Name, $"device:{device.Name}"),
            new Claim(ClaimTypes.Role, "device"),
            new Claim("device_id", device.Id.ToString()),
            new Claim("customer_id", device.CustomerId.ToString())
        };

        return Ok(new LoginResponse(
            WriteToken(claims),
            "Bearer"));
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var user = await _userService.GetByUsernameAsync(
            request.Username,
            cancellationToken);

        if (user is null || !user.IsActive)
        {
            return Unauthorized();
        }

        var verificationResult =
            _passwordHasher.VerifyHashedPassword(
                user,
                user.PasswordHash,
                request.Password);

        if (verificationResult ==
            PasswordVerificationResult.Failed)
        {
            return Unauthorized();
        }

        await _userService.RecordLoginAsync(
            user.Id,
            cancellationToken);

        var token = CreateToken(user);

        return Ok(new LoginResponse(
            token,
            "Bearer"));
    }

    private string CreateToken(
        User user)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.Username),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, "admin")
        };

        return WriteToken(claims);
    }

    private string WriteToken(
        IEnumerable<Claim> claims)
    {
        var key =
            _configuration["Jwt:Key"]
            ?? throw new InvalidOperationException(
                "JWT signing key was not configured.");

        var issuer =
            _configuration["Jwt:Issuer"]
            ?? throw new InvalidOperationException(
                "JWT issuer was not configured.");

        var audience =
            _configuration["Jwt:Audience"]
            ?? throw new InvalidOperationException(
                "JWT audience was not configured.");

        var credentials =
            new SigningCredentials(
                new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(key)),
                SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler()
            .WriteToken(token);
    }
}

public sealed record LoginRequest(
    string Username,
    string Password);

public sealed record LoginResponse(
    string AccessToken,
    string TokenType);

public sealed record DeviceLoginRequest(
    Guid DeviceId,
    string DeviceKey);
