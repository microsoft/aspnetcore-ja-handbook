using Microsoft.Extensions.DependencyInjection;

namespace ServiceLayer;

public sealed class Product
{
	public int Id { get; set; }
	public required string Name { get; set; }
	public decimal Price { get; set; }
}

public sealed record ProductDto(int Id, string Name, decimal Price);

public sealed record CreateProductRequest(string Name, decimal Price);

public sealed record UpdateProductRequest(string Name, decimal Price);

public interface IProductRepository
{
	Task<IReadOnlyList<Product>> GetAllAsync();
	Task<Product?> GetByIdAsync(int id);
	Task AddAsync(Product product);
	Task UpdateAsync(Product product);
	Task DeleteAsync(Product product);
}

public sealed class InMemoryProductRepository : IProductRepository
{
	private readonly List<Product> _products = [];
	private int _nextId = 1;

	public Task<IReadOnlyList<Product>> GetAllAsync() => Task.FromResult((IReadOnlyList<Product>)_products.ToList());

	public Task<Product?> GetByIdAsync(int id) => Task.FromResult(_products.FirstOrDefault(p => p.Id == id));

	public Task AddAsync(Product product)
	{
		product.Id = _nextId++;
		_products.Add(product);
		return Task.CompletedTask;
	}

	public Task UpdateAsync(Product product)
	{
		var current = _products.First(p => p.Id == product.Id);
		current.Name = product.Name;
		current.Price = product.Price;
		return Task.CompletedTask;
	}

	public Task DeleteAsync(Product product)
	{
		_products.Remove(product);
		return Task.CompletedTask;
	}
}

public interface IProductService
{
	Task<IReadOnlyList<ProductDto>> GetAllAsync();
	Task<ProductDto?> GetByIdAsync(int id);
	Task<ProductDto> CreateAsync(CreateProductRequest request);
	Task UpdateAsync(int id, UpdateProductRequest request);
	Task DeleteAsync(int id);
}

public sealed class ProductService(IProductRepository repository) : IProductService
{
	public async Task<IReadOnlyList<ProductDto>> GetAllAsync()
	{
		var products = await repository.GetAllAsync();
		return products.Select(p => new ProductDto(p.Id, p.Name, p.Price)).ToList();
	}

	public async Task<ProductDto?> GetByIdAsync(int id)
	{
		var product = await repository.GetByIdAsync(id);
		return product is null ? null : new ProductDto(product.Id, product.Name, product.Price);
	}

	public async Task<ProductDto> CreateAsync(CreateProductRequest request)
	{
		var product = new Product { Name = request.Name, Price = request.Price };
		await repository.AddAsync(product);
		return new ProductDto(product.Id, product.Name, product.Price);
	}

	public async Task UpdateAsync(int id, UpdateProductRequest request)
	{
		var product = await repository.GetByIdAsync(id)
			?? throw new KeyNotFoundException($"Product with ID {id} not found.");

		product.Name = request.Name;
		product.Price = request.Price;
		await repository.UpdateAsync(product);
	}

	public async Task DeleteAsync(int id)
	{
		var product = await repository.GetByIdAsync(id)
			?? throw new KeyNotFoundException($"Product with ID {id} not found.");

		await repository.DeleteAsync(product);
	}
}

public static class ServiceCollectionExtensions
{
	public static IServiceCollection AddApplicationServices(this IServiceCollection services)
	{
		services.AddScoped<IProductService, ProductService>();
		services.AddScoped<IProductRepository, InMemoryProductRepository>();
		return services;
	}
}
