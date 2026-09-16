using System.Globalization;
using Microsoft.JSInterop;

namespace ModbusTools.Browser;

/// <summary>
/// Access to the browser's Web Serial API (Chromium desktop browsers, secure context only). Keeps one
/// <see cref="WebSerialPort"/> instance per granted port so references stay valid across refreshes.
/// </summary>
public sealed class WebSerialService(IJSRuntime js) : IAsyncDisposable
{
    private List<WebSerialPort> ports = [];

    public IReadOnlyList<WebSerialPort> Ports => ports;

    public async ValueTask<bool> IsSupportedAsync()
    {
        await using var navigator = await js.GetValueAsync<IJSObjectReference>("navigator");
        return await js.InvokeAsync<bool>("Reflect.has", navigator, "serial");
    }

    /// <summary>Reloads the list of ports the user has already granted access to.</summary>
    public async Task<IReadOnlyList<WebSerialPort>> RefreshPortsAsync()
    {
        var refreshed = new List<WebSerialPort>();
        await using (var granted = await js.InvokeAsync<IJSObjectReference>("navigator.serial.getPorts"))
        {
            var count = await granted.GetValueAsync<int>("length");
            for (var i = 0; i < count; i++)
            {
                var handle = await granted.GetValueAsync<IJSObjectReference>(i.ToString(CultureInfo.InvariantCulture));
                var existing = await FindAsync(handle);
                if (existing is not null)
                {
                    await handle.DisposeAsync();
                    refreshed.Add(existing);
                }
                else
                {
                    var info = await handle.InvokeAsync<SerialPortInfo>("getInfo");
                    refreshed.Add(new WebSerialPort(handle, info));
                }
            }
        }

        foreach (var removed in ports.Except(refreshed))
        {
            await removed.Handle.DisposeAsync();
        }

        foreach (var group in refreshed.GroupBy(p => p.DeviceKey))
        {
            var ordinal = 1;
            foreach (var port in group)
            {
                port.AssignOrdinal(ordinal++);
            }
        }

        ports = refreshed;
        return ports;
    }

    /// <summary>
    /// Shows the browser's port picker. Must be called from a user gesture such as a click handler.
    /// Returns the chosen port, or null if the user dismissed the picker.
    /// </summary>
    public async Task<WebSerialPort?> RequestPortAsync()
    {
        IJSObjectReference handle;
        try
        {
            handle = await js.InvokeAsync<IJSObjectReference>("navigator.serial.requestPort");
        }
        catch (JSException ex) when (ex.Message.Contains("No port selected", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        await using (handle)
        {
            await RefreshPortsAsync();
            return await FindAsync(handle);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var port in ports)
        {
            await port.Handle.DisposeAsync();
        }

        ports = [];
    }

    private async Task<WebSerialPort?> FindAsync(IJSObjectReference handle)
    {
        foreach (var port in ports)
        {
            if (await js.InvokeAsync<bool>("Object.is", port.Handle, handle))
            {
                return port;
            }
        }

        return null;
    }
}
