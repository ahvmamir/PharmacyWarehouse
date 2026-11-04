using Microsoft.JSInterop;

namespace PharmacyWarehouse.Client.Services;

public static class FileSaver
{
    public static ValueTask SaveAsync(IJSRuntime js, DownloadedFile file) =>
        js.InvokeVoidAsync("emuSaveFile", file.Name, file.ContentType, Convert.ToBase64String(file.Content));
}
