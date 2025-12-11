using Conductor.Core;
using ExampleWebApplication.Module;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using Conductor.Interfaces;
using Conductor.Transport.Http;

namespace ExampleWebApplication.Controllers;

/// <summary>
/// Product management controller demonstrating Conductor framework capabilities
/// </summary>
[Route("api/[controller]")]
[Produces("application/json")]
[Tags("Products")]
public class ProductController(IConductor conductor) : ControllerBase
{
	/// <summary>
	/// Gets a list of products based on query criteria using Conductor's query handling
	/// </summary>
	/// <param name="query">Product list query criteria</param>
	/// <returns>List of products matching the criteria</returns>
	/// <response code="200">Returns the list of products</response>
	/// <response code="400">If the query is invalid</response>
	[HttpPost("list")]
	[ProducesResponseType(typeof(ApiResponse<List<Product>>), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
	public async Task<IActionResult> GetProducts([FromBody] ProductListQuery query)
	{
		var products = await conductor.Send<List<Product>>(new Query<ProductListQuery>(query));
		return Ok(new
		{
			Data = products.ToList(),
			Metadata = new
			{
				Page = query.Page,
				PageSize = query.PageSize,
				TotalCount = products.Count
			}
		});
	}

	/// <summary>
	/// Gets a specific product by ID using Conductor's query handling
	/// </summary>
	/// <param name="id">The product ID</param>
	/// <returns>The product with the specified ID</returns>
	/// <response code="200">Returns the product</response>
	/// <response code="404">If the product is not found</response>
	/// <response code="400">If the request is invalid</response>
	[HttpGet("{id}")]
	[ProducesResponseType(typeof(ApiResponse<Product>), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
	[ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
	public async Task<IActionResult> GetProduct(int id)
	{
		var product = await conductor.Send<Product?>(new Query<int>(id));
		if (product == null)
			return NotFound($"Product with ID {id} not found");
		return Ok(product);
	}

	/// <summary>
	/// Creates a new product and publishes an event using Conductor's event publishing
	/// </summary>
	/// <param name="product">The product to create</param>
	/// <returns>The created product</returns>
	/// <response code="201">Returns the newly created product</response>
	/// <response code="400">If the product data is invalid</response>
	[HttpPost("create")]
	[ProducesResponseType(typeof(ApiResponse<Product>), StatusCodes.Status201Created)]
	[ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
	public async Task<IActionResult> CreateProduct([FromBody] Product product)
	{
		// Assign new ID (in real app, this would be done by database)
		product.Id = new Random().Next(1000, 9999);
		product.CreatedDate = DateTime.UtcNow;

		// Publish event - Now works with variance support
		await conductor.Publish(new Event<Product>(product));
		var response = Ok(product);

		// Return 201 Created with location header
		var actionResult = CreatedAtAction(nameof(GetProduct), new { id = product.Id }, ((OkObjectResult)response).Value);
		return actionResult;
	}

	/// <summary>
	/// Processes a product through the pipeline using Conductor's pipeline functionality
	/// </summary>
	/// <param name="product">The product to process</param>
	/// <returns>The processed product</returns>
	/// <response code="200">Returns the processed product</response>
	/// <response code="400">If the processing fails</response>
	[HttpPost("process")]
	[ProducesResponseType(typeof(ApiResponse<Product>), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
	public async Task<IActionResult> ProcessProduct([FromBody] Product product)
	{
		// Send through pipeline
		var result = await conductor.SendThrough<Product, Product>(new Bus<Product>(product));
		return Ok(result);
	}
}