namespace DotLuxafor.Cli.Tests;

public class ColorParserTests
{
	[Theory]
	[InlineData("red", 255, 0, 0)]
	[InlineData("green", 0, 255, 0)]
	[InlineData("blue", 0, 0, 255)]
	[InlineData("yellow", 255, 255, 0)]
	[InlineData("cyan", 0, 255, 255)]
	[InlineData("magenta", 255, 0, 255)]
	[InlineData("white", 255, 255, 255)]
	[InlineData("off", 0, 0, 0)]
	public void Parse_NamedColor_ReturnsCorrectColor(string name, byte r, byte g, byte b)
	{
		LuxaforColor color = ColorParser.Parse(name);

		Assert.Equal(new LuxaforColor(r, g, b), color);
	}

	[Theory]
	[InlineData("Red")]
	[InlineData("RED")]
	[InlineData("rEd")]
	public void Parse_NamedColor_IsCaseInsensitive(string name)
	{
		Assert.Equal(LuxaforColor.Red, ColorParser.Parse(name));
	}

	[Theory]
	[InlineData("#FF0000", 255, 0, 0)]
	[InlineData("#00FF00", 0, 255, 0)]
	[InlineData("#0000FF", 0, 0, 255)]
	[InlineData("#ABCDEF", 0xAB, 0xCD, 0xEF)]
	public void Parse_HexColor_ReturnsCorrectColor(string hex, byte r, byte g, byte b)
	{
		LuxaforColor color = ColorParser.Parse(hex);

		Assert.Equal(new LuxaforColor(r, g, b), color);
	}

	[Theory]
	[InlineData("255,0,0", 255, 0, 0)]
	[InlineData("0,255,0", 0, 255, 0)]
	[InlineData("128,64,32", 128, 64, 32)]
	public void Parse_RgbColor_ReturnsCorrectColor(string rgb, byte r, byte g, byte b)
	{
		LuxaforColor color = ColorParser.Parse(rgb);

		Assert.Equal(new LuxaforColor(r, g, b), color);
	}

	[Theory]
	[InlineData("invalid")]
	[InlineData("")]
	[InlineData("pink")]
	public void Parse_InvalidColor_ThrowsFormatException(string input)
	{
		Assert.Throws<FormatException>(() => ColorParser.Parse(input));
	}

	[Theory]
	[InlineData("#GG0000")]
	[InlineData("#FF00")]
	public void Parse_InvalidHex_ThrowsFormatException(string input)
	{
		Assert.Throws<FormatException>(() => ColorParser.Parse(input));
	}

	[Theory]
	[InlineData("256,0,0")]
	[InlineData("1,2")]
	[InlineData("a,b,c")]
	public void Parse_InvalidRgb_ThrowsFormatException(string input)
	{
		Assert.Throws<FormatException>(() => ColorParser.Parse(input));
	}
}
