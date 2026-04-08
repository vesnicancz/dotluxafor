using DotLuxafor;

namespace DotLuxafor.Tests;

public class ParseReportTests
{
	[Fact]
	public void ParseReport_DongleStatus_ReturnsCorrectEvent()
	{
		// Report: [0x00, 0x41(dongle), 0x01(present), 0xE0(RSSI=-32), 0x00, 0x00, 0x4B(75%), 0x01(charging), 0x00]
		byte[] buffer = [0x00, 0x41, 0x01, 0xE0, 0x00, 0x00, 0x4B, 0x01, 0x00];

		var evt = LuxaforDevice.ParseReport(buffer);

		var dongle = Assert.IsType<LuxaforEvent.DongleDataReceived>(evt);
		Assert.True(dongle.Info.IsDevicePresent);
		Assert.Equal(-32, dongle.Info.Rssi);
		Assert.Equal(75, dongle.Info.BatteryLevel);
		Assert.Equal(BatteryStatus.Charging, dongle.Info.BatteryStatus);
	}

	[Fact]
	public void ParseReport_DongleStatus_DeviceNotPresent()
	{
		byte[] buffer = [0x00, 0x41, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

		var evt = LuxaforDevice.ParseReport(buffer);

		var dongle = Assert.IsType<LuxaforEvent.DongleDataReceived>(evt);
		Assert.False(dongle.Info.IsDevicePresent);
	}

	[Theory]
	[InlineData(0x00, BatteryStatus.NotConnected)]
	[InlineData(0x01, BatteryStatus.Charging)]
	[InlineData(0x02, BatteryStatus.Full)]
	[InlineData(0x03, BatteryStatus.NotConnected)] // unknown falls to default
	public void ParseReport_DongleStatus_BatteryStatusMapping(byte statusByte, BatteryStatus expected)
	{
		byte[] buffer = [0x00, 0x41, 0x01, 0x00, 0x00, 0x00, 0x00, statusByte, 0x00];

		var evt = LuxaforDevice.ParseReport(buffer);

		var dongle = Assert.IsType<LuxaforEvent.DongleDataReceived>(evt);
		Assert.Equal(expected, dongle.Info.BatteryStatus);
	}

	[Fact]
	public void ParseReport_DongleStatus_NegativeRssi()
	{
		// RSSI = -80 → 0xB0 as signed byte
		byte[] buffer = [0x00, 0x41, 0x01, 0xB0, 0x00, 0x00, 0x00, 0x00, 0x00];

		var evt = LuxaforDevice.ParseReport(buffer);

		var dongle = Assert.IsType<LuxaforEvent.DongleDataReceived>(evt);
		Assert.Equal(-80, dongle.Info.Rssi);
	}

	[Fact]
	public void ParseReport_MuteButtonPressed()
	{
		byte[] buffer = [0x00, 0x83, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

		var evt = LuxaforDevice.ParseReport(buffer);

		var mute = Assert.IsType<LuxaforEvent.MuteButtonStateChanged>(evt);
		Assert.True(mute.IsPressed);
	}

	[Fact]
	public void ParseReport_MuteButtonReleased()
	{
		byte[] buffer = [0x00, 0x83, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

		var evt = LuxaforDevice.ParseReport(buffer);

		var mute = Assert.IsType<LuxaforEvent.MuteButtonStateChanged>(evt);
		Assert.False(mute.IsPressed);
	}

	[Fact]
	public void ParseReport_DeviceIdentified_StandardDevice()
	{
		// Type=0x01 (Standard), Serial: buffer[3]=0x01, buffer[4]=0x02 → serial = 0x0102 = 258
		byte[] buffer = [0x00, 0x80, 0x01, 0x01, 0x02, 0x00, 0x00, 0x00, 0x00];

		var evt = LuxaforDevice.ParseReport(buffer);

		var identified = Assert.IsType<LuxaforEvent.DeviceIdentified>(evt);
		Assert.Equal(DeviceType.Standard, identified.Info.Type);
		Assert.Equal(258L, identified.Info.SerialNumber);
	}

	[Fact]
	public void ParseReport_DeviceIdentified_BluetoothDevice()
	{
		// Type=15 (Bluetooth, 10..29 range), Serial from 6 bytes: buffer[3..8]
		byte[] buffer = [0x00, 0x80, 15, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06];

		var evt = LuxaforDevice.ParseReport(buffer);

		var identified = Assert.IsType<LuxaforEvent.DeviceIdentified>(evt);
		Assert.Equal(DeviceType.Bluetooth, identified.Info.Type);
		long expected = ((long)0x01 << 40) | ((long)0x02 << 32) | ((long)0x03 << 24) |
		                ((long)0x04 << 16) | ((long)0x05 << 8) | 0x06;
		Assert.Equal(expected, identified.Info.SerialNumber);
	}

	[Fact]
	public void ParseReport_DeviceIdentified_MuteButtonDevice()
	{
		// Type=30 (MuteButton), 6-byte serial
		byte[] buffer = [0x00, 0x80, 30, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF];

		var evt = LuxaforDevice.ParseReport(buffer);

		var identified = Assert.IsType<LuxaforEvent.DeviceIdentified>(evt);
		Assert.Equal(DeviceType.MuteButton, identified.Info.Type);
		long expected = ((long)0xAA << 40) | ((long)0xBB << 32) | ((long)0xCC << 24) |
		                ((long)0xDD << 16) | ((long)0xEE << 8) | 0xFF;
		Assert.Equal(expected, identified.Info.SerialNumber);
	}

	[Fact]
	public void ParseReport_PatternCompleted_Variant1()
	{
		byte[] buffer = [0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

		var evt = LuxaforDevice.ParseReport(buffer);

		Assert.IsType<LuxaforEvent.PatternCompleted>(evt);
	}

	[Fact]
	public void ParseReport_PatternCompleted_Variant2()
	{
		byte[] buffer = [0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

		var evt = LuxaforDevice.ParseReport(buffer);

		Assert.IsType<LuxaforEvent.PatternCompleted>(evt);
	}

	[Fact]
	public void ParseReport_UnknownReport_ReturnsNull()
	{
		byte[] buffer = [0x00, 0x99, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

		var evt = LuxaforDevice.ParseReport(buffer);

		Assert.Null(evt);
	}

	[Fact]
	public void ParseReport_AllZeros_ReturnsNull()
	{
		byte[] buffer = [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

		var evt = LuxaforDevice.ParseReport(buffer);

		Assert.Null(evt);
	}
}
