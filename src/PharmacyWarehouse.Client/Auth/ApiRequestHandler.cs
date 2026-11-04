using System.Net;
using Microsoft.AspNetCore.Components.WebAssembly.Http;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Client.Auth;

public sealed class ApiRequestHandler : DelegatingHandler
{
    private readonly SessionSignals _signals;

    public ApiRequestHandler(SessionSignals signals)
    {
        _signals = signals;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // به مرورگر می‌گوید کوکی را حتی در درخواست بین‌origin (7400 به 7300) بفرستد.
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);

        var isSafe = request.Method == HttpMethod.Get
                     || request.Method == HttpMethod.Head
                     || request.Method == HttpMethod.Options
                     || request.Method == HttpMethod.Trace;

        if (!isSafe && !request.Headers.Contains(ApiHeaders.RequestedWith))
        {
            request.Headers.TryAddWithoutValidation(ApiHeaders.RequestedWith, ApiHeaders.RequestedWithValue);
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized && !IsCredentialCheck(request))
        {
            _signals.ReportSessionRejected();
        }

        return response;
    }

    // ورود و /me خودشان ۴۰۱ را معنا می‌کنند (رمز غلط، ناشناس)؛ ۴۰۱ آن‌ها «نشست رد شد» نیست.
    private static bool IsCredentialCheck(HttpRequestMessage request)
    {
        var path = request.RequestUri?.AbsolutePath;

        return string.Equals(path, ApiRoutes.Login, StringComparison.OrdinalIgnoreCase)
               || string.Equals(path, ApiRoutes.Me, StringComparison.OrdinalIgnoreCase);
    }
}