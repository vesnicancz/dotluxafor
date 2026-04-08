using DotLuxafor;

namespace DotLuxafor.Tests;

public class ClassifyDeviceTests
{
	[Theory]
	[InlineData(0, DeviceType.Standard)]
	[InlineData(1, DeviceType.Standard)]
	[InlineData(3, DeviceType.Standard)]
	[InlineData(5, DeviceType.Standard)]
	[InlineData(9, DeviceType.Standard)]
	public void ClassifyDevice_StandardRange(byte typeByte, DeviceType expected)
	{
		Assert.Equal(expected, LuxaforDevice.ClassifyDevice(typeByte));
	}

	[Fact]
	public void ClassifyDevice_Colorblind()
	{
		Assert.Equal(DeviceType.Colorblind, LuxaforDevice.ClassifyDevice(4));
	}

	[Theory]
	[InlineData(10)]
	[InlineData(15)]
	[InlineData(20)]
	[InlineData(29)]
	public void ClassifyDevice_BluetoothRange(byte typeByte)
	{
		Assert.Equal(DeviceType.Bluetooth, LuxaforDevice.ClassifyDevice(typeByte));
	}

	[Fact]
	public void ClassifyDevice_MuteButton()
	{
		Assert.Equal(DeviceType.MuteButton, LuxaforDevice.ClassifyDevice(30));
	}

	[Fact]
	public void ClassifyDevice_SmartButton()
	{
		Assert.Equal(DeviceType.SmartButton, LuxaforDevice.ClassifyDevice(50));
	}

	[Theory]
	[InlineData(31)]
	[InlineData(49)]
	[InlineData(51)]
	[InlineData(100)]
	[InlineData(255)]
	public void ClassifyDevice_OutOfKnownRange_ReturnsStandard(byte typeByte)
	{
		Assert.Equal(DeviceType.Standard, LuxaforDevice.ClassifyDevice(typeByte));
	}
}
