using Microsoft.Extensions.DependencyInjection;

namespace BasicDI;

public record Order(int Id, string ProductName);

public interface IOrderService
{
	IReadOnlyList<Order> GetAll();
	Order? GetById(int id);
	void Create(Order order);
}

public sealed class InMemoryOrderService : IOrderService
{
	private readonly List<Order> _orders =
	[
		new(1, "Keyboard"),
		new(2, "Mouse")
	];

	public IReadOnlyList<Order> GetAll() => _orders;

	public Order? GetById(int id) => _orders.FirstOrDefault(o => o.Id == id);

	public void Create(Order order) => _orders.Add(order);
}

public sealed class OrderController(IOrderService orderService)
{
	public IReadOnlyList<Order> Index() => orderService.GetAll();

	public Order? Details(int id) => orderService.GetById(id);
}

// ドキュメント外: テストで Minimal API の注入イメージを再現するために追加
public static class OrderEndpoints
{
	public static IReadOnlyList<Order> GetOrders(IOrderService orderService) => orderService.GetAll();

	public static Order? GetOrderById(int id, IOrderService orderService) => orderService.GetById(id);
}

public static class ServiceRegistration
{
	public static IServiceCollection AddBasicDiSample(this IServiceCollection services)
	{
		services.AddScoped<IOrderService, InMemoryOrderService>();
		services.AddScoped<OrderController>();
		return services;
	}
}
