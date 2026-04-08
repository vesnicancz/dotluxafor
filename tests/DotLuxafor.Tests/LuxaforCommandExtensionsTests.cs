using DotLuxafor;
using Moq;

namespace DotLuxafor.Tests;

public class LuxaforCommandExtensionsTests
{
	private readonly Mock<ILuxaforCommands> _commands = new();

	[Fact]
	public async Task SetColorAsync_DelegatesToInterfaceWithCorrectColor()
	{
		var ct = TestContext.Current.CancellationToken;
		_commands.Setup(c => c.SetColorAsync(It.IsAny<LuxaforColor>(), It.IsAny<LedTarget>(), It.IsAny<CancellationToken>()))
			.Returns(Task.CompletedTask);

		await _commands.Object.SetColorAsync(255, 128, 0, cancellationToken: ct);

		_commands.Verify(c => c.SetColorAsync(
			new LuxaforColor(255, 128, 0),
			LedTarget.All,
			ct), Times.Once);
	}

	[Fact]
	public async Task SetColorAsync_WithTarget_PassesTargetThrough()
	{
		var ct = TestContext.Current.CancellationToken;
		_commands.Setup(c => c.SetColorAsync(It.IsAny<LuxaforColor>(), It.IsAny<LedTarget>(), It.IsAny<CancellationToken>()))
			.Returns(Task.CompletedTask);

		await _commands.Object.SetColorAsync(10, 20, 30, LedTarget.TopSide, ct);

		_commands.Verify(c => c.SetColorAsync(
			new LuxaforColor(10, 20, 30),
			LedTarget.TopSide,
			ct), Times.Once);
	}

	[Fact]
	public async Task FadeToAsync_DelegatesToInterfaceWithCorrectColor()
	{
		var ct = TestContext.Current.CancellationToken;
		_commands.Setup(c => c.FadeToAsync(It.IsAny<LuxaforColor>(), It.IsAny<byte>(), It.IsAny<LedTarget>(), It.IsAny<CancellationToken>()))
			.Returns(Task.CompletedTask);

		await _commands.Object.FadeToAsync(100, 200, 50, speed: 128, cancellationToken: ct);

		_commands.Verify(c => c.FadeToAsync(
			new LuxaforColor(100, 200, 50),
			128,
			LedTarget.All,
			ct), Times.Once);
	}

	[Fact]
	public async Task StrobeAsync_DelegatesToInterfaceWithCorrectColor()
	{
		var ct = TestContext.Current.CancellationToken;
		_commands.Setup(c => c.StrobeAsync(It.IsAny<LuxaforColor>(), It.IsAny<byte>(), It.IsAny<byte>(), It.IsAny<LedTarget>(), It.IsAny<CancellationToken>()))
			.Returns(Task.CompletedTask);

		await _commands.Object.StrobeAsync(255, 0, 0, speed: 64, repeat: 5, cancellationToken: ct);

		_commands.Verify(c => c.StrobeAsync(
			new LuxaforColor(255, 0, 0),
			64,
			5,
			LedTarget.All,
			ct), Times.Once);
	}

	[Fact]
	public async Task WaveAsync_DelegatesToInterfaceWithCorrectColor()
	{
		var ct = TestContext.Current.CancellationToken;
		_commands.Setup(c => c.WaveAsync(It.IsAny<WaveType>(), It.IsAny<LuxaforColor>(), It.IsAny<byte>(), It.IsAny<byte>(), It.IsAny<CancellationToken>()))
			.Returns(Task.CompletedTask);

		await _commands.Object.WaveAsync(WaveType.Short, 0, 255, 0, speed: 100, repeat: 3, cancellationToken: ct);

		_commands.Verify(c => c.WaveAsync(
			WaveType.Short,
			new LuxaforColor(0, 255, 0),
			100,
			3,
			ct), Times.Once);
	}
}
