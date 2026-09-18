using System.Reflection;
using DotLuxafor;
using Moq;

namespace DotLuxafor.Tests;

public class DeviceOpenResultTests
{
    private static readonly LuxaforDeviceDescriptor Descriptor =
        new LuxaforDeviceDescriptor("/dev/hidraw0", "LUXAFOR FLAG", "1001");

    /// <summary>
    /// Guards the public surface rather than any behaviour. This assembly sees the library's
    /// internals, so every test here would compile just as well with the factories internal —
    /// which is exactly the state that left anyone implementing <see cref="ILuxaforDeviceOpener"/>
    /// outside the library unable to return a result at all.
    /// </summary>
    [Theory]
    [InlineData(nameof(DeviceOpenResult.Opened))]
    [InlineData(nameof(DeviceOpenResult.NotFound))]
    [InlineData(nameof(DeviceOpenResult.NotMatched))]
    [InlineData(nameof(DeviceOpenResult.Failure))]
    public void Factories_AreCallableFromOutsideTheLibrary(string name)
    {
        var overloads = typeof(DeviceOpenResult)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == name)
            .ToList();

        Assert.NotEmpty(overloads);
        Assert.All(overloads, m => Assert.True(m.IsPublic));
    }

    [Fact]
    public void Constructor_StaysPrivate()
    {
        // The named factories are the whole point: each fixes the state that goes with its status,
        // so no caller can build an Opened result with no device and make IsSuccess lie.
        Assert.Empty(typeof(DeviceOpenResult).GetConstructors());
    }

    [Fact]
    public void Opened_CarriesTheDeviceAndSucceeds()
    {
        var device = new Mock<ILuxaforDevice>().Object;

        var result = DeviceOpenResult.Opened(device, Descriptor);

        Assert.True(result.IsSuccess);
        Assert.Equal(DeviceOpenStatus.Opened, result.Status);
        Assert.Same(device, result.Device);
        Assert.Equal(Descriptor, result.Descriptor);
        Assert.Null(result.Error);
    }

    [Fact]
    public void NotFound_HasNoDeviceAndNoError()
    {
        var result = DeviceOpenResult.NotFound();

        Assert.False(result.IsSuccess);
        Assert.Equal(DeviceOpenStatus.NotFound, result.Status);
        Assert.Null(result.Device);
        Assert.Null(result.Descriptor);
        Assert.Null(result.Error);
    }

    [Fact]
    public void NotFound_ByPath_NamesThePath()
    {
        var result = DeviceOpenResult.NotFound("/dev/hidraw9");

        Assert.Equal(DeviceOpenStatus.NotFound, result.Status);
        Assert.Contains("/dev/hidraw9", result.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void NotMatched_NamesTheAttachedDevices()
    {
        var result = DeviceOpenResult.NotMatched("serial number '9999'", new[] { Descriptor });

        Assert.Equal(DeviceOpenStatus.NotFound, result.Status);
        Assert.Contains("9999", result.Description, StringComparison.Ordinal);
        Assert.Contains("1001", result.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Failure_KeepsTheReason()
    {
        var error = new UnauthorizedAccessException("Permission denied.");

        var result = DeviceOpenResult.Failure(DeviceOpenStatus.AccessDenied, Descriptor, error);

        Assert.False(result.IsSuccess);
        Assert.Equal(DeviceOpenStatus.AccessDenied, result.Status);
        Assert.Null(result.Device);
        Assert.Equal(Descriptor, result.Descriptor);
        Assert.Same(error, result.Error);
    }
}
