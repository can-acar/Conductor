using Conductor.Attributes;
using Conductor.Core;
using Conductor.Modules.Pipeline;
using ExampleWebApplication.Module;

namespace ExampleWebApplication.Handlers;

public class ProductPipelineSteps : BasePipelineStep
{
	private readonly ILogger<ProductPipelineSteps> _logger;

	public ProductPipelineSteps(ILogger<ProductPipelineSteps> logger)
	{
		_logger = logger;
	}

	public override int Order => 1;
	public override string StepName => "ProductValidation";

	public override async Task<object> ExecuteAsync(object input, CancellationToken cancellationToken = default)
	{
		if (input is Product product)
		{
			_logger.LogInformation("Validating product: {ProductName}", product.Name);

			// Validate product
			if (string.IsNullOrEmpty(product.Name))
				throw new ArgumentException("Product name is required");
			if (product.Price <= 0)
				throw new ArgumentException("Product price must be greater than zero");

			// Set creation date if not set
			if (product.CreatedDate == default)
				product.CreatedDate = DateTime.UtcNow;
		}
		await Task.Delay(50, cancellationToken);
		return input;
	}

	[Pipeline("ProductProcessing", Order = 1)]
	public async Task<Product> ValidateProduct(Bus<Product> busData)
	{
		_logger.LogInformation("Pipeline: Validating product {ProductName}", busData.Data.Name);
		if (string.IsNullOrEmpty(busData.Data.Name))
			throw new ArgumentException("Product name is required");
		if (busData.Data.Price <= 0)
			throw new ArgumentException("Product price must be greater than zero");
		await Task.Delay(100);
		return busData.Data;
	}

	[Pipeline("ProductProcessing", Order = 2)]
	public async Task<Product> EnrichProduct(Bus<Product> busData)
	{
		_logger.LogInformation("Pipeline: Enriching product {ProductName}", busData.Data.Name);

		// Add creation date if missing
		if (busData.Data.CreatedDate == default)
			busData.Data.CreatedDate = DateTime.UtcNow;

		// Add metadata to bus context
		busData.Context["ProcessedAt"] = DateTime.UtcNow;
		busData.Context["ProcessedBy"] = "ProductPipelineSteps";
		await Task.Delay(100);
		return busData.Data;
	}
}