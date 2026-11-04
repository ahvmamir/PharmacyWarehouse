namespace PharmacyWarehouse.Client.Auth;

// پل بین ApiRequestHandler (که IHttpClientFactory آن را در دامنه‌ی DI جدا می‌سازد)
// و ApiAuthenticationStateProvider. singleton است تا هر دو یک نمونه را ببینند.
public sealed class SessionSignals
{
    // سرور به درخواستی که نیاز به ورود داشت ۴۰۱ داد: نشست دیگر معتبر نیست.
    public event Action? SessionRejected;

    public void ReportSessionRejected() => SessionRejected?.Invoke();
}