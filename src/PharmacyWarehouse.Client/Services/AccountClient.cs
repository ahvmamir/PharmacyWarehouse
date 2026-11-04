using System.Net.Http.Json;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Client.Services;

public sealed class AccountClient
{
    private readonly HttpClient _http;

    public AccountClient(HttpClient http)
    {
        _http = http;
    }

    public Task<ApiOutcome> ChangePasswordAsync(string currentPassword, string newPassword)
    {
        var request = new ChangePasswordRequest
        {
            CurrentPassword = currentPassword,
            NewPassword = newPassword
        };

        return ApiSender.SendNoContentAsync(ct => _http.PostAsJsonAsync(ApiRoutes.ChangePassword, request, ct));
    }

    // فقط برای کاربرِ دارای رمز اولیه (سرور اگر پرچم فعال نباشد ۴۰۳ می‌دهد).
    public Task<ApiOutcome> SetInitialPasswordAsync(string newPassword)
    {
        var request = new SetInitialPasswordRequest { NewPassword = newPassword };

        return ApiSender.SendNoContentAsync(ct => _http.PostAsJsonAsync(ApiRoutes.SetInitialPassword, request, ct));
    }
}