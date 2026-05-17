using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AdvancedRegistration;

public interface INotificationService
{
	string ApiKey { get; }
}

public sealed class SlackNotificationService(string apiKey) : INotificationService
{
	public string ApiKey { get; } = apiKey;
}

public sealed class AppSettings
{
	public int MaxRetries { get; init; }
	public int TimeoutSeconds { get; init; }
}

public interface ICache
{
	string ProviderName { get; }
	string? Get(string key);
}

public sealed class RedisCache : ICache
{
	public string ProviderName => "distributed";
	public string? Get(string key) => $"redis:{key}";
}

public sealed class MemoryCache : ICache
{
	public string ProviderName => "local";
	public string? Get(string key) => $"memory:{key}";
}

public interface INotificationChannel
{
	Task<string> SendAsync(string message);
}

public sealed class EmailNotificationChannel : INotificationChannel
{
	public Task<string> SendAsync(string message) => Task.FromResult($"email:{message}");
}

public sealed class SmsNotificationChannel : INotificationChannel
{
	public Task<string> SendAsync(string message) => Task.FromResult($"sms:{message}");
}

public sealed class PushNotificationChannel : INotificationChannel
{
	public Task<string> SendAsync(string message) => Task.FromResult($"push:{message}");
}

public sealed class NotificationService(IEnumerable<INotificationChannel> channels)
{
	public async Task<IReadOnlyList<string>> NotifyAllAsync(string message)
	{
		var results = new List<string>();
		foreach (var channel in channels)
		{
			results.Add(await channel.SendAsync(message));
		}

		return results;
	}
}

public interface IOrderService
{
	Task ProcessPendingOrdersAsync();
}

public sealed class CountingOrderService : IOrderService
{
	public static int ProcessCallCount;

	public Task ProcessPendingOrdersAsync()
	{
		Interlocked.Increment(ref ProcessCallCount);
		return Task.CompletedTask;
	}
}

public sealed class OrderProcessingRunner(IServiceScopeFactory serviceScopeFactory)
{
	public async Task RunOnceAsync()
	{
		using var scope = serviceScopeFactory.CreateScope();
		var service = scope.ServiceProvider.GetRequiredService<IOrderService>();
		await service.ProcessPendingOrdersAsync();
	}
}

public static class AdvancedRegistrationSetup
{
	public static IServiceCollection AddFactorySample(this IServiceCollection services, IConfiguration configuration)
	{
		services.AddSingleton<INotificationService>(_ =>
		{
			var apiKey = configuration["Notification:ApiKey"]
				?? throw new InvalidOperationException("Notification:ApiKey is not configured.");
			return new SlackNotificationService(apiKey);
		});

		return services;
	}
}
