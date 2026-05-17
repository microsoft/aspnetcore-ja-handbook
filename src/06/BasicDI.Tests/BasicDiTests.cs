using Microsoft.Extensions.DependencyInjection;

namespace BasicDI.Tests;

public class BasicDiTests
{
    [Fact]
    public void ResolveController_WhenRegistered_ControllerWorksWithInjectedService()
    {
        var services = new ServiceCollection();
        services.AddBasicDiSample();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var controller = scope.ServiceProvider.GetRequiredService<OrderController>();
        var orders = controller.Index();

        Assert.Equal(2, orders.Count);
    }

    [Fact]
    public void Details_WhenIdExists_ReturnsOrder()
    {
        var services = new ServiceCollection();
        services.AddBasicDiSample();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var controller = scope.ServiceProvider.GetRequiredService<OrderController>();
        var order = controller.Details(1);

        Assert.NotNull(order);
        Assert.Equal("Keyboard", order!.ProductName);
    }

    [Fact]
    public void EndpointHandler_WhenInvoked_UsesInjectedService()
    {
        var services = new ServiceCollection();
        services.AddBasicDiSample();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var orderService = scope.ServiceProvider.GetRequiredService<IOrderService>();
        var orders = OrderEndpoints.GetOrders(orderService);

        Assert.Equal(2, orders.Count);
    }
}
