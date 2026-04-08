using DotLuxafor;

namespace DotLuxafor.Tests;

public class ExtractSerialNumberTests
{
	[Fact]
	public void StandardDevice_Uses2ByteSerial()
	{
		byte[] buffer = [0x00, 0x80, 0x01, 0xAB, 0xCD, 0x00, 0x00, 0x00, 0x00];

		var serial = LuxaforDevice.ExtractSerialNumber(buffer, DeviceType.Standard);

		Assert.Equal(0xABCDL, serial);
	}

	[Fact]
	public void StandardDevice_ZeroSerial()
	{
		byte[] buffer = [0x00, 0x80, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

		var serial = LuxaforDevice.ExtractSerialNumber(buffer, DeviceType.Standard);

		Assert.Equal(0L, serial);
	}

	[Fact]
	public void StandardDevice_MaxSerial()
	{
		byte[] buffer = [0x00, 0x80, 0x01, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00];

		var serial = LuxaforDevice.ExtractSerialNumber(buffer, DeviceType.Standard);

		Assert.Equal(0xFFFFL, serial);
	}

	[Fact]
	public void BluetoothDevice_Uses6ByteSerial()
	{
		byte[] buffer = [0x00, 0x80, 15, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06];

		var serial = LuxaforDevice.ExtractSerialNumber(buffer, DeviceType.Bluetooth);

		long expected = ((long)0x01 << 40) | ((long)0x02 << 32) | ((long)0x03 << 24) |
		                ((long)0x04 << 16) | ((long)0x05 << 8) | 0x06;
		Assert.Equal(expected, serial);
	}

	[Fact]
	public void MuteButtonDevice_Uses6ByteSerial()
	{
		byte[] buffer = [0x00, 0x80, 30, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF];

		var serial = LuxaforDevice.ExtractSerialNumber(buffer, DeviceType.MuteButton);

		long expected = ((long)0xAA << 40) | ((long)0xBB << 32) | ((long)0xCC << 24) |
		                ((long)0xDD << 16) | ((long)0xEE << 8) | 0xFF;
		Assert.Equal(expected, serial);
	}

	[Fact]
	public void SmartButtonDevice_Uses6ByteSerial()
	{
		byte[] buffer = [0x00, 0x80, 50, 0x10, 0x20, 0x30, 0x40, 0x50, 0x60];

		var serial = LuxaforDevice.ExtractSerialNumber(buffer, DeviceType.SmartButton);

		long expected = ((long)0x10 << 40) | ((long)0x20 << 32) | ((long)0x30 << 24) |
		                ((long)0x40 << 16) | ((long)0x50 << 8) | 0x60;
		Assert.Equal(expected, serial);
	}

	[Fact]
	public void ColorblindDevice_Uses2ByteSerial()
	{
		byte[] buffer = [0x00, 0x80, 4, 0x12, 0x34, 0x00, 0x00, 0x00, 0x00];

		var serial = LuxaforDevice.ExtractSerialNumber(buffer, DeviceType.Colorblind);

		Assert.Equal(0x1234L, serial);
	}

	[Fact]
	public void SixByteSerial_AllZeros()
	{
		byte[] buffer = [0x00, 0x80, 15, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];

		var serial = LuxaforDevice.ExtractSerialNumber(buffer, DeviceType.Bluetooth);

		Assert.Equal(0L, serial);
	}

	[Fact]
	public void SixByteSerial_AllMax()
	{
		byte[] buffer = [0x00, 0x80, 15, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];

		var serial = LuxaforDevice.ExtractSerialNumber(buffer, DeviceType.Bluetooth);

		long expected = ((long)0xFF << 40) | ((long)0xFF << 32) | ((long)0xFF << 24) |
		                ((long)0xFF << 16) | ((long)0xFF << 8) | 0xFF;
		Assert.Equal(expected, serial);
	}
}
