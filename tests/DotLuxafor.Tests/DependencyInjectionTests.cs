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
}
