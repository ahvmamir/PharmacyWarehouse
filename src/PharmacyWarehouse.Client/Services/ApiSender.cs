using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace PharmacyWarehouse.Client.Services;

// منطق مشترک فراخوانی Api برای همه‌ی کلاینت‌های سرویس. استثنا بیرون نمی‌دهد؛ نتیجه را برمی‌گرداند.
// خطای مدیریت‌شده با Console.WriteLine ثبت می‌شود، نه Console.Error: نوشتن در stderr نوار خطای Blazor را با لینک
// «بارگذاری مجدد» نشان می‌دهد و Reload فرم ارسال‌نشده‌ی کاربر را گم می‌کند.
internal static class ApiSender
{
    // موقت تا الگوی نهایی تایم‌اوت ساخته شود.
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    // کد وضعیت خطا را به ApiOutcome تبدیل می‌کند؛ برای پاسخ موفق null برمی‌گرداند.
    public static ApiOutcome? MapError(HttpResponseMessage response)
    {
        switch (response.StatusCode)
        {
            case HttpStatusCode.Unauthorized: return ApiOutcome.Unauthorized;
            case HttpStatusCode.Forbidden: return ApiOutcome.Forbidden;
            case HttpStatusCode.BadRequest: return ApiOutcome.BadRequest;
            case HttpStatusCode.NotFound: return ApiOutcome.NotFound;
            case HttpStatusCode.UnprocessableEntity: return ApiOutcome.Rejected;
            case HttpStatusCode.TooManyRequests: return ApiOutcome.TooManyRequests;
            case HttpStatusCode.Conflict: return ApiOutcome.Conflict;
        }

        return response.IsSuccessStatusCode ? null : ApiOutcome.ServerError;
    }

    // بدنه‌ی خطا اختیاری است (مثلاً ۴۰۳ مجوز بدنه ندارد)؛ نبودش خطا نیست.
    private static async Task<ApiProblem?> ReadProblemAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ApiProblem>(ct);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    // گزارش بزرگ، ورود و بکاپ بیشتر از یک درخواست عادی طول می‌کشند.
    public static readonly TimeSpan FileTimeout = TimeSpan.FromMinutes(5);

    public static async Task<ApiResult<DownloadedFile>> DownloadAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> send)
    {
        try
        {
            using var cts = new CancellationTokenSource(FileTimeout);
            using var response = await send(cts.Token);

            var error = MapError(response);
            if (error is not null)
            {
                return ApiResult<DownloadedFile>.Failure(error.Value, await ReadProblemAsync(response, cts.Token));
            }

            var headers = response.Content.Headers;
            return ApiResult<DownloadedFile>.Success(new DownloadedFile(
                headers.ContentDisposition?.FileNameStar ?? headers.ContentDisposition?.FileName?.Trim('"') ?? "download",
                headers.ContentType?.MediaType ?? "application/octet-stream",
                await response.Content.ReadAsByteArrayAsync(cts.Token)));
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            Console.WriteLine(ex);
            return ApiResult<DownloadedFile>.Failure(ApiOutcome.NetworkError);
        }
    }

    // برای پاسخ‌های دارای بدنه.
    public static async Task<ApiResult<T>> SendAsync<T>(
        Func<CancellationToken, Task<HttpResponseMessage>> send, TimeSpan? timeout = null) where T : class
    {
        try
        {
            using var cts = new CancellationTokenSource(timeout ?? RequestTimeout);
            using var response = await send(cts.Token);

            var error = MapError(response);
            if (error is not null)
            {
                return ApiResult<T>.Failure(error.Value, await ReadProblemAsync(response, cts.Token));
            }

            var value = await response.Content.ReadFromJsonAsync<T>(cts.Token);
            return value is null
                ? ApiResult<T>.Failure(ApiOutcome.ServerError)
                : ApiResult<T>.Success(value);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            Console.WriteLine(ex);
            return ApiResult<T>.Failure(ApiOutcome.NetworkError);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            Console.WriteLine(ex);
            return ApiResult<T>.Failure(ApiOutcome.ServerError);
        }
    }

    // برای endpointهایی که بدنه ندارند (۲۰۴).
    public static async Task<ApiOutcome> SendNoContentAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> send)
    {
        try
        {
            using var cts = new CancellationTokenSource(RequestTimeout);
            using var response = await send(cts.Token);

            return MapError(response) ?? ApiOutcome.Success;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            Console.WriteLine(ex);
            return ApiOutcome.NetworkError;
        }
    }
}