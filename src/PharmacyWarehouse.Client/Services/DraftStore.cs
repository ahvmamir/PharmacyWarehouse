using System.Text.Json;
using Microsoft.JSInterop;
using PharmacyWarehouse.Client.Auth;

namespace PharmacyWarehouse.Client.Services;

// پیش‌نویس فرم‌ها در localStorage با کلید «کاربر + فرم» (بخش ۱۵.۲ spec).
// فقط با ثبت موفق یا «پاک کردن پیش‌نویس» پاک می‌شود؛ با reload، خروج و بستن مرورگر می‌ماند.
// رمز هرگز نباید به آن داده شود.
public sealed class DraftStore(IJSRuntime js, ApiAuthenticationStateProvider session)
{
    private static readonly JsonSerializerOptions Json = new();

    private string Key(string form) => $"pw-draft-{session.CurrentUser?.Id}-{form}";

    public Draft<T> Open<T>(string form, string? legacyKey = null) where T : class => new(this, form, legacyKey);

    internal async Task<string?> ReadAsync(string form, string? legacyKey)
    {
        try
        {
            var json = await js.InvokeAsync<string?>("localStorage.getItem", Key(form));
            if (!string.IsNullOrEmpty(json) || legacyKey is null) return json;

            // مهاجرت پیش‌نویس قدیمی (قبل از DraftStore) به کلید تازه.
            json = await js.InvokeAsync<string?>("localStorage.getItem", legacyKey);
            if (!string.IsNullOrEmpty(json))
            {
                await js.InvokeVoidAsync("localStorage.setItem", Key(form), json);
                await js.InvokeVoidAsync("localStorage.removeItem", legacyKey);
            }

            return json;
        }
        catch (JSException ex)
        {
            Console.WriteLine(ex.Message);
            return null;
        }
    }

    internal async Task WriteAsync(string form, string? json)
    {
        try
        {
            if (json is null) await js.InvokeVoidAsync("localStorage.removeItem", Key(form));
            else await js.InvokeVoidAsync("localStorage.setItem", Key(form), json);
        }
        catch (JSException ex)
        {
            // localStorage پر یا بسته: کار ادامه دارد، فقط پیش‌نویس نمی‌ماند.
            Console.WriteLine(ex.Message);
        }
    }

    internal static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);
    internal static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Json);
}

/// <summary>وضعیت نمایشی پیش‌نویس یک فرم؛ <see cref="DraftBar"/> آن را نشان می‌دهد.</summary>
public class DraftState
{
    public bool Saved { get; protected set; }
    public bool Restored { get; protected set; }
    public bool Has { get; protected set; }
}

public sealed class Draft<T>(DraftStore store, string form, string? legacyKey) : DraftState where T : class
{
    private string? _last;

    /// <summary>پیش‌نویس ذخیره‌شده یا null. خرابی JSON نادیده گرفته می‌شود.</summary>
    public async Task<T?> LoadAsync()
    {
        var json = await store.ReadAsync(form, legacyKey);
        if (string.IsNullOrEmpty(json)) return null;

        try
        {
            var value = DraftStore.Deserialize<T>(json);
            _last = json;
            Has = Restored = value is not null;
            return value;
        }
        catch (JsonException ex)
        {
            Console.WriteLine(ex.Message);
            return null;
        }
    }

    /// <summary>ذخیره؛ value null یعنی چیزی برای نگه‌داشتن نیست و پیش‌نویس پاک می‌شود. متن یکسان دوباره نوشته نمی‌شود.</summary>
    public async Task SaveAsync(T? value)
    {
        var json = value is null ? null : DraftStore.Serialize(value);
        if (json == _last) return;

        _last = json;
        await store.WriteAsync(form, json);
        Has = json is not null;
        Saved = Has;
        if (!Has) Restored = false;
    }

    /// <summary>پس از ثبت موفق یا «پاک کردن پیش‌نویس».</summary>
    public async Task ClearAsync()
    {
        _last = null;
        await store.WriteAsync(form, null);
        Has = Saved = Restored = false;
    }
}
