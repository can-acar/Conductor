using Conductor.Attributes;
using Conductor.Core;
using ExampleWebApplication.Module;
using System.Data;

namespace ExampleWebApplication.Handlers;

public class ProductListQueries
{
    private readonly ProductDb _db;
    private readonly ILogger<ProductListQueries> _logger;

    public ProductListQueries(ProductDb db, ILogger<ProductListQueries> logger)
    {
        _db = db;
        _logger = logger;
    }

    [Handle]
    [CacheModule(Duration = 60)] // Cache for 60 seconds
    [Validate(ValidateRequest = true, ValidateResponse = true)]
    [Audit(Category = "ProductQuery", LogRequest = true, LogResponse = true, LogExecutionTime = true)]
    public async Task<List<Product>> GetProducts(Query<ProductListQuery> query)
    {
        _logger.LogInformation("Processing product list query");

        var products = await _db.GetProductsAsync();
        var filteredProducts = products.AsQueryable();

        if (!string.IsNullOrEmpty(query.Data.Category))
        {
            filteredProducts = filteredProducts.Where(p =>
                p.Category.Equals(query.Data.Category, StringComparison.OrdinalIgnoreCase));
        }

        if (query.Data.MinPrice.HasValue)
        {
            filteredProducts = filteredProducts.Where(p => p.Price >= query.Data.MinPrice.Value);
        }

        if (query.Data.MaxPrice.HasValue)
        {
            filteredProducts = filteredProducts.Where(p => p.Price <= query.Data.MaxPrice.Value);
        }

        var pagedProducts = filteredProducts
            .Skip((query.Data.Page - 1) * query.Data.PageSize)
            .Take(query.Data.PageSize)
            .ToList();

        return pagedProducts;
    }

    [Handle]
    [CacheModule(Duration = 300, SlidingExpiration = true)]
    [Validate(ValidateRequest = true)]
    [Audit(Category = "ProductQuery", LogRequest = true, LogResponse = true)]
    [Transaction(IsolationLevel = System.Data.IsolationLevel.ReadCommitted, TimeoutSeconds = 30)]
    public async Task<Product?> GetProductById(Query<int> query)
    {
        _logger.LogInformation("Getting product by ID: {ProductId}", query.Data);
        return await _db.GetProductByIdAsync(query.Data);
    }
}