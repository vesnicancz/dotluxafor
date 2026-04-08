using DotLuxafor;
using Moq;
using HidSharp;

namespace DotLuxafor.Tests;

public class LuxaforDeviceManagerTests
{
    private readonly Mock<IHidDeviceListProvider> _provider = new();

    private LuxaforDeviceManager CreateManager() => new LuxaforDeviceManager(_provider.Object);

    [Fact]
    public void TryOpen_NoDevices_ReturnsNull()
    {
        _provider.Setup(p => p.GetDevices(LuxaforDevice.VendorId, LuxaforDevice.ProductId))
            .Returns(Enumerable.Empty<HidDevice>());

        var result = CreateManager().TryOpen();

        Assert.Null(result);
    }

    [Fact]
    public void OpenAll_NoDevices_ReturnsEmptyList()
    {
        _provider.Setup(p => p.GetDevices(LuxaforDevice.VendorId, LuxaforDevice.ProductId))
            .Returns(Enumerable.Empty<HidDevice>());

        var result = CreateManager().OpenAll();

        Assert.Empty(result);
    }

    [Fact]
    public void IsDevicePresent_NoDevices_ReturnsFalse()
    {
        _provider.Setup(p => p.GetDevices(LuxaforDevice.VendorId, LuxaforDevice.ProductId))
            .Returns(Enumerable.Empty<HidDevice>());

        Assert.False(CreateManager().IsDevicePresent());
    }

    [Fact]
    public void TryOpen_QueriesCorrectVendorAndProductId()
    {
        _provider.Setup(p => p.GetDevices(It.IsAny<int>(), It.IsAny<int>()))
            .Returns(Enumerable.Empty<HidDevice>());

        CreateManager().TryOpen();

        _provider.Verify(p => p.GetDevices(0x04D8, 0xF372), Times.Once);
    }

    [Fact]
    public void OpenAll_QueriesCorrectVendorAndProductId()
    {
        _provider.Setup(p => p.GetDevices(It.IsAny<int>(), It.IsAny<int>()))
            .Returns(Enumerable.Empty<HidDevice>());

        CreateManager().OpenAll();

        _provider.Verify(p => p.GetDevices(0x04D8, 0xF372), Times.Once);
    }

    [Fact]
    public void IsDevicePresent_QueriesCorrectVendorAndProductId()
    {
        _provider.Setup(p => p.GetDevices(It.IsAny<int>(), It.IsAny<int>()))
            .Returns(Enumerable.Empty<HidDevice>());

        CreateManager().IsDevicePresent();

        _provider.Verify(p => p.GetDevices(0x04D8, 0xF372), Times.Once);
    }

    [Fact]
    public void DefaultConstructor_DoesNotThrow()
    {
        // Verifies the parameterless constructor wires up the real HidDeviceListProvider
        var manager = new LuxaforDeviceManager();
        Assert.NotNull(manager);
    }
}
