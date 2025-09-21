using Conductor.Core;
using Conductor.Middleware;
using ExampleWebApplication.Module;
using Microsoft.AspNetCore.Mvc;

namespace ExampleWebApplication.Controllers;

/// <summary>
/// Simplified product controller using standard ControllerBase with ResponseFormatter middleware
/// </summary>
[Route("api/[controller]")]
[ApiController]
[Produces("application/json")]
[Tags("Simple Products")]
public class SimpleProductController : ControllerBase
{
    private readonly IConductor _conductor;

    public SimpleProductController(IConductor conductor)
    {
        _conductor = conductor;
    }

    /// <summary>
    /// Gets a list of products - Response automatically wrapped by middleware
    /// </summary>
    [HttpPost("list")]
    public async Task<List<Product>> GetProducts([FromBody] ProductListQuery query)
    {
        // Sadece business logic - response formatting middleware tarafından yapılacak
        var products = await _conductor.Send<List<Product>>(new Query<ProductListQuery>(query));

        // Pagination info'yu header'a ekle - middleware bunu kullanacak
        var pagination = CreatePaginationInfo(query.Page, query.PageSize, products.Count);
        HttpContext.SetPaginationHeader(pagination);

        return products;
    }

    /// <summary>
    /// Gets a specific product by ID - Response automatically wrapped by middleware
    /// </summary>
    [HttpGet("{id}")]
    public async Task<Product?> GetProduct(int id)
    {
        // Sadece business logic - null response'u middleware handle edecek
        return await _conductor.Send<Product?>(new Query<int>(id));
    }

    /// <summary>
    /// Creates a new product - Response automatically wrapped by middleware
    /// </summary>
    [HttpPost("create")]
    public async Task<IActionResult> CreateProduct([FromBody] Product product)
    {
        // Business logic
        product.Id = new Random().Next(1000, 9999);
        product.CreatedDate = DateTime.UtcNow;

        // Event publishing
        await _conductor.Publish(new Event<Product>(product));

        // 201 Created response with location header
        return CreatedAtAction(nameof(GetProduct), new { id = product.Id }, product);
    }

    /// <summary>
    /// Processes a product through pipeline - Response automatically wrapped by middleware
    /// </summary>
    [HttpPost("process")]
    public async Task<Product> ProcessProduct([FromBody] Product product)
    {
        // Sadece business logic - response formatting otomatik
        return await _conductor.SendThrough<Product, Product>(new Bus<Product>(product));
    }

    /// <summary>
    /// Example of manual error handling (still gets formatted by middleware)
    /// </summary>
    [HttpGet("error-example/{id}")]
    public async Task<Product> GetProductWithValidation(int id)
    {
        if (id <= 0)
        {
            throw new ArgumentException("Product ID must be greater than 0");
        }

        var product = await _conductor.Send<Product?>(new Query<int>(id));

        if (product == null)
        {
            throw new KeyNotFoundException($"Product with ID {id} not found");
        }

        return product;
    }

    /// <summary>
    /// Example of returning different status codes
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateProduct(int id, [FromBody] Product product)
    {
        var existing = await _conductor.Send<Product?>(new Query<int>(id));

        if (existing == null)
        {
            return NotFound(); // Middleware will format this
        }

        // Update logic here
        product.Id = id;

        return Ok(product); // Middleware will format this
    }

    /// <summary>
    /// Example of NoContent response
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteProduct(int id)
    {
        var existing = await _conductor.Send<Product?>(new Query<int>(id));

        if (existing == null)
        {
            return NotFound();
        }

        // Delete logic here

        return NoContent(); // 204 - won't be wrapped by middleware
    }

    private PaginationInfo CreatePaginationInfo(int page, int pageSize, long totalCount)
    {
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        return new PaginationInfo
        {
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages,
            HasNextPage = page < totalPages,
            HasPreviousPage = page > 1
        };
    }
}