# DotLuxafor

.NET library for controlling [Luxafor](https://luxafor.com/) LED devices via HID using [HidSharp](https://github.com/IntegratedCircuits/HidSharp).

Supports **Luxafor Flag**, **Bluetooth Pro** (via USB dongle), **Mute Button**, **Smart Button**, and **Colorblind** devices.

## Features

- **Async-first API** with `CancellationToken` support
- **Commands**: Static color, fade, strobe, wave, and built-in patterns
- **Per-LED targeting**: Individual LEDs, top/bottom side, or all
- **Event streaming** via `IAsyncEnumerable<LuxaforEvent>` — battery level, mute button, pattern completion
- **Device identification**: Auto-detect device type and serial number
- **Multi-device support**: Control multiple Luxafor devices simultaneously
- **Dependency injection** integration built in (net8.0+), no extra package needed
- **Hotplug aware**: wait for a device to be plugged in instead of polling for it
- **Predefined colors**, hex parsing (`#RRGGBB`, `#RGB`, with or without `#`), and configuration binding
- Targets `netstandard2.0` and `net8.0`

## Installation

```
dotnet add package DotLuxafor
```

## Quick Start

```csharp
using DotLuxafor;

using var device = LuxaforDevices.TryOpen();
if (device is null)
{
    Console.WriteLine("No Luxafor device found.");
    return;
}

// Solid color
await device.SetColorAsync(LuxaforColor.Red);

// Fade
await device.FadeToAsync(LuxaforColor.Green, speed: 20);

// Strobe
await device.StrobeAsync(LuxaforColor.Blue, speed: 10, repeat: 5);

// Wave
await device.WaveAsync(WaveType.Smooth, LuxaforColor.Cyan, speed: 10, repeat: 3);

// Built-in pattern
await device.PlayPatternAsync(BuiltInPattern.Rainbow, repeat: 3);

// Per-LED control
await device.SetColorAsync(LuxaforColor.Red, LedTarget.TopSide);
await device.SetColorAsync(LuxaforColor.Green, LedTarget.BottomSide);

// Hex color
await device.SetColorAsync(LuxaforColor.FromHex("#FF8800"));

// RGB bytes (via extension method)
await device.SetColorAsync(255, 136, 0);

// Turn off
await device.TurnOffAsync();
```

## Event Monitoring

Monitor device events using `await foreach`. Events are delivered as a strongly-typed discriminated union:

```csharp
using var device = LuxaforDevices.TryOpen()!;

// Stream events — monitoring starts automatically
await foreach (var evt in device.ObserveAsync(cancellationToken))
{
    switch (evt)
    {
        case LuxaforEvent.DeviceIdentified { Info: var info }:
            Console.WriteLine($"Device: {info.Type}, Serial: {info.SerialNumber}");
            break;

        case LuxaforEvent.DongleDataReceived { Info: var info }:
            Console.WriteLine($"Battery: {info.BatteryLevel}% ({info.BatteryStatus})");
            Console.WriteLine($"RSSI: {info.Rssi} dBm, Present: {info.IsDevicePresent}");
            break;

        case LuxaforEvent.MuteButtonStateChanged { IsPressed: var pressed }:
            Console.WriteLine(pressed ? "Muted" : "Unmuted");
            break;

        case LuxaforEvent.PatternCompleted:
            Console.WriteLine("Pattern finished.");
            break;

        case LuxaforEvent.Disconnected:
            Console.WriteLine("Device disconnected.");
            break;

        case LuxaforEvent.ReadError { Exception: var ex }:
            Console.WriteLine($"Read error: {ex.Message}");
            break;
    }
}
```

## Dependency Injection

DI support is built-in (net8.0+), no extra package needed.

```csharp
// Basic registration — registers ILuxaforDeviceManager
builder.Services.AddLuxafor();

// With background hosted service for auto-reconnect and monitoring
builder.Services.AddLuxaforHostedService(options =>
{
    options.AutoReconnect = true;
    options.AutoMonitor = true;
    options.ReconnectDelay = TimeSpan.FromSeconds(5);
});
```

With the hosted service, inject `ILuxaforDeviceAccessor` to reach the device it keeps open. Do not
dispose it and do not cache it in a field — the service owns it and replaces it across reconnects:

```csharp
public class StatusService(ILuxaforDeviceAccessor luxafor)
{
    public Task SetBusyAsync(CancellationToken ct)
        => luxafor.Current?.SetColorAsync(LuxaforColor.Red, cancellationToken: ct)
           ?? Task.CompletedTask;
}
```

To block until a device is available rather than skipping the work:

```csharp
var device = await luxafor.WaitForDeviceAsync(ct);
await device.SetColorAsync(LuxaforColor.Red, cancellationToken: ct);
```

Without the hosted service, inject `ILuxaforDeviceManager` and open devices yourself:

```csharp
public class StatusService(ILuxaforDeviceManager manager)
{
    private ILuxaforDevice? _device;

    public async Task SetBusyAsync(CancellationToken ct)
    {
        _device ??= manager.TryOpen();
        if (_device is not null)
        {
            await _device.SetColorAsync(LuxaforColor.Red, cancellationToken: ct);
        }
    }
}
```

### Registered Services

| Service | Lifetime | Registered by | Description |
|---------|----------|---------------|-------------|
| `ILuxaforDeviceManager` | Singleton | both | Device discovery (TryOpen, Open, OpenAll, IsDevicePresent, WaitForDeviceAsync) |
| `IOptions<LuxaforOptions>` | Singleton | both | Configuration options |
| `ILuxaforDeviceAccessor` | Singleton | `AddLuxaforHostedService` | Access to the device the background service holds open |

`ILuxaforDeviceAccessor` is the same instance as the running `LuxaforHostedService`, so what it
reports is what the service actually has open.

No device is opened during registration — this is safe even when no device is connected.

### Reconnection

With `AutoReconnect`, the service reconnects on the operating system's hotplug notification, so
plugging a device in is picked up at once. `ReconnectDelay` is the fallback interval it re-checks on
when no notification arrives, and the interval it uses when a device is attached but will not open
(retrying that on hotplug would spin, since the device is already there).

## Device Discovery

```csharp
// Open first device
using var device = LuxaforDevices.TryOpen();

// Open all connected devices, skipping any that will not open
var devices = LuxaforDevices.OpenAll();

// ...or see the outcome for every one of them
foreach (var result in LuxaforDevices.OpenAllResults())
{
    if (result.Device is null)
    {
        Console.Error.WriteLine(result.Description);
    }
}

// Check without opening
bool present = LuxaforDevices.IsDevicePresent();

// Wait for one to be plugged in (hotplug-driven, not polling)
await LuxaforDevices.WaitForDeviceAsync(cancellationToken);
using var device = LuxaforDevices.TryOpen();

// Using the manager directly (useful for DI or custom logic)
var manager = new LuxaforDeviceManager();
using var device = manager.TryOpen();
```

### Diagnosing a failed open

`TryOpen()` returns `null` both when no device is plugged in and when one is plugged in but the
operating system refuses to open it. Use `Open()` when you need to tell those apart:

```csharp
var result = LuxaforDevices.Open();

if (result.Device is null)
{
    // e.g. "A Luxafor device is connected, but the operating system denied access to it.
    //       On macOS, HID access requires the Input Monitoring permission ..."
    Console.Error.WriteLine(result.Description);
    return;
}

using var device = result.Device;
```

`result.Status` is one of `Opened`, `NotFound`, `AccessDenied`, `InUse` or `Failed`, and
`result.Error` carries the original exception from the HID stack.

Platform notes for `AccessDenied`:

| Platform | Cause | Fix |
|----------|-------|-----|
| macOS | HID access is gated by TCC | Grant Input Monitoring in System Settings > Privacy & Security. App Sandbox blocks IOKit HID access outright, regardless of that setting. |
| Linux | `/dev/hidraw*` is root-only by default | Add a udev rule for `04d8:f372`. |
| Windows | Another process holds the device | Reported as `InUse`; close the official Luxafor software. |

## API Reference

### Interfaces

#### ILuxaforCommands

| Method | Description |
|--------|-------------|
| `SetColorAsync(color, target, ct)` | Sets LED(s) to a solid color |
| `FadeToAsync(color, speed, target, ct)` | Smoothly transitions LED(s) to a color |
| `StrobeAsync(color, speed, repeat, target, ct)` | Flashes LED(s) with a color |
| `WaveAsync(type, color, speed, repeat, ct)` | Plays a wave animation |
| `PlayPatternAsync(pattern, repeat, ct)` | Plays a built-in hardware pattern |
| `TurnOffAsync(ct)` | Turns off all LEDs |

All methods default to `LedTarget.All` when `target` is omitted.

#### ILuxaforConnection

| Member | Description |
|--------|-------------|
| `IsConnected` | Whether the device connection is active |
| `DeviceInfo` | Device type and serial, or `null` until identified |
| `RequestDeviceInfoAsync(ct)` | Fills `DeviceInfo` with the device type and serial number |

#### ILuxaforDeviceManager

| Method | Description |
|--------|-------------|
| `TryOpen()` | Opens the first device, or `null` |
| `Open()` | Opens the first device, reporting why it failed |
| `OpenAll()` | Opens every connected device, skipping any that will not open |
| `OpenAllResults()` | Opens every connected device, reporting the outcome of each attempt |
| `IsDevicePresent()` | Whether a device is attached, without opening it |
| `WaitForDeviceAsync(ct)` | Waits until a device is attached; does not open it |

`WaitForDeviceAsync` is satisfied by a device that is attached but cannot be opened, so do not use
it to drive a retry loop around a failing `Open()`.

#### ILuxaforDeviceAccessor

Registered by `AddLuxaforHostedService`. See [Dependency Injection](#dependency-injection).

| Member | Description |
|--------|-------------|
| `Current` | The device the background service has open, or `null` |
| `WaitForDeviceAsync(ct)` | Waits until the background service has a device open |

#### ILuxaforMonitor

| Method | Description |
|--------|-------------|
| `ObserveAsync(ct)` | Streams device events as `IAsyncEnumerable<LuxaforEvent>` |

### LuxaforEvent Types

| Event | Data | Source |
|-------|------|--------|
| `DeviceIdentified` | `DeviceInfo` (Type, SerialNumber) | All devices |
| `DongleDataReceived` | `DongleInfo` (Battery, RSSI, Presence) | Bluetooth Pro |
| `MuteButtonStateChanged` | `bool IsPressed` | Mute Button |
| `PatternCompleted` | — | All devices |
| `Disconnected` | — | All devices |
| `ReadError` | `Exception` | All devices |

### Extension Methods

RGB byte overloads are available as extension methods on `ILuxaforCommands`:

```csharp
await device.SetColorAsync(255, 0, 0);                          // RGB bytes
await device.FadeToAsync(0, 255, 0, speed: 20);                 // RGB bytes + speed
await device.StrobeAsync(0, 0, 255, speed: 10, repeat: 5);      // RGB bytes + speed + repeat
await device.WaveAsync(WaveType.Short, 255, 128, 0, speed: 10, repeat: 3);
```

### Enums

| Enum | Values |
|------|--------|
| `LedTarget` | `All`, `TopSide`, `BottomSide`, `Led1`–`Led6` |
| `WaveType` | `Short`, `Long`, `ShortOverlapping`, `LongOverlapping`, `Smooth` |
| `BuiltInPattern` | `TrafficLights`, `Random1`–`Random5`, `Police`, `Rainbow` |
| `DeviceType` | `Standard`, `Bluetooth`, `MuteButton`, `SmartButton`, `Colorblind` |
| `BatteryStatus` | `NotConnected`, `Charging`, `Full` |

### LuxaforColor

```csharp
// Predefined colors
LuxaforColor.Red, .Green, .Blue, .Yellow, .Cyan, .Magenta, .White, .Off

// From RGB bytes
new LuxaforColor(255, 128, 0)

// From hex — #RRGGBB or #RGB, with or without the '#', any case, whitespace ignored
LuxaforColor.FromHex("#FF8800")
LuxaforColor.FromHex("ff8800")
LuxaforColor.FromHex("#F80")     // → #FF8800
LuxaforColor.TryFromHex("#FF8800", out var color)

// To hex
color.ToHex() // → "#FF8800"

// Dim and blend
LuxaforColor.Red.WithBrightness(0.25)                          // → #400000
LuxaforColor.Lerp(LuxaforColor.Off, LuxaforColor.White, 0.5)   // → #808080
```

`WithBrightness` and `Lerp` clamp their factor to 0.0–1.0, so animation code does not have to
range-check. Both operate directly on the RGB channels — neither is gamma-corrected.

#### Binding from configuration

`LuxaforColor` carries a `TypeConverter`, so it binds straight out of `appsettings.json`:

```jsonc
{ "Status": { "BusyColor": "#FF8800" } }
```

```csharp
builder.Services.Configure<StatusOptions>(builder.Configuration.GetSection("Status"));

public sealed class StatusOptions
{
    public LuxaforColor BusyColor { get; set; } = LuxaforColor.Red;
}
```

On net8.0 it also implements `IParsable<LuxaforColor>`, which is what minimal-API route and query
binding looks for:

```csharp
app.MapPost("/color/{color}", (LuxaforColor color, ILuxaforDeviceAccessor luxafor)
    => luxafor.Current?.SetColorAsync(color) ?? Task.CompletedTask);
```

## Supported Devices

| Device | Commands | Battery/RSSI | Mute Button |
|--------|----------|--------------|-------------|
| Luxafor Flag | Yes | No | No |
| Luxafor Bluetooth Pro | Yes | Yes (via dongle) | No |
| Luxafor Mute Button | Yes | Yes (via dongle) | Yes |
| Luxafor Smart Button | Yes | Yes (via dongle) | No |

## Device Identification

`RequestDeviceInfoAsync()` fills the `DeviceInfo` property; read it once the call returns:

```csharp
await device.RequestDeviceInfoAsync();

if (device.DeviceInfo is { } info)
{
    Console.WriteLine($"{info.Type}, serial {info.SerialNumber}");
}
```

The value is read from a HID feature report when the device supports one, and from the USB HID
descriptor otherwise. It does **not** arrive as a `DeviceIdentified` event — that event only appears
when the device pushes an identification report of its own accord during monitoring.

## Thread Safety

All command methods are thread-safe and can be called concurrently with event monitoring, and
`DeviceInfo` is safe to read while monitoring updates it. Events from `ObserveAsync()` are delivered
on a background thread — UI marshalling is the caller's responsibility.

Commands are asynchronous because they are serialized against each other, not because the HID write
is asynchronous: `HidStream` offers only a blocking write, and a nine-byte report completes in well
under a millisecond, so it is written inline rather than handed to a thread-pool thread. A
`cancellationToken` therefore cancels the wait for the device to become free, not a write that has
already begun.

Events are buffered (64 deep) and delivered in order. A consumer that falls further behind than that
applies backpressure to the reader instead of losing events, so keep the body of the `await foreach`
short and hand slow work to another task.

## License

MIT
