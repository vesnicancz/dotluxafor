# Luxafor.HidSharp

.NET library for controlling [Luxafor](https://luxafor.com/) LED devices via HID using [HidSharp](https://github.com/IntegratedCircuits/HidSharp).

Supports **Luxafor Flag**, **Bluetooth Pro** (via USB dongle), **Mute Button**, **Smart Button**, and **Colorblind** devices.

## Features

- **Async-first API** with `CancellationToken` support
- **Commands**: Static color, fade, strobe, wave, and built-in patterns
- **Per-LED targeting**: Individual LEDs, top/bottom side, or all
- **Event streaming** via `IAsyncEnumerable<LuxaforEvent>` — battery level, mute button, pattern completion
- **Device identification**: Auto-detect device type and serial number
- **Multi-device support**: Control multiple Luxafor devices simultaneously
- **Dependency injection** integration via [Luxafor.HidSharp.DependencyInjection](https://www.nuget.org/packages/Luxafor.HidSharp.DependencyInjection)
- **Predefined colors** and hex color parsing
- Targets `netstandard2.0` and `net8.0`

## Installation

```
dotnet add package Luxafor.HidSharp
```

For DI integration:

```
dotnet add package Luxafor.HidSharp.DependencyInjection
```

## Quick Start

```csharp
using Luxafor.HidSharp;

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

// Request device identification (response arrives as an event)
await device.RequestDeviceInfoAsync();

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

Inject `ILuxaforDeviceManager` and open devices when needed:

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

| Service | Lifetime | Description |
|---------|----------|-------------|
| `ILuxaforDeviceManager` | Singleton | Device discovery (TryOpen, OpenAll, IsDevicePresent) |
| `IOptions<LuxaforOptions>` | Singleton | Configuration options |

No device is opened during registration — this is safe even when no device is connected.

## Device Discovery

```csharp
// Open first device
using var device = LuxaforDevices.TryOpen();

// Open all connected devices
var devices = LuxaforDevices.OpenAll();

// Check without opening
bool present = LuxaforDevices.IsDevicePresent();

// Using the manager directly (useful for DI or custom logic)
var manager = new LuxaforDeviceManager();
using var device = manager.TryOpen();
```

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
| `DeviceInfo` | Device type and serial (available after identification) |
| `RequestDeviceInfoAsync(ct)` | Requests device type and serial number |

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

// From hex
LuxaforColor.FromHex("#FF8800")
LuxaforColor.TryFromHex("#FF8800", out var color)

// To hex
color.ToHex() // → "#FF8800"
```

## Supported Devices

| Device | Commands | Battery/RSSI | Mute Button |
|--------|----------|--------------|-------------|
| Luxafor Flag | Yes | No | No |
| Luxafor Bluetooth Pro | Yes | Yes (via dongle) | No |
| Luxafor Mute Button | Yes | Yes (via dongle) | Yes |
| Luxafor Smart Button | Yes | Yes (via dongle) | No |

## Thread Safety

All command methods are thread-safe and can be called concurrently with event monitoring. Events from `ObserveAsync()` are delivered on a background thread — UI marshalling is the caller's responsibility.

## License

MIT
