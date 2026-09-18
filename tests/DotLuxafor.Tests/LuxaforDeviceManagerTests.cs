using DotLuxafor;
using Moq;

namespace DotLuxafor.Tests;

public class LuxaforDeviceManagerTests
{
    private readonly Mock<IHidDeviceListProvider> _provider = new();

    private LuxaforDeviceManager CreateManager() => new LuxaforDeviceManager(_provider.Object);

    [Fact]
    public void TryOpen_NoDevices_ReturnsNull()
    {
        _provider.Setup(p => p.GetDevices(LuxaforDevice.VendorId, LuxaforDevice.ProductId))
            .Returns(Enumerable.Empty<IHidDeviceHandle>());

        var result = CreateManager().TryOpen();

        Assert.Null(result);
    }

    [Fact]
    public void Open_NoDevices_ReturnsNotFoundWithoutError()
    {
        _provider.Setup(p => p.GetDevices(LuxaforDevice.VendorId, LuxaforDevice.ProductId))
            .Returns(Enumerable.Empty<IHidDeviceHandle>());

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
            .Returns(Enumerable.Empty<IHidDeviceHandle>());

        var result = CreateManager().OpenAll();

        Assert.Empty(result);
    }

    [Fact]
    public void IsDevicePresent_NoDevices_ReturnsFalse()
    {
        _provider.Setup(p => p.GetDevices(LuxaforDevice.VendorId, LuxaforDevice.ProductId))
            .Returns(Enumerable.Empty<IHidDeviceHandle>());

        Assert.False(CreateManager().IsDevicePresent());
    }

    [Fact]
    public void TryOpen_QueriesCorrectVendorAndProductId()
    {
        _provider.Setup(p => p.GetDevices(It.IsAny<int>(), It.IsAny<int>()))
            .Returns(Enumerable.Empty<IHidDeviceHandle>());

        CreateManager().TryOpen();

        _provider.Verify(p => p.GetDevices(0x04D8, 0xF372), Times.Once);
    }

    [Fact]
    public void OpenAll_QueriesCorrectVendorAndProductId()
    {
        _provider.Setup(p => p.GetDevices(It.IsAny<int>(), It.IsAny<int>()))
            .Returns(Enumerable.Empty<IHidDeviceHandle>());

        CreateManager().OpenAll();

        _provider.Verify(p => p.GetDevices(0x04D8, 0xF372), Times.Once);
    }

    [Fact]
    public void IsDevicePresent_QueriesCorrectVendorAndProductId()
    {
        _provider.Setup(p => p.GetDevices(It.IsAny<int>(), It.IsAny<int>()))
            .Returns(Enumerable.Empty<IHidDeviceHandle>());

        CreateManager().IsDevicePresent();

        _provider.Verify(p => p.GetDevices(0x04D8, 0xF372), Times.Once);
    }

    #region OpenAllResults

    [Fact]
    public void OpenAllResults_NoDevices_ReturnsEmptyList()
    {
        _provider.Setup(p => p.GetDevices(LuxaforDevice.VendorId, LuxaforDevice.ProductId))
            .Returns(Enumerable.Empty<IHidDeviceHandle>());

        Assert.Empty(CreateManager().OpenAllResults());
    }

    [Fact]
    public void OpenAllResults_ReportsOneOutcomePerAttachedDevice()
    {
        // A device that is attached but will not open is the case that OpenAll swallows entirely.
        _provider.Setup(p => p.GetDevices(LuxaforDevice.VendorId, LuxaforDevice.ProductId))
            .Returns(new IHidDeviceHandle[] { FakeHidDeviceHandle.Blocked("/dev/hidraw0"), FakeHidDeviceHandle.Blocked("/dev/hidraw1") });

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
            .Returns(() => new IHidDeviceHandle[] { FakeHidDeviceHandle.Openable("/dev/hidraw0"), FakeHidDeviceHandle.Blocked("/dev/hidraw1") });

        var manager = CreateManager();

        Assert.Equal(
            manager.OpenAllResults().Count(r => r.Device != null),
            manager.OpenAll().Count);
    }

