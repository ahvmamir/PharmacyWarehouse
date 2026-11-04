using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using PharmacyWarehouse.Client.Services;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Client.Auth;

public sealed class ApiAuthenticationStateProvider : AuthenticationStateProvider, IDisposable
{
    private readonly HttpClient _http;
    private readonly SessionSignals _signals;

    // وضعیت قطعی ورود (وارد یا ناشناس). null یعنی هنوز معلوم نیست.
    // خطای گذرای /me هرگز اینجا ذخیره نمی‌شود، تا «ناشناس» کش نشود.
    private AuthenticationState? _state;
    private Task<AuthenticationState>? _loading;

    // با هر ورود، خروج یا ۴۰۱ زیاد می‌شود؛ جواب کهنه‌ی /me که وسط آن‌ها برسد دور ریخته می‌شود.
    private int _version;

    public ApiAuthenticationStateProvider(HttpClient http, SessionSignals signals)
    {
        _http = http;
        _signals = signals;
        _signals.SessionRejected += OnSessionRejected;
    }

    public CurrentUserResponse? CurrentUser { get; private set; }

    // فقط برای نمایش دکمه‌ها؛ سرور مرجع است.
    public bool Can(string permission) => CurrentUser?.Permissions.Contains(permission) == true;

    // true یعنی آخرین پرسش از /me به خطای گذرا (شبکه یا ۵۰۰) خورد و وضعیت ورود نامعلوم است.
    public bool IdentityLoadFailed { get; private set; }

    // هر وقت CurrentUser یا IdentityLoadFailed عوض شد (برای اینکه MainLayout دوباره رسم شود).
    public event Action? StatusChanged;

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (_state is not null) return _state;

        var load = _loading ??= LoadAsync();
        try
        {
            return await load;
        }
        finally
        {
            if (ReferenceEquals(_loading, load)) _loading = null;
        }
    }

    // تلاش دوباره بعد از خطای گذرا.
    public Task RefreshAsync()
    {
        _state = null;
        var load = LoadAsync();
        NotifyAuthenticationStateChanged(load);
        return load;
    }

    public async Task<LoginOutcome> LoginAsync(string username, string password)
    {
        var request = new LoginRequest { Username = username, Password = password };

        var result = await ApiSender.SendAsync<CurrentUserResponse>(
            ct => _http.PostAsJsonAsync(ApiRoutes.Login, request, ct));

        switch (result.Outcome)
        {
            case ApiOutcome.Success:
                SetUser(result.Value!);
                return LoginOutcome.Success;

            case ApiOutcome.Unauthorized:
                return LoginOutcome.InvalidCredentials;

            case ApiOutcome.TooManyRequests:
                return LoginOutcome.LockedOut;

            case ApiOutcome.NetworkError:
                return LoginOutcome.NetworkError;

            default:
                return LoginOutcome.UnexpectedError;
        }
    }

    public async Task<bool> LogoutAsync()
    {
        var outcome = await ApiSender.SendNoContentAsync(
            ct => _http.PostAsync(ApiRoutes.Logout, content: null, ct));

        if (outcome != ApiOutcome.Success) return false;

        SetUser(null);
        return true;
    }

    public void Dispose()
    {
        _signals.SessionRejected -= OnSessionRejected;
    }

    private async Task<AuthenticationState> LoadAsync()
    {
        var startVersion = _version;

        var result = await ApiSender.SendAsync<CurrentUserResponse>(
            ct => _http.GetAsync(ApiRoutes.Me, ct));

        // اگر وسط راه ورود، خروج یا ۴۰۱ شد، جواب /me کهنه است.
        if (startVersion != _version) return _state ?? Anonymous();

        AuthenticationState state;

        if (result.IsSuccess)
        {
            state = Commit(result.Value);
        }
        else if (result.Outcome == ApiOutcome.Unauthorized)
        {
            state = Commit(null);
        }
        else
        {
            // خطای گذرا: ناشناس نشان می‌دهیم ولی ذخیره نمی‌کنیم.
            CurrentUser = null;
            IdentityLoadFailed = true;
            state = Anonymous();
        }

        StatusChanged?.Invoke();
        return state;
    }

    private void OnSessionRejected()
    {
        // اگر وضعیت هنوز معلوم نیست یا همین حالا هم ناشناس هستیم، کاری لازم نیست.
        if (_state is null || CurrentUser is null) return;

        SetUser(null);
    }

    private void SetUser(CurrentUserResponse? user)
    {
        _version++;
        var state = Commit(user);
        NotifyAuthenticationStateChanged(Task.FromResult(state));
        StatusChanged?.Invoke();
    }

    private AuthenticationState Commit(CurrentUserResponse? user)
    {
        CurrentUser = user;
        IdentityLoadFailed = false;
        _state = ToState(user);
        return _state;
    }

    private static AuthenticationState Anonymous()
        => new(new ClaimsPrincipal(new ClaimsIdentity()));

    private static AuthenticationState ToState(CurrentUserResponse? user)
    {
        if (user is null) return Anonymous();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName)
        };
        claims.AddRange(user.Roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var identity = new ClaimsIdentity(claims, authenticationType: "ServerCookie");
        return new AuthenticationState(new ClaimsPrincipal(identity));
    }
}