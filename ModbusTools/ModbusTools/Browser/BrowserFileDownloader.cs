using Microsoft.JSInterop;

namespace ModbusTools.Browser;

/// <summary>Saves generated content as a browser download.</summary>
public sealed class BrowserFileDownloader(IJSRuntime js)
{
    public async Task DownloadAsync(string fileName, string contentType, byte[] content)
    {
        await using var blob = await js.InvokeConstructorAsync("Blob", new object[] { content }, new { type = contentType });
        var url = await js.InvokeAsync<string>("URL.createObjectURL", blob);
        try
        {
            await using var anchor = await js.InvokeAsync<IJSObjectReference>("document.createElement", "a");
            await anchor.SetValueAsync("href", url);
            await anchor.SetValueAsync("download", fileName);
            await anchor.InvokeVoidAsync("click");
        }
        finally
        {
            await js.InvokeVoidAsync("URL.revokeObjectURL", url);
        }
    }
}
