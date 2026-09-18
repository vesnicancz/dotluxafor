using DotLuxafor;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace DotLuxafor.Tests;

public class DependencyInjectionTests
{
	[Fact]
	public void AddLuxafor_RegistersDeviceManager()
	{
		var services = new ServiceCollection();

		services.AddLuxafor();
		var provider = services.BuildServiceProvider();

		var manager = provider.GetService<ILuxaforDeviceManager>();
		Assert.NotNull(manager);
		Assert.IsType<LuxaforDeviceManager>(manager);
	}

	[Fact]
	public void AddLuxafor_RegistersDeviceManagerAsSingleton()
	{
		var services = new ServiceCollection();

		services.AddLuxafor();
		var provider = services.BuildServiceProvider();

		var a = provider.GetService<ILuxaforDeviceManager>();
		var b = provider.GetService<ILuxaforDeviceManager>();
		Assert.Same(a, b);
	}

	[Fact]
	public void AddLuxafor_RegistersDeviceOpener()
	{
		var services = new ServiceCollection();

		services.AddLuxafor();
		var provider = services.BuildServiceProvider();

		var opener = provider.GetService<ILuxaforDeviceOpener>();

		// The narrow face of the same singleton, not a second manager of its own.
		Assert.NotNull(opener);
		Assert.Same(provider.GetRequiredService<ILuxaforDeviceManager>(), opener);
	}

	[Fact]
	public void AddLuxafor_WithCustomDeviceManager_OpenerIsThatManager()
	{
		var services = new ServiceCollection();
		var mockManager = new Mock<ILuxaforDeviceManager>();

		services.AddSingleton(mockManager.Object);
		services.AddLuxafor();

		var provider = services.BuildServiceProvider();

		// A substituted manager has to win for the opener too, or a test double would be bypassed
		// by whichever of the two faces the code under test happens to ask for.
		Assert.Same(mockManager.Object, provider.GetService<ILuxaforDeviceOpener>());
	}

	[Fact]
	public void AddLuxafor_WithOptions_ConfiguresOptions()
	{
		var services = new ServiceCollection();

		services.AddLuxafor(options =>
		{
			options.AutoReconnect = true;
			options.ReconnectDelay = TimeSpan.FromSeconds(5);
			options.AutoMonitor = true;
		});

		var provider = services.BuildServiceProvider();
		var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<LuxaforOptions>>();

		Assert.True(options.Value.AutoReconnect);
		Assert.Equal(TimeSpan.FromSeconds(5), options.Value.ReconnectDelay);
		Assert.True(options.Value.AutoMonitor);
	}

	[Fact]
	public void AddLuxafor_WithCustomDeviceManager_UsesCustomManager()
	{
		var services = new ServiceCollection();
		var mockManager = new Mock<ILuxaforDeviceManager>();

		// Register custom manager BEFORE AddLuxafor (TryAddSingleton won't override)
		services.AddSingleton(mockManager.Object);
		services.AddLuxafor();

		var provider = services.BuildServiceProvider();
		var resolved = provider.GetService<ILuxaforDeviceManager>();

		Assert.Same(mockManager.Object, resolved);
	}

	[Fact]
	public void AddLuxafor_DoesNotRegisterDeviceDirectly()
	{
		var services = new ServiceCollection();

		services.AddLuxafor();
		var provider = services.BuildServiceProvider();

		Assert.Null(provider.GetService<ILuxaforDevice>());
		Assert.Null(provider.GetService<ILuxaforCommands>());
		Assert.Null(provider.GetService<ILuxaforConnection>());
		Assert.Null(provider.GetService<ILuxaforMonitor>());
	}

	[Fact]
	public void LuxaforOptions_DefaultValues()
	{
		var options = new LuxaforOptions();

		Assert.False(options.AutoReconnect);
		Assert.Equal(TimeSpan.FromSeconds(3), options.ReconnectDelay);
		Assert.False(options.AutoMonitor);
	}

	[Fact]
	public void AddLuxafor_RegistersOptionsValidator()
	{
		var services = new ServiceCollection();

		services.AddLuxafor();
		var provider = services.BuildServiceProvider();

		var validators = provider.GetServices<Microsoft.Extensions.Options.IValidateOptions<LuxaforOptions>>();
		Assert.NotEmpty(validators);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(-1)]
	public void LuxaforOptionsValidator_RejectsNonPositiveReconnectDelay(int seconds)
	{
		var services = new ServiceCollection();
		services.AddLuxafor(opts => opts.ReconnectDelay = TimeSpan.FromSeconds(seconds));
		var provider = services.BuildServiceProvider();

		var optionsMonitor = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<LuxaforOptions>>();

		Assert.Throws<Microsoft.Extensions.Options.OptionsValidationException>(() => optionsMonitor.Value);
	}

