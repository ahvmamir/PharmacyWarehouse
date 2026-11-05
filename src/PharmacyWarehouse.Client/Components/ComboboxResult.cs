namespace PharmacyWarehouse.Client.Components;

// پاسخ جست‌وجوی Combobox. AutoPick فقط برای انطباق قطعی (بارکد دقیق) است؛ Items صرفاً پیشنهادند و خودکار انتخاب نمی‌شوند.
public sealed record ComboboxResult<TItem>(IReadOnlyList<TItem> Items, TItem? AutoPick = null, string? Error = null) where TItem : class;
