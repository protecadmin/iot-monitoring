using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Controllers;
using Bms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Bms.Api.Common;

public sealed class AccessPolicyFilter(BmsDbContext db) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated == true && context.ActionDescriptor is ControllerActionDescriptor action)
        {
            var role = context.HttpContext.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value ?? "";
            if (!AccessPolicy.Allows(role, action.ControllerName, action.ActionName, context.HttpContext.Request.Method))
            {
                context.Result = new StatusCodeResult(403);
                return;
            }
            // Resource IDs from paths/queries are checked against the same scoped query used by lists and exports.
            var ct = context.HttpContext.RequestAborted;
            foreach (var argument in context.ActionArguments)
            {
                if (argument.Value is not Guid id || id == Guid.Empty) continue;
                bool? allowed = argument.Key switch
                {
                    "siteId" => await db.Sites.AnyAsync(s => s.Id == id, ct),
                    "deviceId" => await db.Devices.AnyAsync(d => d.Id == id, ct),
                    "id" when action.ControllerName == "Sites" => await db.Sites.AnyAsync(s => s.Id == id, ct),
                    "id" when action.ControllerName == "Devices" => await db.Devices.AnyAsync(d => d.Id == id, ct),
                    "id" when action.ControllerName == "Alerts" && action.ActionName is "Acknowledge" or "Resolve" => await db.Alerts.AnyAsync(a => a.Id == id, ct),
                    _ => null
                };
                if (allowed == false) { context.Result = new StatusCodeResult(403); return; }
            }
        }
        await next();
    }
}
