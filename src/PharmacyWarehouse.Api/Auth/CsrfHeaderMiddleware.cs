using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Api.Auth;

public sealed class CsrfHeaderMiddleware
{
    private readonly RequestDelegate _next;

    public CsrfHeaderMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var method = context.Request.Method;

        var isSafe = HttpMethods.IsGet(method)
                     || HttpMethods.IsHead(method)
                     || HttpMethods.IsOptions(method)
                     || HttpMethods.IsTrace(method);

        if (!isSafe && context.Request.Headers[ApiHeaders.RequestedWith] != ApiHeaders.RequestedWithValue)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("Missing or invalid " + ApiHeaders.RequestedWith + " header.");
            return;
        }

        await _next(context);
    }
}