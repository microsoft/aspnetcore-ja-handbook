using Microsoft.Extensions.DependencyInjection;

namespace ServiceLifetimes;

public interface IOperationTransient
{
	Guid OperationId { get; }
}

public interface IOperationScoped
{
	Guid OperationId { get; }
}

public interface IOperationSingleton
{
	Guid OperationId { get; }
}

public sealed class Operation : IOperationTransient, IOperationScoped, IOperationSingleton
{
	public Guid OperationId { get; } = Guid.NewGuid();
}

public static class LifetimeRegistration
{
	public static IServiceCollection AddLifetimeSamples(this IServiceCollection services)
	{
		services.AddTransient<IOperationTransient, Operation>();
		services.AddScoped<IOperationScoped, Operation>();
		services.AddSingleton<IOperationSingleton, Operation>();
		return services;
	}
}

public sealed class MyScopedService;

public sealed class MySingletonService(MyScopedService scopedService)
{
	public MyScopedService ScopedService { get; } = scopedService;
}
