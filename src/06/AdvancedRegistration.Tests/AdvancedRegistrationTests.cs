using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AdvancedRegistration.Tests;

public class AdvancedRegistrationTests
{
    [Fact]
    public void FactoryRegistration_WhenConfigured_ResolvesServiceWithApiKey()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Notification:ApiKey"] = "sample-key"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddFactorySample(configuration);

        using var provider = services.BuildServiceProvider();

        var notification = provider.GetRequiredService<INotificationService>();
        Assert.Equal("sample-key", notification.ApiKey);
    }

    [Fact]
    public void ExistingInstanceRegistration_WhenSingletonRegistered_ResolvesSameReference()
    {
        var settings = new AppSettings { MaxRetries = 3, TimeoutSeconds = 30 };
        var services = new ServiceCollection();
        services.AddSingleton(settings);

        using var provider = services.BuildServiceProvider();

        var resolved = provider.GetRequiredService<AppSettings>();
        Assert.Same(settings, resolved);
    }

    [Fact]
    public void KeyedServices_WhenRegistered_ResolveByKey()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<ICache, RedisCache>("distributed");
        services.AddKeyedSingleton<ICache, MemoryCache>("local");

        using var provider = services.BuildServiceProvider();

        var distributed = provider.GetRequiredKeyedService<ICache>("distributed");
        var local = provider.GetRequiredKeyedService<ICache>("local");

        Assert.Equal("distributed", distributed.ProviderName);
        Assert.Equal("local", local.ProviderName);
    }

    [Fact]
    public async Task MultipleImplementations_WhenInjectedAsEnumerable_AllChannelsAreUsed()
    {
        var services = new ServiceCollection();
        services.AddSingleton<INotificationChannel, EmailNotificationChannel>();
        services.AddSingleton<INotificationChannel, SmsNotificationChannel>();
        services.AddSingleton<INotificationChannel, PushNotificationChannel>();
        services.AddSingleton<NotificationService>();

        using var provider = services.BuildServiceProvider();

        var notification = provider.GetRequiredService<NotificationService>();
        var sent = await notification.NotifyAllAsync("hello");

        Assert.Equal(3, sent.Count);
    }

    [Fact]
    public async Task ManualScope_WhenRunnerExecutes_UsesScopedServiceSafely()
    {
        CountingOrderService.ProcessCallCount = 0;

        var services = new ServiceCollection();
        services.AddScoped<IOrderService, CountingOrderService>();
        services.AddSingleton<OrderProcessingRunner>();

        using var provider = services.BuildServiceProvider();
        var runner = provider.GetRequiredService<OrderProcessingRunner>();

        await runner.RunOnceAsync();

        Assert.Equal(1, CountingOrderService.ProcessCallCount);
    }
}
