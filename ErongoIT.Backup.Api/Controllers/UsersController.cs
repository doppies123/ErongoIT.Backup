using ErongoIT.Backup.Application.Users;
using ErongoIT.Backup.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace ErongoIT.Backup.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/users")]
public sealed class UsersController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly PasswordHasher<User> _passwordHasher;

    public UsersController(
        IUserService userService,
        PasswordHasher<User> passwordHasher)
    {
        _userService = userService;
        _passwordHasher = passwordHasher;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserResponse>>> GetAll(
        CancellationToken cancellationToken)
    {
        var users = await _userService.GetAllAsync(
            cancellationToken);

        var response = users
            .Select(ToResponse)
            .ToList();

        return Ok(response);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var user = await _userService.GetByIdAsync(
            id,
            cancellationToken);

        if (user is null)
            return NotFound();

        return Ok(ToResponse(user));
    }

    [HttpPost]
    [AllowAnonymous]
    public async Task<ActionResult<UserResponse>> Create(
        [FromBody] CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        var existingUsers = await _userService.GetAllAsync(
            cancellationToken);

        if (existingUsers.Count > 0 &&
            !(User.Identity?.IsAuthenticated ?? false))
        {
            return Unauthorized();
        }

        var temporaryUser = new User(
            request.Username,
            "temporary");

        var passwordHash = _passwordHasher.HashPassword(
            temporaryUser,
            request.Password);

        try
        {
            var user = await _userService.CreateAsync(
                request.Username,
                passwordHash,
                cancellationToken);

            return CreatedAtAction(
                nameof(GetById),
                new { id = user.Id },
                ToResponse(user));
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new
            {
                error = exception.Message
            });
        }
    }

    [HttpPut("{id:guid}/password")]
    public async Task<IActionResult> ChangePassword(
        Guid id,
        [FromBody] ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        var temporaryUser = new User(
            "temporary",
            "temporary");

        var passwordHash = _passwordHasher.HashPassword(
            temporaryUser,
            request.Password);

        var updated = await _userService.ChangePasswordAsync(
            id,
            passwordHash,
            cancellationToken);

        if (!updated)
            return NotFound();

        return NoContent();
    }

    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(
        Guid id,
        CancellationToken cancellationToken)
    {
        var updated = await _userService.ActivateAsync(
            id,
            cancellationToken);

        if (!updated)
            return NotFound();

        return NoContent();
    }

    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(
        Guid id,
        CancellationToken cancellationToken)
    {
        var updated = await _userService.DeactivateAsync(
            id,
            cancellationToken);

        if (!updated)
            return NotFound();

        return NoContent();
    }

    private static UserResponse ToResponse(
        User user)
    {
        return new UserResponse(
            user.Id,
            user.Username,
            user.IsActive,
            user.CreatedAtUtc,
            user.LastLoginAtUtc);
    }
}

public sealed record CreateUserRequest(
    string Username,
    string Password);

public sealed record ChangePasswordRequest(
    string Password);

public sealed record UserResponse(
    Guid Id,
    string Username,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime? LastLoginAtUtc);
