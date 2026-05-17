using Microsoft.Extensions.DependencyInjection;

namespace ServiceLifetimes.Tests;

public class ServiceLifetimesTests
{
    [Fact]
    public void Transient_WhenResolvedTwiceInSameScope_ReturnsDifferentInstances()
    {
        var services = new ServiceCollection();
        services.AddLifetimeSamples();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var first = scope.ServiceProvider.GetRequiredService<IOperationTransient>();
        var second = scope.ServiceProvider.GetRequiredService<IOperationTransient>();

        Assert.NotEqual(first.OperationId, second.OperationId);
    }

    [Fact]
    public void Scoped_WhenResolvedWithinScope_ReturnsSameInstance()
    {
        var services = new ServiceCollection();
        services.AddLifetimeSamples();

        using var provider = services.BuildServiceProvider();

        Guid scope1First;
        Guid scope1Second;
        using (var scope1 = provider.CreateScope())
        {
            scope1First = scope1.ServiceProvider.GetRequiredService<IOperationScoped>().OperationId;
            scope1Second = scope1.ServiceProvider.GetRequiredService<IOperationScoped>().OperationId;
        }

        Guid scope2First;
        using (var scope2 = provider.CreateScope())
        {
            scope2First = scope2.ServiceProvider.GetRequiredService<IOperationScoped>().OperationId;
        }

        Assert.Equal(scope1First, scope1Second);
        Assert.NotEqual(scope1First, scope2First);
    }

    [Fact]
    public void Singleton_WhenResolvedAcrossScopes_ReturnsSameInstance()
    {
        var services = new ServiceCollection();
        services.AddLifetimeSamples();

        using var provider = services.BuildServiceProvider();

        Guid scope1;
        using (var first = provider.CreateScope())
        {
            scope1 = first.ServiceProvider.GetRequiredService<IOperationSingleton>().OperationId;
        }

        Guid scope2;
        using (var second = provider.CreateScope())
        {
            scope2 = second.ServiceProvider.GetRequiredService<IOperationSingleton>().OperationId;
        }

        Assert.Equal(scope1, scope2);
    }

    [Fact]
    public void ScopeValidation_WhenSingletonDependsOnScoped_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        services.AddSingleton<MySingletonService>();
        services.AddScoped<MyScopedService>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true
        });

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<MySingletonService>());
        Assert.Contains("scoped", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
