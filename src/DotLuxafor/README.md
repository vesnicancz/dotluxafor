# DotLuxafor

.NET library for controlling [Luxafor](https://luxafor.com/) LED devices via HID using [HidSharp](https://github.com/IntegratedCircuits/HidSharp).

Supports **Luxafor Flag**, **Bluetooth Pro** (via USB dongle), **Mute Button**, **Smart Button**, and **Colorblind** devices.

## Features

- **Async-first API** with `CancellationToken` support
- **Commands**: Static color, fade, strobe, wave, and built-in patterns
- **Per-LED targeting**: Individual LEDs, top/bottom side, or all
- **Event streaming** via `IAsyncEnumerable<LuxaforEvent>` — battery level, mute button, pattern completion
- **Device identification**: Auto-detect device type and serial number
- **Multi-device support**: list attached devices, open a specific one by path, control several at once
- **Dependency injection** integration built in (net8.0+), no extra package needed, with the
  background service pinned to a chosen device by serial number, path or your own rule
- **Hotplug aware**: wait for a device to be plugged in instead of polling for it
- **Software animations**: fades between arbitrary colors, brightness pulses, scoped colors
- **Predefined colors**, parsing by name, hex (`#RRGGBB`, `#RGB`) or `r,g,b`, and configuration binding
- Targets `netstandard2.0`, `net8.0` and `net10.0`

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

## Software Animations

`FadeToAsync` and `StrobeAsync` run on the device itself and keep going after your process stops
awaiting them — prefer them where they suffice. The animations below run on the computer, sending
static-color reports at about 40 Hz, which buys effects the hardware cannot produce on its own:

```csharp
// Fade between two arbitrary colors over a wall-clock duration
await device.FadeOverAsync(LuxaforColor.Green, LuxaforColor.Red, TimeSpan.FromSeconds(2), cancellationToken: ct);

// ...or from wherever the device is resting now
await device.FadeOverAsync(LuxaforColor.Red, TimeSpan.FromSeconds(2), cancellationToken: ct);

// Pulse three times, then leave the color showing
await device.PulseAsync(LuxaforColor.Yellow, TimeSpan.FromMilliseconds(800), cycles: 3, cancellationToken: ct);

// Pulse until cancelled
await device.PulseAsync(LuxaforColor.Red, TimeSpan.FromSeconds(1), cancellationToken: ct);
```

The position of a fade is taken from the clock rather than a frame counter, so it takes the time it
was asked for even when the device is slow to accept reports, and it always finishes by setting the
destination color exactly.

### Scoped colors

To show a color for the duration of some work and then put the light back:

```csharp
await using (await device.SetColorScopedAsync(LuxaforColor.Red, cancellationToken: ct))
{
    await RunTheBuildAsync(ct);
}
// back to whatever the device was showing before
```

The color to restore is read from `LastColor` — what this library last set the whole device to —
falling back to `Off` when that is not known. Pass it explicitly when you know better:

```csharp
await using (await device.SetColorScopedAsync(LuxaforColor.Red, restoreTo: LuxaforColor.Green, cancellationToken: ct))
```

Restoring deliberately ignores the cancellation token: a scope that ends *because* its token was
cancelled is exactly the case where the light still has to be put back.

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
| `ILuxaforDeviceManager` | Singleton | both | Device discovery (TryOpen, Open, OpenAll, List, IsDevicePresent, IsPresent, WaitForDeviceAsync) |
| `ILuxaforDeviceOpener` | Singleton | both | The same manager, narrowed to `Open()` — depend on this when you do not choose the device |
| `IOptions<LuxaforOptions>` | Singleton | both | Configuration options |
| `ILuxaforDeviceAccessor` | Singleton | `AddLuxaforHostedService` | Access to the device the background service holds open |

`ILuxaforDeviceAccessor` is the same instance as the running `LuxaforHostedService`, so what it
reports is what the service actually has open.

No device is opened during registration — this is safe even when no device is connected.

### Choosing which device the service opens

By default the hosted service opens whatever the platform enumerates first. With more than one
Luxafor attached, name the one you want:

```csharp
builder.Services.AddLuxaforHostedService(options =>
{
    options.AutoReconnect = true;

    // The stable way to name one device: it survives a replug into another port.
    options.SerialNumber = "1001";
});
```

Three selectors are available, and at most one may be set — configuring two fails validation at
startup rather than quietly letting one win:

| Option | Binds from config | Use it for |
|--------|-------------------|------------|
| `SerialNumber` | yes | Pinning one device for good; survives a replug into another port |
| `DevicePath` | yes | A path a user just picked out of `List()`; valid while the device stays in that port |
| `SelectDevice` | no (delegate) | Any other rule — matching on product name, preferring one device but settling for another |

```csharp
options.SelectDevice = devices =>
    devices.FirstOrDefault(d => d.ProductName?.Contains("MUTE") == true);
```

`SelectDevice` is called on every connection attempt with the devices attached at that moment, never
with an empty list. Returning `null` means "none of these", and the service tries again on the next
attempt rather than falling back to the wrong device.

When a selector matches nothing but other Luxafors are attached, the service logs a warning naming
what *is* attached — a mistyped serial number is fixable straight from the log. Nothing attached at
all stays at `Debug`, since waiting for a device to be plugged in is the ordinary case.

### Reconnection

With `AutoReconnect`, the service reconnects on the operating system's hotplug notification, so
plugging a device in is picked up at once. `ReconnectDelay` is the fallback interval it re-checks on
when no notification arrives, and the interval it uses when a device is attached but will not open
(retrying that on hotplug would spin, since the device is already there). A device that is attached
but is not the one a selector asks for is the same case, and uses the same interval.

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

// List what is attached, without opening anything
foreach (var d in LuxaforDevices.List())
{
    Console.WriteLine($"{d} at {d.DevicePath}");
}

// Check without opening
bool present = LuxaforDevices.IsDevicePresent();

// Check whether one particular device is still attached
bool stillThere = LuxaforDevices.IsPresent(device.Descriptor!);

// Wait for one to be plugged in (hotplug-driven, not polling)
await LuxaforDevices.WaitForDeviceAsync(cancellationToken);
using var device = LuxaforDevices.TryOpen();

// Using the manager directly (useful for DI or custom logic)
var manager = new LuxaforDeviceManager();
using var device = manager.TryOpen();
```

### Choosing a device

`Open()` takes whatever the platform enumerates first, which is not guaranteed to be the same device
across replugs. When it matters which one you get — a picker in a UI, a `--device` flag, or simply
reopening the same device after a reconnect — go through `List()`:

```csharp
// Offer a choice
IReadOnlyList<LuxaforDeviceDescriptor> attached = LuxaforDevices.List();
LuxaforDeviceDescriptor chosen = attached[index];

// ...and open exactly that one
using var device = LuxaforDevices.Open(chosen).Device;

// The path is what to persist; it is stable while the device stays in the same port
string path = chosen.DevicePath;
using var again = LuxaforDevices.Open(path).Device;
```

`List()` opens nothing, so a device the operating system will not let you open still shows up —
which is the device a user most needs to see in a picker. Its `ProductName` and `SerialNumber` come
from USB string descriptors and are `null` when the platform will not hand them over.

Every opened device carries the descriptor it was opened as, which is how devices from `OpenAll()`
are told apart:

```csharp
foreach (var device in LuxaforDevices.OpenAll())
{
    Console.WriteLine($"{device.Descriptor}: {device.Descriptor?.DevicePath}");
}
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

### When a device goes away mid-use

Once a device is open, a command that can no longer reach it throws
`LuxaforDeviceDisconnectedException` — whether the device went away before the write or during it:

```csharp
try
{
    await device.SetColorAsync(LuxaforColor.Red, cancellationToken: ct);
}
catch (LuxaforDeviceDisconnectedException ex)
{
    // ex.Descriptor names the device that went away, and carries the DevicePath
    // needed to reopen it once it comes back.
    Console.Error.WriteLine(ex.Message);
}
```

It derives from `InvalidOperationException`, which is what the library threw before the type
existed, so code already catching that keeps working. Catching the specific type is what tells a
vanished device apart from a misuse of the API — a second `ObserveAsync` consumer, say — which
still reports the plain `InvalidOperationException`.

A device that *you* disposed throws `ObjectDisposedException` instead: that says the caller let go
of the device, not that the hardware left. While monitoring, the same event arrives as
`LuxaforEvent.Disconnected` rather than as an exception.

`IsConnected` will *not* tell you: it reports whether the handle was closed, and an unplugged device
does not close it. To find out before sending a command, ask the manager:

```csharp
if (device.Descriptor is not null && !manager.IsPresent(device.Descriptor))
{
    device.Dispose();
    device = manager.Open(device.Descriptor).Device;
}
```

The exception is constructible: `new LuxaforDeviceDisconnectedException(descriptor, inner)` builds
the same thing the library throws, message and all, so a test double can make the handling below
actually run.

The reliable pattern is to act on the exception rather than to poll: catch
`LuxaforDeviceDisconnectedException`, dispose the handle, reopen through `ex.Descriptor` and repeat
the command — otherwise the command is lost until whatever drives the device next comes round.
`AddLuxaforHostedService` does this for the device it holds, on its reconnect timer.

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

All methods default to `LedTarget.All` when `target` is omitted. Every one of them throws
`LuxaforDeviceDisconnectedException` when the device is gone and `ObjectDisposedException` when it
has been disposed — see [When a device goes away mid-use](#when-a-device-goes-away-mid-use).

#### ILuxaforConnection

| Member | Description |
|--------|-------------|
| `IsConnected` | Whether this handle is still usable — **not** whether the device is plugged in |
| `Descriptor` | How the device was identified when opened, or `null` if not opened via the manager |
| `DeviceInfo` | Device type and serial, or `null` until identified |
| `LastColor` | The color this library last set the whole device to, or `null` when unknown |
| `RequestDeviceInfoAsync(ct)` | Fills `DeviceInfo` with the device type and serial number |

`IsConnected` reports the handle, not the hardware: nothing polls the USB bus, so a device whose
cable was pulled goes on reporting `true` until something touches it. Read it as "we have not closed
this". To ask whether the device is still there, call `manager.IsPresent(device.Descriptor)` — see
[When a device goes away mid-use](#when-a-device-goes-away-mid-use).

`LastColor` is set by `SetColorAsync` and `FadeToAsync` when they target `LedTarget.All`. It is
`null` before the first such command, after a per-LED command, and after a strobe, wave or pattern —
in each of those cases the device as a whole has no single resting color. It reports what the
library sent, not a reading from the device: the hardware cannot be asked what it is showing.

#### ILuxaforDeviceManager

| Method | Description |
|--------|-------------|
| `TryOpen()` | Opens the first device, or `null` |
| `Open()` | Opens the first device, reporting why it failed |
| `Open(devicePath)` | Opens the device at a specific path, reporting why it failed |
| `Open(descriptor)` | Opens the device a descriptor identifies |
| `List()` | Lists attached devices without opening any of them |
| `OpenAll()` | Opens every connected device, skipping any that will not open |
| `OpenAllResults()` | Opens every connected device, reporting the outcome of each attempt |
| `IsDevicePresent()` | Whether a device is attached, without opening it |
| `IsPresent(descriptor)` | Whether **that** device is still attached, without opening it |
| `WaitForDeviceAsync(ct)` | Waits until a device is attached; does not open it |

`WaitForDeviceAsync` is satisfied by a device that is attached but cannot be opened, so do not use
it to drive a retry loop around a failing `Open()`.

`IsPresent` costs a device enumeration, so call it where a stale answer would cost you something —
before a command that must not silently do nothing, or on a reconnect timer — not in a loop.

#### ILuxaforDeviceOpener

`ILuxaforDeviceManager` inherits from it, and both resolve to the same singleton from DI.

| Method | Description |
|--------|-------------|
| `Open()` | Opens a device, reporting why the attempt failed |

Most of `ILuxaforDeviceManager` is about *choosing* between devices. Code that only wants something
to light up can depend on this instead, and a test double is one method rather than nine:

```csharp
public class StatusService(ILuxaforDeviceOpener opener, ILogger<StatusService> logger)
{
    public async Task SetBusyAsync(CancellationToken ct)
    {
        var result = opener.Open();
        if (!result.IsSuccess)
        {
            logger.LogWarning("{Reason}", result.Description);
            return;
        }

        using var device = result.Device!;
        await device.SetColorAsync(LuxaforColor.Red, cancellationToken: ct);
    }
}

// In a test:
var opener = new Mock<ILuxaforDeviceOpener>();
opener.Setup(o => o.Open()).Returns(DeviceOpenResult.Opened(fakeDevice, descriptor));
```

`DeviceOpenResult`'s factory methods — `Opened`, `NotFound`, `NotMatched`, `Failure` — are public
precisely so an implementation outside the library can return one.

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

Animations, on `ILuxaforCommands` (see [Software Animations](#software-animations)):

| Method | Description |
|--------|-------------|
| `FadeOverAsync(from, to, duration, target, ct)` | Software fade between two colors |
| `FadeOverAsync(to, duration, target, ct)` | Software fade from the current resting color |
| `PulseAsync(color, period, cycles, target, ct)` | Brightness pulse; `cycles: 0` runs until cancelled |
| `SetColorScopedAsync(color, target, ct)` | Sets a color and restores the previous one on dispose |
| `SetColorScopedAsync(color, restoreTo, target, ct)` | Sets a color and restores a chosen one on dispose |

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

// From any accepted spelling: a name, hex, or decimal RGB
LuxaforColor.Parse("red")
LuxaforColor.Parse("#FF8800")
LuxaforColor.Parse("255,136,0")
LuxaforColor.TryParse(userInput, out var color)

// From hex specifically — #RRGGBB or #RGB, with or without the '#', any case, whitespace ignored
LuxaforColor.FromHex("#FF8800")
LuxaforColor.FromHex("ff8800")
LuxaforColor.FromHex("#F80")     // → #FF8800
LuxaforColor.TryFromHex("#FF8800", out var hex)

// To hex
color.ToHex() // → "#FF8800"

// Dim and blend
LuxaforColor.Red.WithBrightness(0.25)                          // → #400000
LuxaforColor.Lerp(LuxaforColor.Off, LuxaforColor.White, 0.5)   // → #808080
```

`WithBrightness` and `Lerp` clamp their factor to 0.0–1.0, so animation code does not have to
range-check. Both operate directly on the RGB channels — neither is gamma-corrected. They are what
[the software animations](#software-animations) are built from.

The names `Parse` accepts are `red`, `green`, `blue`, `yellow`, `cyan`, `magenta`, `white` and
`off`/`black` — the colors this type declares as constants, and no more. `FromHex` stays hex-only,
so code that means "a hex color" still says so.

#### Binding from configuration

`LuxaforColor` carries a `TypeConverter`, so it binds straight out of `appsettings.json`:

```jsonc
{ "Status": { "BusyColor": "#FF8800" } }
// ...or "red", or "255,136,0" — the converter accepts everything Parse does
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
`DeviceInfo`, `LastColor` and `Descriptor` are safe to read while monitoring updates them. Events
from `ObserveAsync()` are delivered on a background thread — UI marshalling is the caller's
responsibility.

`ObserveAsync()` runs its blocking read loop on a dedicated background thread, not a thread-pool
thread: the loop blocks for the whole monitoring session, and parking that on the pool would cost it
a thread for as long as monitoring runs.

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
