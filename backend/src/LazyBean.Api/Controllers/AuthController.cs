using System.Security.Claims;
using LazyBean.Api.Auth;
using LazyBean.Application.Auth.Commands.ChangePassword;
using LazyBean.Application.Auth.Commands.Login;
using LazyBean.Application.Auth.Dtos;
using LazyBean.Application.Auth.Queries.GetAccount;
using LazyBean.Application.Auth.Queries.GetSecurityStamp;
using LazyBean.Domain.Exceptions;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LazyBean.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(ISender sender) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [ProducesResponseType(typeof(AuthenticatedUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthenticatedUserDto>> Login(LoginCommand command, CancellationToken ct)
    {
        var user = await sender.Send(command, ct);
        var stamp = await sender.Send(new GetSecurityStampQuery(user.Id), ct)
            ?? throw new InvalidCredentialsException(user.Username);

        await SignInAsync(user.Id, user.Username, stamp);

        return Ok(user);
    }

    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    [HttpGet("me")]
    [ProducesResponseType(typeof(AuthenticatedUserDto), StatusCodes.Status200OK)]
    public ActionResult<AuthenticatedUserDto> Me()
    {
        var id = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var username = User.FindFirstValue(ClaimTypes.Name)!;
        return Ok(new AuthenticatedUserDto(id, username));
    }

    // The signed-in user's account: username, and when the password was last changed on the website (null while it
    // is still the password set during setup).
    [HttpGet("account")]
    [ProducesResponseType(typeof(AccountDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<AccountDto>> Account(CancellationToken ct) =>
        Ok(await sender.Send(new GetAccountQuery(CurrentUserId()), ct));

    // Requires the current password. On success this browser stays signed in, and every other browser session is
    // signed out. Collector apps keep working unless RevokeDevices is set.
    [HttpPost("change-password")]
    [EnableRateLimiting("password-change")]
    [ProducesResponseType(typeof(ChangePasswordResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ChangePasswordResponse>> ChangePassword(ChangePasswordBody body, CancellationToken ct)
    {
        var userId = CurrentUserId();
        var result = await sender.Send(
            new ChangePasswordCommand(userId, body.CurrentPassword ?? string.Empty, body.NewPassword ?? string.Empty, body.RevokeDevices),
            ct);

        await SignInAsync(userId, User.FindFirstValue(ClaimTypes.Name)!, result.SecurityStamp);

        return Ok(new ChangePasswordResponse(result.PasswordChangedAt, result.DevicesRevoked));
    }

    private Guid CurrentUserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // Issues the dashboard cookie with the user's current security stamp (see DashboardSecurityStamp).
    private async Task SignInAsync(Guid userId, string username, string securityStamp)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, username),
            new Claim(DashboardSecurityStamp.ClaimType, securityStamp),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));
    }
}

public sealed record ChangePasswordBody(string? CurrentPassword, string? NewPassword, bool RevokeDevices);

public sealed record ChangePasswordResponse(DateTimeOffset PasswordChangedAt, int DevicesRevoked);
