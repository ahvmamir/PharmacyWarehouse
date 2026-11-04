namespace PharmacyWarehouse.Client.Services;

public sealed record Toast(Guid Id, string Message, bool IsWarning);

// پیام کوتاه بعد از هر کار موفق؛ جابه‌جایی بین صفحه‌ها آن را از بین نمی‌برد.
public sealed class ToastService
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(5);

    private readonly List<Toast> _toasts = [];

    public IReadOnlyList<Toast> Toasts => _toasts;

    public event Action? Changed;

    public void Show(string message, bool isWarning = false)
    {
        var toast = new Toast(Guid.NewGuid(), message, isWarning);
        _toasts.Add(toast);
        Changed?.Invoke();

        _ = RemoveLaterAsync(toast.Id, isWarning ? Lifetime * 2 : Lifetime);
    }

    public void Dismiss(Guid id)
    {
        if (_toasts.RemoveAll(t => t.Id == id) > 0) Changed?.Invoke();
    }

    private async Task RemoveLaterAsync(Guid id, TimeSpan delay)
    {
        await Task.Delay(delay);
        Dismiss(id);
    }
}
