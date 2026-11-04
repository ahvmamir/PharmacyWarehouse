namespace PharmacyWarehouse.Api.Auth;

// همه‌ی پاسخ‌های Api، حتی ۴۰۱ و ۴۰۰، no-store هستند تا محل قدیمی دستگاه از کش نیاید.
// فقط زیر /api: در تولید فایل‌های Client هم از همین سرور سرو می‌شوند و باید کش شوند.
public sealed class NoStoreMiddleware
{
    private readonly RequestDelegate _next;

    public NoStoreMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.Headers.CacheControl = "no-store";
        }

        return _next(context);
    }
}
