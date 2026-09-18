using DotLuxafor;
using Microsoft.Extensions.Configuration;

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
	[InlineData("FF0000", 255, 0, 0)]
	[InlineData("abcdef", 0xAB, 0xCD, 0xEF)]
	[InlineData("#F00", 255, 0, 0)]
	[InlineData("0f8", 0x00, 0xFF, 0x88)]
	[InlineData("  #FF8800  ", 255, 136, 0)]
	public void FromHex_ValidInput_ReturnsCorrectColor(string hex, byte r, byte g, byte b)
	{
		var color = LuxaforColor.FromHex(hex);

		Assert.Equal(r, color.R);
		Assert.Equal(g, color.G);
		Assert.Equal(b, color.B);
	}

	[Theory]
	[InlineData("")]
	[InlineData("#")]
	[InlineData("#FF00")]
	[InlineData("#FF00Z")]
	[InlineData("#GGG")]
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

	[Theory]
	[InlineData("#FFF", 255, 255, 255)]
	[InlineData("#000", 0, 0, 0)]
	[InlineData("#8A3", 0x88, 0xAA, 0x33)]
	public void FromHex_ShortForm_DoublesEachDigit(string hex, byte r, byte g, byte b)
	{
		Assert.Equal(new LuxaforColor(r, g, b), LuxaforColor.FromHex(hex));
	}

	[Theory]
	[InlineData("#FFF", "#FFFFFF")]
	[InlineData("#8A3", "#88AA33")]
	[InlineData("f00", "#FF0000")]
	public void FromHex_ShortForm_MatchesTheEquivalentLongForm(string shortForm, string longForm)
	{
		Assert.Equal(LuxaforColor.FromHex(longForm), LuxaforColor.FromHex(shortForm));
	}

	[Theory]
	[InlineData(1.0, 200, 100, 50)]
	[InlineData(0.5, 100, 50, 25)]
	[InlineData(0.0, 0, 0, 0)]
	public void WithBrightness_ScalesEachChannel(double factor, byte r, byte g, byte b)
	{
		var dimmed = new LuxaforColor(200, 100, 50).WithBrightness(factor);

		Assert.Equal(new LuxaforColor(r, g, b), dimmed);
	}

	[Theory]
	[InlineData(-1.0, 0, 0, 0)]
	[InlineData(2.0, 200, 100, 50)]
	public void WithBrightness_ClampsOutOfRangeFactors(double factor, byte r, byte g, byte b)
	{
		var scaled = new LuxaforColor(200, 100, 50).WithBrightness(factor);

		Assert.Equal(new LuxaforColor(r, g, b), scaled);
	}

	[Fact]
	public void WithBrightness_NeverOverflowsAChannel()
	{
		Assert.Equal(LuxaforColor.White, LuxaforColor.White.WithBrightness(1.0));
	}

	[Theory]
	[InlineData(0.0, 0, 0, 0)]
	[InlineData(0.5, 128, 128, 128)]
	[InlineData(1.0, 255, 255, 255)]
	public void Lerp_InterpolatesBetweenEndpoints(double amount, byte r, byte g, byte b)
	{
		var mixed = LuxaforColor.Lerp(LuxaforColor.Off, LuxaforColor.White, amount);

		Assert.Equal(new LuxaforColor(r, g, b), mixed);
	}

	[Fact]
	public void Lerp_ClampsOutOfRangeAmounts()
	{
		Assert.Equal(LuxaforColor.Off, LuxaforColor.Lerp(LuxaforColor.Off, LuxaforColor.White, -1.0));
		Assert.Equal(LuxaforColor.White, LuxaforColor.Lerp(LuxaforColor.Off, LuxaforColor.White, 2.0));
	}

	[Fact]
	public void Lerp_MovesEachChannelIndependently()
	{
		var mixed = LuxaforColor.Lerp(new LuxaforColor(0, 100, 200), new LuxaforColor(100, 100, 0), 0.5);

		Assert.Equal(new LuxaforColor(50, 100, 100), mixed);
	}

	[Fact]
	public void Parse_IsEquivalentToFromHex()
	{
		Assert.Equal(LuxaforColor.FromHex("#FF8800"), LuxaforColor.Parse("#FF8800", null));
		Assert.Throws<FormatException>(() => LuxaforColor.Parse("nope", null));
	}

	[Fact]
	public void TryParse_IsEquivalentToTryFromHex()
	{
		Assert.True(LuxaforColor.TryParse("#FF8800", null, out var parsed));
		Assert.Equal(LuxaforColor.FromHex("#FF8800"), parsed);

		Assert.False(LuxaforColor.TryParse("nope", null, out _));
	}

	[Fact]
	public void ImplementsIParsable()
	{
		// Minimal-API route and query binding goes looking for this.
		Assert.True(typeof(IParsable<LuxaforColor>).IsAssignableFrom(typeof(LuxaforColor)));

		Assert.Equal(LuxaforColor.Red, ParseVia<LuxaforColor>("#FF0000"));
	}

	private static T ParseVia<T>(string text) where T : IParsable<T> => T.Parse(text, null);

	[Fact]
	public void TypeConverter_ConvertsFromString()
	{
		var converter = System.ComponentModel.TypeDescriptor.GetConverter(typeof(LuxaforColor));

		Assert.True(converter.CanConvertFrom(typeof(string)));
		Assert.Equal(LuxaforColor.FromHex("#FF8800"), converter.ConvertFromString("#FF8800"));
	}

	[Fact]
	public void TypeConverter_ConvertsToString()
	{
		var converter = System.ComponentModel.TypeDescriptor.GetConverter(typeof(LuxaforColor));

		Assert.Equal("#FF8800", converter.ConvertToString(LuxaforColor.FromHex("#FF8800")));
	}

	[Fact]
	public void BindsFromConfiguration()
	{
		// The converter exists so that this works; configuration binding goes through
		// TypeConverter rather than IParsable.
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?>
			{
				["Status:BusyColor"] = "#FF8800",
			})
			.Build();

		var options = configuration.GetSection("Status").Get<ColorOptions>();

		Assert.NotNull(options);
		Assert.Equal(LuxaforColor.FromHex("#FF8800"), options.BusyColor);
	}

	private sealed class ColorOptions
	{
		public LuxaforColor BusyColor { get; set; }
	}
}
