using Conductor.Attributes;
using Conductor.Core;
using ExampleWebApplication.Module;

namespace ExampleWebApplication.Handlers;

public class ProductEventHandlers
{
    private readonly ILogger<ProductEventHandlers> _logger;

    public ProductEventHandlers(ILogger<ProductEventHandlers> logger)
    {
        _logger = logger;
    }

    [Saga("ProductCreation", Order = 1)]
    public async Task HandleProductCreated(Event<Product> productEvent)
    {
        _logger.LogInformation("Product created: {ProductName}", productEvent.Data.Name);

        // Saga step 1: Log product creation
        await Task.Delay(100);
    }

    [Saga("ProductCreation", Order = 2)]
    public async Task SendNotification(Event<Product> productEvent)
    {
        _logger.LogInformation("Sending notification for product: {ProductName}", productEvent.Data.Name);

        // Saga step 2: Send notification
        await Task.Delay(200);
    }
}