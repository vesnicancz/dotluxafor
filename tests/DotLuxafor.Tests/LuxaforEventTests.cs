using DotLuxafor;

namespace DotLuxafor.Tests;

public class LuxaforEventTests
{
	[Fact]
	public void DeviceIdentified_RecordEquality()
	{
		var info = new DeviceInfo(DeviceType.Standard, 12345);
		var a = new LuxaforEvent.DeviceIdentified(info);
		var b = new LuxaforEvent.DeviceIdentified(info);

		Assert.Equal(a, b);
	}

	[Fact]
	public void DeviceIdentified_DifferentInfo_NotEqual()
	{
		var a = new LuxaforEvent.DeviceIdentified(new DeviceInfo(DeviceType.Standard, 1));
		var b = new LuxaforEvent.DeviceIdentified(new DeviceInfo(DeviceType.Standard, 2));

		Assert.NotEqual(a, b);
	}

	[Fact]
	public void DongleDataReceived_StoresInfo()
	{
		var info = new DongleInfo(true, -50, 80, BatteryStatus.Charging);
		var evt = new LuxaforEvent.DongleDataReceived(info);

		Assert.True(evt.Info.IsDevicePresent);
		Assert.Equal(-50, evt.Info.Rssi);
		Assert.Equal(80, evt.Info.BatteryLevel);
		Assert.Equal(BatteryStatus.Charging, evt.Info.BatteryStatus);
	}

	[Fact]
	public void MuteButtonStateChanged_StoresIsPressed()
	{
		var pressed = new LuxaforEvent.MuteButtonStateChanged(true);
		var released = new LuxaforEvent.MuteButtonStateChanged(false);

		Assert.True(pressed.IsPressed);
		Assert.False(released.IsPressed);
	}

	[Fact]
	public void PatternCompleted_RecordEquality()
	{
		var a = new LuxaforEvent.PatternCompleted();
		var b = new LuxaforEvent.PatternCompleted();

		Assert.Equal(a, b);
	}

	[Fact]
	public void Disconnected_RecordEquality()
	{
		var a = new LuxaforEvent.Disconnected();
		var b = new LuxaforEvent.Disconnected();

		Assert.Equal(a, b);
	}

	[Fact]
	public void ReadError_StoresException()
	{
		var ex = new InvalidOperationException("test error");
		var evt = new LuxaforEvent.ReadError(ex);

		Assert.Same(ex, evt.Exception);
	}

	[Fact]
	public void PatternMatching_WorksCorrectly()
	{
		LuxaforEvent evt = new LuxaforEvent.MuteButtonStateChanged(true);

		var result = evt switch
		{
			LuxaforEvent.DeviceIdentified => "identified",
			LuxaforEvent.DongleDataReceived => "dongle",
			LuxaforEvent.MuteButtonStateChanged { IsPressed: true } => "mute_pressed",
			LuxaforEvent.MuteButtonStateChanged { IsPressed: false } => "mute_released",
			LuxaforEvent.PatternCompleted => "pattern",
			LuxaforEvent.Disconnected => "disconnected",
			LuxaforEvent.ReadError => "error",
			_ => "unknown"
		};

		Assert.Equal("mute_pressed", result);
	}

	[Fact]
	public void DifferentEventTypes_AreNotEqual()
	{
		LuxaforEvent a = new LuxaforEvent.PatternCompleted();
		LuxaforEvent b = new LuxaforEvent.Disconnected();

		Assert.NotEqual(a, b);
	}
}