	[Fact]
	public void LuxaforOptionsValidator_AcceptsPositiveReconnectDelay()
	{
		var services = new ServiceCollection();
		services.AddLuxafor(opts => opts.ReconnectDelay = TimeSpan.FromMilliseconds(100));
		var provider = services.BuildServiceProvider();

		var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<LuxaforOptions>>();

		Assert.Equal(TimeSpan.FromMilliseconds(100), options.Value.ReconnectDelay);
	}

	[Fact]
	public void AddLuxaforHostedService_RegistersHostedService()
	{
		var services = new ServiceCollection();
		services.AddLogging();

		services.AddLuxaforHostedService(opts => opts.AutoReconnect = true);
		var provider = services.BuildServiceProvider();

		var hostedServices = provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>();
		Assert.Contains(hostedServices, s => s is LuxaforHostedService);
	}

	[Fact]
	public void AddLuxaforHostedService_RegistersAccessor()
	{
		var services = new ServiceCollection();
		services.AddLogging();

		services.AddLuxaforHostedService(opts => opts.AutoReconnect = true);
		var provider = services.BuildServiceProvider();

		Assert.NotNull(provider.GetService<ILuxaforDeviceAccessor>());
	}

	[Fact]
	public void AddLuxaforHostedService_AccessorIsTheRunningHostedService()
	{
		var services = new ServiceCollection();
		services.AddLogging();

		services.AddLuxaforHostedService(opts => opts.AutoReconnect = true);
		var provider = services.BuildServiceProvider();

		var accessor = provider.GetRequiredService<ILuxaforDeviceAccessor>();
		var hosted = provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
			.OfType<LuxaforHostedService>()
			.Single();

		// An accessor pointing at a second, never-started instance would always report no device.
		Assert.Same(hosted, accessor);
		Assert.Same(hosted, provider.GetRequiredService<LuxaforHostedService>());
	}

	[Fact]
	public void AddLuxaforHostedService_CalledTwice_RegistersOneInstance()
	{
		var services = new ServiceCollection();
		services.AddLogging();

		services.AddLuxaforHostedService(opts => opts.AutoReconnect = true);
		services.AddLuxaforHostedService(opts => opts.AutoMonitor = true);
		var provider = services.BuildServiceProvider();

		Assert.Single(provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<LuxaforHostedService>());
	}

	[Fact]
	public void LuxaforOptionsValidator_RejectsTwoDeviceSelectors()
	{
		var services = new ServiceCollection();
		services.AddLuxafor(opts =>
		{
			opts.DevicePath = "/dev/hidraw0";
			opts.SerialNumber = "1001";
		});
		var provider = services.BuildServiceProvider();

		var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<LuxaforOptions>>();

		// Resolving precedence silently would leave one of the two configured selectors ignored.
		var ex = Assert.Throws<Microsoft.Extensions.Options.OptionsValidationException>(() => options.Value);
		Assert.Contains(nameof(LuxaforOptions.DevicePath), ex.Message);
		Assert.Contains(nameof(LuxaforOptions.SerialNumber), ex.Message);
	}

	/// <summary>
	/// An unset configuration key binds to an empty string, which matches no device — it would
	/// otherwise look exactly like a device nobody ever plugged in.
	/// </summary>
	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void LuxaforOptionsValidator_RejectsAnEmptySelector(bool byPath)
	{
		var services = new ServiceCollection();
		services.AddLuxafor(opts =>
		{
			if (byPath)
			{
				opts.DevicePath = string.Empty;
			}
			else
			{
				opts.SerialNumber = string.Empty;
			}
		});
		var provider = services.BuildServiceProvider();

		var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<LuxaforOptions>>();

		Assert.Throws<Microsoft.Extensions.Options.OptionsValidationException>(() => options.Value);
	}

	[Fact]
	public void LuxaforOptionsValidator_AcceptsOneDeviceSelector()
	{
		var services = new ServiceCollection();
		services.AddLuxafor(opts => opts.SerialNumber = "1001");
		var provider = services.BuildServiceProvider();

		var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<LuxaforOptions>>();

		Assert.Equal("1001", options.Value.SerialNumber);
	}

	[Fact]
	public void LuxaforOptionsValidator_AcceptsNoDeviceSelector()
	{
		var services = new ServiceCollection();
		services.AddLuxafor();
		var provider = services.BuildServiceProvider();

		var options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<LuxaforOptions>>();

		Assert.Null(options.Value.DevicePath);
		Assert.Null(options.Value.SerialNumber);
		Assert.Null(options.Value.SelectDevice);
	}

	[Fact]
	public void AddLuxafor_WithoutHostedService_DoesNotRegisterAccessor()
	{
		var services = new ServiceCollection();

		services.AddLuxafor();
		var provider = services.BuildServiceProvider();

		// Nothing owns a device, so there is nothing for an accessor to expose.
		Assert.Null(provider.GetService<ILuxaforDeviceAccessor>());
	}
}
