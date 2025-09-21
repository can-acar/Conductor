using Conductor.Attributes;
using Conductor.Validation;
using ExampleWebApplication.Module;

namespace ExampleWebApplication.Validators;

public class ProductValidator : AbstractValidator<Product>
{
    public ProductValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Product name is required").WithErrorCode("REQUIRED")
            .MaximumLength(100).WithMessage("Product name cannot exceed 100 characters").WithErrorCode("MAX_LENGTH");

        RuleFor(x => x.Price)
            .GreaterThanOrEqualTo(0).WithMessage("Price cannot be negative").WithErrorCode("NEGATIVE_PRICE")
            .LessThanOrEqualTo(1000000).WithMessage("Price cannot exceed 1,000,000").WithErrorCode("MAX_PRICE");

        RuleFor(x => x.Category)
            .NotEmpty().WithMessage("Category is required").WithErrorCode("REQUIRED")
            .MaximumLength(50).WithMessage("Category cannot exceed 50 characters").WithErrorCode("MAX_LENGTH");

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("Description cannot exceed 500 characters").WithErrorCode("MAX_LENGTH")
            .When(x => !string.IsNullOrEmpty(x.Description));

        // Example of custom validation
        RuleFor(x => x.Name)
            .Must(BeUniqueProductName).WithMessage("Product name must be unique").WithErrorCode("DUPLICATE_NAME")
            .When(x => !string.IsNullOrEmpty(x.Name));

        // Example of async validation
        RuleFor(x => x.Category)
            .MustAsync(async (category, cancellation) => await IsCategoryValidAsync(category, cancellation))
            .WithMessage("Category is not valid").WithErrorCode("INVALID_CATEGORY")
            .When(x => !string.IsNullOrEmpty(x.Category));
    }

    private bool BeUniqueProductName(string name)
    {
        // In a real application, this would check against a database
        // For demonstration purposes, we'll just check for some reserved names
        var reservedNames = new[] { "admin", "system", "test", "reserved" };
        return !reservedNames.Contains(name.ToLowerInvariant());
    }

    private async Task<bool> IsCategoryValidAsync(string category, CancellationToken cancellationToken)
    {
        // Simulate async database call
        await Task.Delay(10, cancellationToken);

        // In a real application, this would validate against a database of valid categories
        var validCategories = new[] { "electronics", "books", "clothing", "home", "sports", "toys" };
        return validCategories.Contains(category.ToLowerInvariant());
    }
}