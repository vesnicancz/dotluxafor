namespace DotLuxafor.Cli.Tests;

public class ArgParserTests
{
	[Fact]
	public void ParsesKeyValuePairs()
	{
		var parser = new ArgParser(["command", "--color", "red", "--speed", "128"]);

		Assert.Equal("red", parser.GetRequired("color"));
		Assert.Equal("128", parser.GetRequired("speed"));
	}

	[Fact]
	public void GetRequired_MissingKey_ThrowsArgumentException()
	{
		var parser = new ArgParser(["command", "--color", "red"]);

		var ex = Assert.Throws<ArgumentException>(() => parser.GetRequired("speed"));
		Assert.Contains("--speed", ex.Message);
	}

	[Fact]
	public void GetOptional_MissingKey_ReturnsDefault()
	{
		var parser = new ArgParser(["command", "--color", "red"]);

		Assert.Null(parser.GetOptional("target"));
		Assert.Equal("all", parser.GetOptional("target", "all"));
	}

	[Fact]
	public void GetOptional_PresentKey_ReturnsValue()
	{
		var parser = new ArgParser(["command", "--target", "top"]);

		Assert.Equal("top", parser.GetOptional("target"));
	}

	[Fact]
	public void GetRequiredByte_ValidValue_ReturnsByte()
	{
		var parser = new ArgParser(["command", "--speed", "128"]);

		Assert.Equal(128, parser.GetRequiredByte("speed"));
	}

	[Fact]
	public void GetRequiredByte_InvalidValue_ThrowsArgumentException()
	{
		var parser = new ArgParser(["command", "--speed", "abc"]);

		var ex = Assert.Throws<ArgumentException>(() => parser.GetRequiredByte("speed"));
		Assert.Contains("--speed", ex.Message);
	}

	[Fact]
	public void GetRequiredByte_OutOfRange_ThrowsArgumentException()
	{
		var parser = new ArgParser(["command", "--speed", "999"]);

		Assert.Throws<ArgumentException>(() => parser.GetRequiredByte("speed"));
	}

	[Fact]
	public void OptionWithoutValue_ThrowsArgumentException()
	{
		var ex = Assert.Throws<ArgumentException>(() =>
			new ArgParser(["command", "--color"]));
		Assert.Contains("--color", ex.Message);
	}

	[Fact]
	public void OptionFollowedByAnotherOption_ThrowsArgumentException()
	{
		var ex = Assert.Throws<ArgumentException>(() =>
			new ArgParser(["command", "--color", "--speed", "10"]));
		Assert.Contains("--color", ex.Message);
	}

	[Fact]
	public void KeysAreCaseInsensitive()
	{
		var parser = new ArgParser(["command", "--Color", "red"]);

		Assert.Equal("red", parser.GetRequired("color"));
		Assert.Equal("red", parser.GetRequired("COLOR"));
	}

	[Fact]
	public void SkipsNonOptionTokens()
	{
		var parser = new ArgParser(["command", "extra", "--color", "red"]);

		Assert.Equal("red", parser.GetRequired("color"));
	}
}
