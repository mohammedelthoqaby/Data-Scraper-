using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LazyBean.Api.Controllers;

public sealed record SessionResponse(bool Authenticated, string? Username);

[ApiController]
[Route("api/session")]
[AllowAnonymous]
public class SessionController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(SessionResponse), StatusCodes.Status200OK)]
    public ActionResult<SessionResponse> Get()
    {
        var isAuthenticated = User.Identity?.IsAuthenticated == true;
        var username = isAuthenticated ? User.Identity?.Name : null;
        return Ok(new SessionResponse(isAuthenticated, username));
    }
}
