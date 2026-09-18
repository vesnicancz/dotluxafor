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
    public void Open_NoDevices_ReturnsNotFoundWithoutError()
    {
        _provider.Setup(p => p.GetDevices(LuxaforDevice.VendorId, LuxaforDevice.ProductId))
            .Returns(Enumerable.Empty<HidDevice>());

        var result = CreateManager().Open();

        Assert.Equal(DeviceOpenStatus.NotFound, result.Status);
        Assert.False(result.IsSuccess);
        Assert.Null(result.Device);
        Assert.Null(result.Error);
        Assert.Contains("No Luxafor device found", result.Description, StringComparison.Ordinal);
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

    #region OpenAllResults

    [Fact]
    public void OpenAllResults_NoDevices_ReturnsEmptyList()
    {
        _provider.Setup(p => p.GetDevices(LuxaforDevice.VendorId, LuxaforDevice.ProductId))
            .Returns(Enumerable.Empty<HidDevice>());

        Assert.Empty(CreateManager().OpenAllResults());
    }

    [Fact]
    public void OpenAllResults_ReportsOneOutcomePerAttachedDevice()
    {
        // TryOpen on a bare mock fails, which is the case that OpenAll used to swallow entirely.
        _provider.Setup(p => p.GetDevices(LuxaforDevice.VendorId, LuxaforDevice.ProductId))
            .Returns(new[] { new Mock<HidDevice>().Object, new Mock<HidDevice>().Object });

        var results = CreateManager().OpenAllResults();

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.False(r.IsSuccess));
        Assert.All(results, r => Assert.NotEqual(DeviceOpenStatus.NotFound, r.Status));
        Assert.All(results, r => Assert.False(string.IsNullOrWhiteSpace(r.Description)));
    }

    [Fact]
    public void OpenAll_AndOpenAllResults_AgreeOnWhatOpened()
    {
        _provider.Setup(p => p.GetDevices(LuxaforDevice.VendorId, LuxaforDevice.ProductId))
            .Returns(() => new[] { new Mock<HidDevice>().Object });

        var manager = CreateManager();

        Assert.Equal(
            manager.OpenAllResults().Count(r => r.Device != null),
            manager.OpenAll().Count);
    }

    [Fact]
    public void OpenAllResults_QueriesCorrectVendorAndProductId()
    {
        _provider.Setup(p => p.GetDevices(It.IsAny<int>(), It.IsAny<int>()))
            .Returns(Enumerable.Empty<HidDevice>());

        CreateManager().OpenAllResults();

        _provider.Verify(p => p.GetDevices(0x04D8, 0xF372), Times.Once);
    }

    #endregion

    #region WaitForDeviceAsync

    private bool _devicePresent;
    private Action? _notifyChanged;
    private readonly Mock<IDisposable> _subscription = new();

    /// <summary>
    /// Builds a manager whose device presence follows <see cref="_devicePresent"/> and whose
    /// change notification is fired by calling <see cref="_notifyChanged"/>.
    /// </summary>
    private LuxaforDeviceManager CreateHotplugManager()
    {
        _provider.Setup(p => p.GetDevices(LuxaforDevice.VendorId, LuxaforDevice.ProductId))
            .Returns(() => _devicePresent
                ? new[] { new Mock<HidDevice>().Object }
                : Enumerable.Empty<HidDevice>());

        _provider.Setup(p => p.SubscribeToChanges(It.IsAny<Action>()))
            .Returns((Action handler) =>
            {
                _notifyChanged = handler;
                return _subscription.Object;
            });

        return new LuxaforDeviceManager(_provider.Object);
    }

    [Fact]
    public async Task WaitForDeviceAsync_WhenDeviceAlreadyPresent_CompletesWithoutSubscribing()
    {
        var ct = TestContext.Current.CancellationToken;
        _devicePresent = true;

        await CreateHotplugManager().WaitForDeviceAsync(ct);

        _provider.Verify(p => p.SubscribeToChanges(It.IsAny<Action>()), Times.Never);
    }

    [Fact]
    public async Task WaitForDeviceAsync_CompletesWhenADeviceArrives()
    {
        var ct = TestContext.Current.CancellationToken;
        var waiting = CreateHotplugManager().WaitForDeviceAsync(ct);

        Assert.False(waiting.IsCompleted);

        _devicePresent = true;
        _notifyChanged!();

        await waiting.WaitAsync(TimeSpan.FromSeconds(10), ct);
        _subscription.Verify(d => d.Dispose(), Times.Once);
    }

    [Fact]
    public async Task WaitForDeviceAsync_IgnoresChangesThatBringNoLuxafor()
    {
        var ct = TestContext.Current.CancellationToken;
        var waiting = CreateHotplugManager().WaitForDeviceAsync(ct);

        // The notification fires for every HID device on the machine, not just ours.
        _notifyChanged!();
        _notifyChanged!();

        Assert.False(waiting.IsCompleted);

        _devicePresent = true;
        _notifyChanged!();

        await waiting.WaitAsync(TimeSpan.FromSeconds(10), ct);
    }

    [Fact]
    public async Task WaitForDeviceAsync_WhenCancelled_ThrowsAndUnsubscribes()
    {
        var ct = TestContext.Current.CancellationToken;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var waiting = CreateHotplugManager().WaitForDeviceAsync(cts.Token);

        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        _subscription.Verify(d => d.Dispose(), Times.Once);
    }

    [Fact]
    public async Task WaitForDeviceAsync_WhenAlreadyCancelled_ThrowsWithoutQuerying()
    {
        var manager = CreateHotplugManager();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.WaitForDeviceAsync(cts.Token));

        _provider.Verify(p => p.GetDevices(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    #endregion

    [Fact]
    public void DefaultConstructor_DoesNotThrow()
    {
        // Verifies the parameterless constructor wires up the real HidDeviceListProvider
        var manager = new LuxaforDeviceManager();
        Assert.NotNull(manager);
    }
}