    [Fact]
    public void OpenAllResults_QueriesCorrectVendorAndProductId()
    {
        _provider.Setup(p => p.GetDevices(It.IsAny<int>(), It.IsAny<int>()))
            .Returns(Enumerable.Empty<IHidDeviceHandle>());

        CreateManager().OpenAllResults();

        _provider.Verify(p => p.GetDevices(0x04D8, 0xF372), Times.Once);
    }

    #endregion

    #region List and open by path

    private void Attach(params IHidDeviceHandle[] handles)
    {
        _provider.Setup(p => p.GetDevices(LuxaforDevice.VendorId, LuxaforDevice.ProductId))
            .Returns(handles);
    }

    [Fact]
    public void List_NoDevices_ReturnsEmptyList()
    {
        Attach();

        Assert.Empty(CreateManager().List());
    }

    [Fact]
    public void List_ReturnsOneDescriptorPerAttachedDevice_InDiscoveryOrder()
    {
        Attach(
            FakeHidDeviceHandle.Openable("/dev/hidraw0", "LUXAFOR FLAG", "1001"),
            FakeHidDeviceHandle.Openable("/dev/hidraw1", "LUXAFOR MUTE", "1002"));

        var descriptors = CreateManager().List();

        Assert.Equal(2, descriptors.Count);
        Assert.Equal("/dev/hidraw0", descriptors[0].DevicePath);
        Assert.Equal("LUXAFOR FLAG", descriptors[0].ProductName);
        Assert.Equal("1001", descriptors[0].SerialNumber);
        Assert.Equal("/dev/hidraw1", descriptors[1].DevicePath);
    }

    [Fact]
    public void List_IncludesDevicesThatWillNotOpen()
    {
        // Listing needs no permission to open, so a device blocked by the OS still has to show up —
        // otherwise a picker would silently hide the very device the user is trying to diagnose.
        Attach(FakeHidDeviceHandle.Blocked("/dev/hidraw0"));

        var descriptor = Assert.Single(CreateManager().List());

        Assert.Equal("/dev/hidraw0", descriptor.DevicePath);
    }

    [Fact]
    public void Open_ByPath_OpensTheMatchingDevice()
    {
        Attach(
            FakeHidDeviceHandle.Openable("/dev/hidraw0"),
            FakeHidDeviceHandle.Openable("/dev/hidraw1"));

        using var result = CreateManager().Open("/dev/hidraw1").Device;

        Assert.NotNull(result);
        Assert.Equal("/dev/hidraw1", result.Descriptor?.DevicePath);
    }

