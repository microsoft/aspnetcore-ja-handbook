using Microsoft.Extensions.DependencyInjection;

namespace ServiceLayer.Tests;

public class ServiceLayerTests
{
    [Fact]
    public void Registration_WhenAddApplicationServicesCalled_ResolvesService()
    {
        var services = new ServiceCollection();
        services.AddApplicationServices();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var productService = scope.ServiceProvider.GetRequiredService<IProductService>();
        Assert.NotNull(productService);
    }

    [Fact]
    public async Task CrudFlow_WhenUsingProductService_BehavesAsDocumented()
    {
        var services = new ServiceCollection();
        services.AddApplicationServices();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var productService = scope.ServiceProvider.GetRequiredService<IProductService>();

        var created = await productService.CreateAsync(new CreateProductRequest("Notebook", 1200m));
        Assert.Equal("Notebook", created.Name);

        var found = await productService.GetByIdAsync(created.Id);
        Assert.NotNull(found);

        await productService.UpdateAsync(created.Id, new UpdateProductRequest("Notebook Pro", 1800m));
        var updated = await productService.GetByIdAsync(created.Id);
        Assert.Equal("Notebook Pro", updated!.Name);

        await productService.DeleteAsync(created.Id);
        var deleted = await productService.GetByIdAsync(created.Id);
        Assert.Null(deleted);
    }
}
