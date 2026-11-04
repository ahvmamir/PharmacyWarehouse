namespace PharmacyWarehouse.Client.Services;

// دانلود پشتیبان کامل دیتابیس. ورود و خروج Excel در برش ۴ می‌آید.
public sealed class BackupClient
{
    private readonly HttpClient _http;

    public BackupClient(HttpClient http)
    {
        _http = http;
    }

    public Task<ApiResult<DownloadedFile>> BackupAsync() =>
        ApiSender.DownloadAsync(ct => _http.GetAsync(PharmacyWarehouse.Domain.Contracts.ApiRoutes.DataBackup, ct));
}