    [Fact]
    public void Open_ByPath_PicksTheRequestedDeviceNotTheFirstOne()
    {
        Attach(
            FakeHidDeviceHandle.Blocked("/dev/hidraw0"),
            FakeHidDeviceHandle.Openable("/dev/hidraw1"));

        var result = CreateManager().Open("/dev/hidraw1");
        using var device = result.Device;

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Open_ByPath_UnknownPath_ReportsNotFoundAndNamesThePath()
    {
        Attach(FakeHidDeviceHandle.Openable("/dev/hidraw0"));

        var result = CreateManager().Open("/dev/hidraw9");

        Assert.Equal(DeviceOpenStatus.NotFound, result.Status);
        Assert.Null(result.Device);
        Assert.Contains("/dev/hidraw9", result.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Open_ByPath_BlockedDevice_ReportsWhy()
    {
        Attach(FakeHidDeviceHandle.Blocked("/dev/hidraw0", new UnauthorizedAccessException("nope")));

        var result = CreateManager().Open("/dev/hidraw0");

        Assert.Equal(DeviceOpenStatus.AccessDenied, result.Status);
        Assert.Equal("/dev/hidraw0", result.Descriptor?.DevicePath);
    }

    [Fact]
    public void Open_ByDescriptor_IsEquivalentToOpeningItsPath()
    {
        Attach(FakeHidDeviceHandle.Openable("/dev/hidraw0", "LUXAFOR FLAG", "1001"));
        var manager = CreateManager();

        var descriptor = Assert.Single(manager.List());
        using var device = manager.Open(descriptor).Device;

        Assert.NotNull(device);
        Assert.Equal(descriptor, device.Descriptor);
    }

    [Fact]
    public void Open_ByPath_Null_Throws()
    {
        Attach();

        Assert.Throws<ArgumentNullException>(() => CreateManager().Open((string)null!));
        Assert.Throws<ArgumentNullException>(() => CreateManager().Open((LuxaforDeviceDescriptor)null!));
    }

    [Fact]
    public void Open_Success_CarriesTheDescriptor()
    {
        Attach(FakeHidDeviceHandle.Openable("/dev/hidraw0", "LUXAFOR FLAG", "1001"));

        var result = CreateManager().Open();
        using var device = result.Device;

        Assert.Equal("/dev/hidraw0", result.Descriptor?.DevicePath);
        Assert.Equal(result.Descriptor, device?.Descriptor);
    }

    [Fact]
    public void Open_NothingAttached_HasNoDescriptor()
    {
        Attach();

        Assert.Null(CreateManager().Open().Descriptor);
    }

    [Fact]
    public void OpenAll_DevicesCanBeToldApartByDescriptor()
    {
        // The point of the descriptor: OpenAll used to hand back a list nobody could label.
        Attach(
            FakeHidDeviceHandle.Openable("/dev/hidraw0", "LUXAFOR FLAG", "1001"),
            FakeHidDeviceHandle.Openable("/dev/hidraw1", "LUXAFOR MUTE", "1002"));

        var devices = CreateManager().OpenAll();

        try
        {
            Assert.Equal(
                new[] { "/dev/hidraw0", "/dev/hidraw1" },
                devices.Select(d => d.Descriptor?.DevicePath));
        }
        finally
        {
            foreach (var device in devices)
            {
                device.Dispose();
            }
        }
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
                ? new IHidDeviceHandle[] { FakeHidDeviceHandle.Openable("/dev/hidraw0") }
                : Enumerable.Empty<IHidDeviceHandle>());

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

    #region IsPresent

    [Fact]
    public void IsPresent_DeviceStillAttached_ReturnsTrue()
    {
        Attach(FakeHidDeviceHandle.Openable("/dev/hidraw0"), FakeHidDeviceHandle.Openable("/dev/hidraw1"));

        Assert.True(CreateManager().IsPresent(new LuxaforDeviceDescriptor("/dev/hidraw1", null, null)));
    }

    [Fact]
    public void IsPresent_DeviceUnplugged_ReturnsFalse()
    {
        Attach(FakeHidDeviceHandle.Openable("/dev/hidraw0"));

        // The whole point of the method: the caller is holding a handle to hidraw1 that still looks
        // fine to it, and only the enumeration can say the device behind it is gone.
        Assert.False(CreateManager().IsPresent(new LuxaforDeviceDescriptor("/dev/hidraw1", null, null)));
    }

    [Fact]
    public void IsPresent_MatchesOnPathAlone()
    {
        // The name and serial come from USB string descriptors the platform may refuse to hand
        // over, so the same device can enumerate with them null; comparing whole descriptors would
        // then report a device that is plainly still there as gone.
        Attach(FakeHidDeviceHandle.Openable("/dev/hidraw0"));

        Assert.True(CreateManager().IsPresent(new LuxaforDeviceDescriptor("/dev/hidraw0", "LUXAFOR FLAG", "1001")));
    }

    [Fact]
    public void IsPresent_DeviceThatWillNotOpen_ReturnsTrue()
    {
        // Attached but blocked by the OS is still attached — reopening is what fails, not presence.
        Attach(FakeHidDeviceHandle.Blocked("/dev/hidraw0"));

        Assert.True(CreateManager().IsPresent(new LuxaforDeviceDescriptor("/dev/hidraw0", null, null)));
    }

    [Fact]
    public void IsPresent_NullDescriptor_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => CreateManager().IsPresent(null!));
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
