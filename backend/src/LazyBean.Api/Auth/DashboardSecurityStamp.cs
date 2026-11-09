using System.Security.Claims;
using LazyBean.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LazyBean.Api.Auth;

// A dashboard cookie stays valid only while its security stamp matches the user's current stamp. A password change
// replaces the stamp, so every other browser session is signed out on its next request.
public static class DashboardSecurityStamp
{
    public const string ClaimType = "lazybean.security_stamp";

    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        var userId = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var issuedStamp = principal?.FindFirstValue(ClaimType);

        if (!Guid.TryParse(userId, out var id) || string.IsNullOrEmpty(issuedStamp))
        {
            await RejectAsync(context);
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<LazyBeanDbContext>();
        var currentStamp = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => u.SecurityStamp)
            .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

        if (!string.Equals(currentStamp, issuedStamp, StringComparison.Ordinal))
        {
            await RejectAsync(context);
        }
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
