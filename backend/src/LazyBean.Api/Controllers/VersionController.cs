using LazyBean.Api.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LazyBean.Api.Controllers;

[ApiController]
[Route("api/version")]
[AllowAnonymous]
public class VersionController(IConfiguration config) : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        var commitHash = BuildInfoHelper.GetCommitHash(config);
        var version = typeof(VersionController).Assembly.GetName().Version?.ToString() ?? "1.0.0";
        return Ok(new { commitHash, version });
    }
}
