# DotLuxafor

.NET library and tools for controlling [Luxafor](https://luxafor.com/) LED devices via HID.

Supports **Luxafor Flag**, **Bluetooth Pro** (via USB dongle), **Mute Button**, **Smart Button**, and
**Colorblind** devices, on Windows, macOS and Linux.

[![Build & Test](https://github.com/vesnicancz/dotluxafor/actions/workflows/build.yml/badge.svg)](https://github.com/vesnicancz/dotluxafor/actions/workflows/build.yml)
[![NuGet](https://img.shields.io/nuget/v/DotLuxafor.svg)](https://www.nuget.org/packages/DotLuxafor)

## Projects

| Project | Description |
|---------|-------------|
| [`src/DotLuxafor`](src/DotLuxafor) | The library, published to NuGet as [`DotLuxafor`](https://www.nuget.org/packages/DotLuxafor). Targets `netstandard2.0` and `net8.0`. |
| [`src/DotLuxafor.Cli`](src/DotLuxafor.Cli) | Command-line interface for driving a device from a shell or a script. |
| [`src/DotLuxafor.ControlPanel`](src/DotLuxafor.ControlPanel) | Avalonia desktop app for controlling a device and watching its events. |

## Quick start

```
dotnet add package DotLuxafor
```

```csharp
using DotLuxafor;

using var device = LuxaforDevices.TryOpen();
await device!.SetColorAsync(LuxaforColor.Red);
```

The full API documentation — commands, event monitoring, dependency injection, and how to diagnose a
device the operating system refuses to open — lives in the
[library README](src/DotLuxafor/README.md).

## Building

```
dotnet build
dotnet test
```

Requires the .NET 10 SDK (see [`global.json`](global.json)). The library itself targets
`netstandard2.0` and `net8.0`, so consumers do not need .NET 10.

## License

MIT — see [LICENSE](LICENSE).
