// Web Serial API interop for SerialTest.razor.
// The module keeps a single open port for the lifetime of the page.

let port = null;
let dotNetRef = null;

export function isSupported() {
    return "serial" in navigator;
}

// Must run in response to a user gesture (the Connect button click),
// otherwise the browser refuses to show the port picker.
export async function connect(baudRate, callbackRef) {
    if (!isSupported()) {
        throw new Error("Web Serial API is not supported by this browser.");
    }

    if (port) {
        await disconnect();
    }

    const selected = await navigator.serial.requestPort();
    await selected.open({ baudRate });

    port = selected;
    dotNetRef = callbackRef;
    port.addEventListener("disconnect", onDeviceDisconnected);

    const { usbVendorId, usbProductId } = port.getInfo();
    return usbVendorId !== undefined
        ? `USB device ${toHex(usbVendorId)}:${toHex(usbProductId)}`
        : "serial port";
}

// bytes arrives from .NET (byte[]) as a Uint8Array.
export async function send(bytes) {
    if (!port?.writable) {
        throw new Error("The serial port is not open.");
    }

    const writer = port.writable.getWriter();
    try {
        await writer.write(bytes);
    } finally {
        writer.releaseLock();
    }
}

export async function disconnect() {
    if (!port) {
        return;
    }

    const closing = port;
    port = null;
    dotNetRef = null;
    closing.removeEventListener("disconnect", onDeviceDisconnected);

    try {
        await closing.close();
    } catch {
        // The port may already be closed or the device removed.
    }
}

function onDeviceDisconnected() {
    const ref = dotNetRef;
    port = null;
    dotNetRef = null;
    ref?.invokeMethodAsync("OnDeviceDisconnected");
}

function toHex(value) {
    return value.toString(16).toUpperCase().padStart(4, "0");
}
