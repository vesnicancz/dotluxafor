using DotLuxafor;

namespace DotLuxafor.Tests;

public class LuxaforColorTests
{
	[Fact]
	public void Constructor_SetsRgbValues()
	{
		var color = new LuxaforColor(10, 20, 30);

		Assert.Equal(10, color.R);
		Assert.Equal(20, color.G);
		Assert.Equal(30, color.B);
	}

	[Fact]
	public void PredefinedColors_HaveCorrectValues()
	{
		Assert.Equal(new LuxaforColor(255, 0, 0), LuxaforColor.Red);
		Assert.Equal(new LuxaforColor(0, 255, 0), LuxaforColor.Green);
		Assert.Equal(new LuxaforColor(0, 0, 255), LuxaforColor.Blue);
		Assert.Equal(new LuxaforColor(255, 255, 0), LuxaforColor.Yellow);
		Assert.Equal(new LuxaforColor(0, 255, 255), LuxaforColor.Cyan);
		Assert.Equal(new LuxaforColor(255, 0, 255), LuxaforColor.Magenta);
		Assert.Equal(new LuxaforColor(255, 255, 255), LuxaforColor.White);
		Assert.Equal(new LuxaforColor(0, 0, 0), LuxaforColor.Off);
	}

	[Theory]
	[InlineData("#FF0000", 255, 0, 0)]
	[InlineData("#00FF00", 0, 255, 0)]
	[InlineData("#0000FF", 0, 0, 255)]
	[InlineData("#ABCDEF", 0xAB, 0xCD, 0xEF)]
	[InlineData("#000000", 0, 0, 0)]
	[InlineData("#FFFFFF", 255, 255, 255)]
	public void FromHex_ValidInput_ReturnsCorrectColor(string hex, byte r, byte g, byte b)
	{
		var color = LuxaforColor.FromHex(hex);

		Assert.Equal(r, color.R);
		Assert.Equal(g, color.G);
		Assert.Equal(b, color.B);
	}

	[Theory]
	[InlineData("")]
	[InlineData("FF0000")]
	[InlineData("#FF00")]
	[InlineData("#FF00000")]
	[InlineData("#GGGGGG")]
	[InlineData("invalid")]
	public void FromHex_InvalidInput_ThrowsFormatException(string hex)
	{
		Assert.Throws<FormatException>(() => LuxaforColor.FromHex(hex));
	}

	[Fact]
	public void FromHex_Null_ThrowsFormatException()
	{
		Assert.Throws<FormatException>(() => LuxaforColor.FromHex(null!));
	}

	[Theory]
	[InlineData("#FF0000", true, 255, 0, 0)]
	[InlineData("#00FF00", true, 0, 255, 0)]
	[InlineData("", false, 0, 0, 0)]
	[InlineData("invalid", false, 0, 0, 0)]
	[InlineData("#GG0000", false, 0, 0, 0)]
	public void TryFromHex_ReturnsExpectedResult(string? hex, bool expectedSuccess, byte r, byte g, byte b)
	{
		var success = LuxaforColor.TryFromHex(hex, out var color);

		Assert.Equal(expectedSuccess, success);
		if (expectedSuccess)
		{
			Assert.Equal(r, color.R);
			Assert.Equal(g, color.G);
			Assert.Equal(b, color.B);
		}
	}

	[Fact]
	public void TryFromHex_Null_ReturnsFalse()
	{
		var success = LuxaforColor.TryFromHex(null, out _);

		Assert.False(success);
	}

	[Theory]
	[InlineData(255, 0, 0, "#FF0000")]
	[InlineData(0, 255, 0, "#00FF00")]
	[InlineData(0, 0, 255, "#0000FF")]
	[InlineData(0xAB, 0xCD, 0xEF, "#ABCDEF")]
	public void ToHex_ReturnsCorrectString(byte r, byte g, byte b, string expected)
	{
		var color = new LuxaforColor(r, g, b);

		Assert.Equal(expected, color.ToHex());
	}

	[Fact]
	public void ToString_ReturnsSameAsToHex()
	{
		var color = new LuxaforColor(128, 64, 32);

		Assert.Equal(color.ToHex(), color.ToString());
	}

	[Fact]
	public void RecordEquality_SameValues_AreEqual()
	{
		var a = new LuxaforColor(10, 20, 30);
		var b = new LuxaforColor(10, 20, 30);

		Assert.Equal(a, b);
		Assert.True(a == b);
	}

	[Fact]
	public void RecordEquality_DifferentValues_AreNotEqual()
	{
		var a = new LuxaforColor(10, 20, 30);
		var b = new LuxaforColor(10, 20, 31);

		Assert.NotEqual(a, b);
		Assert.True(a != b);
	}

	[Fact]
	public void FromHex_ToHex_Roundtrip()
	{
		var original = "#AB12EF";
		var color = LuxaforColor.FromHex(original);

		Assert.Equal(original, color.ToHex());
	}
}
