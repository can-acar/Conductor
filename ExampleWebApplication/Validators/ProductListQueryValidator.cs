using Conductor.Attributes;
using Conductor.Core;
using Conductor.Validation;
using ExampleWebApplication.Module;

namespace ExampleWebApplication.Validators;

public class ProductListQueryValidator : AbstractValidator<Query<ProductListQuery>>
{
    public ProductListQueryValidator()
    {
        RuleFor(x => x.Data)
            .NotNull().WithMessage("Query data cannot be null").WithErrorCode("REQUIRED");

        RuleFor(x => x.Data.Page)
            .GreaterThan(0).WithMessage("Page number must be greater than 0").WithErrorCode("INVALID_PAGE")
            .When(x => x.Data != null);

        RuleFor(x => x.Data.PageSize)
            .GreaterThan(0).WithMessage("Page size must be greater than 0").WithErrorCode("INVALID_PAGE_SIZE")
            .LessThanOrEqualTo(100).WithMessage("Page size cannot exceed 100").WithErrorCode("INVALID_PAGE_SIZE")
            .When(x => x.Data != null);

        RuleFor(x => x.Data.MinPrice)
            .GreaterThanOrEqualTo(0).WithMessage("Minimum price cannot be negative").WithErrorCode("NEGATIVE_PRICE")
            .When(x => x.Data != null && x.Data.MinPrice.HasValue);

        RuleFor(x => x.Data.MaxPrice)
            .GreaterThanOrEqualTo(0).WithMessage("Maximum price cannot be negative").WithErrorCode("NEGATIVE_PRICE")
            .When(x => x.Data != null && x.Data.MaxPrice.HasValue);

        RuleFor(x => x.Data)
            .Must(x => !x.MinPrice.HasValue || !x.MaxPrice.HasValue || x.MinPrice <= x.MaxPrice)
            .WithMessage("Minimum price cannot be greater than maximum price").WithErrorCode("INVALID_PRICE_RANGE")
            .When(x => x.Data != null);

        RuleFor(x => x.Data.Category)
            .MaximumLength(50).WithMessage("Category name cannot exceed 50 characters").WithErrorCode("CATEGORY_TOO_LONG")
            .When(x => x.Data != null && !string.IsNullOrEmpty(x.Data.Category));
    }
}