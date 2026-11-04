using System.Security.Claims;

namespace PharmacyWarehouse.Api.Data;

// کاربر و دلیل تغییر در این درخواست. بدون کاربر (مثل فرمان خط فرمان) «system» ثبت می‌شود.
public sealed class AuditContext
{
    public const string SystemUserName = "system";

    public Guid? UserId { get; private set; }
    public string UserName { get; private set; } = SystemUserName;
    public string? Reason { get; set; }

    public void SetUser(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true) return;

        if (Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
        {
            UserId = id;
            UserName = principal.Identity.Name ?? SystemUserName;
        }
    }
}

// کاربر را یک جا پر می‌کند تا هیچ endpointی نتواند فراموشش کند.
public sealed class AuditContextMiddleware
{
    private readonly RequestDelegate _next;

    public AuditContextMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public Task InvokeAsync(HttpContext context, AuditContext audit)
    {
        audit.SetUser(context.User);
        return _next(context);
    }
}
