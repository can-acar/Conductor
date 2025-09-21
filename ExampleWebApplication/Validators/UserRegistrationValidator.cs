using Conductor.Validation;

namespace ExampleWebApplication.Validators;

// Example model for demonstration
public class UserRegistration
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
    public int Age { get; set; }
    public string Website { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public string PreferredLanguage { get; set; } = string.Empty;
    public bool AcceptTerms { get; set; }
    public DateTime? BirthDate { get; set; }
}

public enum UserRole
{
    User = 0,
    Admin = 1,
    Moderator = 2
}

// Comprehensive validator demonstrating all fluent validation features
public class UserRegistrationValidator : AbstractValidator<UserRegistration>
{
    public UserRegistrationValidator()
    {
        // Basic string validations
        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required").WithErrorCode("FIRST_NAME_REQUIRED")
            .Length(2, 50).WithMessage("First name must be between 2 and 50 characters").WithErrorCode("FIRST_NAME_LENGTH");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Last name is required").WithErrorCode("LAST_NAME_REQUIRED")
            .Length(2, 50).WithMessage("Last name must be between 2 and 50 characters").WithErrorCode("LAST_NAME_LENGTH");

        // Email validation using extension method
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required").WithErrorCode("EMAIL_REQUIRED")
            .EmailAddress();

        // Phone number validation using extension method
        RuleFor(x => x.PhoneNumber)
            .PhoneNumber()
            .When(x => !string.IsNullOrEmpty(x.PhoneNumber));

        // Password validation with custom rules
        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required").WithErrorCode("PASSWORD_REQUIRED")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters long").WithErrorCode("PASSWORD_TOO_SHORT")
            .Must(HaveUpperCaseLetter).WithMessage("Password must contain at least one uppercase letter").WithErrorCode("PASSWORD_NO_UPPERCASE")
            .Must(HaveLowerCaseLetter).WithMessage("Password must contain at least one lowercase letter").WithErrorCode("PASSWORD_NO_LOWERCASE")
            .Must(HaveDigit).WithMessage("Password must contain at least one digit").WithErrorCode("PASSWORD_NO_DIGIT")
            .Must(HaveSpecialCharacter).WithMessage("Password must contain at least one special character").WithErrorCode("PASSWORD_NO_SPECIAL");

        // Confirm password validation
        RuleFor(x => x.ConfirmPassword)
            .Equal(null).WithMessage("Passwords do not match").WithErrorCode("PASSWORD_MISMATCH")
            .When(x => !string.IsNullOrEmpty(x.Password));

        // Numeric validations
        RuleFor(x => x.Age)
            .GreaterThanOrEqualTo(13).WithMessage("User must be at least 13 years old").WithErrorCode("AGE_TOO_YOUNG")
            .LessThan(120).WithMessage("Age must be realistic").WithErrorCode("AGE_TOO_OLD");

        // URL validation using extension method
        RuleFor(x => x.Website)
            .Url()
            .When(x => !string.IsNullOrEmpty(x.Website));

        // Enum validation using extension method
        RuleFor(x => x.Role)
            .IsInEnum().WithMessage("Invalid user role").WithErrorCode("INVALID_ROLE");

        // Choice validation using extension method
        RuleFor(x => x.PreferredLanguage)
            .IsIn("en", "tr", "fr", "de", "es").WithMessage("Preferred language must be one of: en, tr, fr, de, es").WithErrorCode("INVALID_LANGUAGE")
            .When(x => !string.IsNullOrEmpty(x.PreferredLanguage));

        // Boolean validation
        RuleFor(x => x.AcceptTerms)
            .Equal(true).WithMessage("You must accept the terms and conditions").WithErrorCode("TERMS_NOT_ACCEPTED");

        // Date validation with custom logic
        RuleFor(x => x.BirthDate)
            .Must(BeValidBirthDate).WithMessage("Birth date must be a valid date in the past").WithErrorCode("INVALID_BIRTH_DATE")
            .When(x => x.BirthDate.HasValue);

        // Cross-property validation
        RuleFor(x => x)
            .Must(HaveConsistentAgeAndBirthDate).WithMessage("Age and birth date are inconsistent").WithErrorCode("AGE_BIRTHDATE_MISMATCH")
            .When(x => x.BirthDate.HasValue);

        // Async validation example
        RuleFor(x => x.Email)
            .MustAsync(async (email, cancellation) => await BeUniqueEmailAsync(email, cancellation))
            .WithMessage("Email address is already registered").WithErrorCode("EMAIL_DUPLICATE")
            .When(x => !string.IsNullOrEmpty(x.Email));

        // Conditional validation using Unless
        RuleFor(x => x.PhoneNumber)
            .NotEmpty().WithMessage("Phone number is required for admin users").WithErrorCode("ADMIN_PHONE_REQUIRED")
            .Unless(x => x.Role != UserRole.Admin);
    }

    private bool HaveUpperCaseLetter(string password)
    {
        return password.Any(char.IsUpper);
    }

    private bool HaveLowerCaseLetter(string password)
    {
        return password.Any(char.IsLower);
    }

    private bool HaveDigit(string password)
    {
        return password.Any(char.IsDigit);
    }

    private bool HaveSpecialCharacter(string password)
    {
        var specialChars = "!@#$%^&*()_+-=[]{}|;:,.<>?";
        return password.Any(specialChars.Contains);
    }

    private bool BeValidBirthDate(DateTime? birthDate)
    {
        if (!birthDate.HasValue) return true;

        return birthDate.Value < DateTime.Today && birthDate.Value > DateTime.Today.AddYears(-120);
    }

    private bool HaveConsistentAgeAndBirthDate(UserRegistration user)
    {
        if (!user.BirthDate.HasValue) return true;

        var calculatedAge = DateTime.Today.Year - user.BirthDate.Value.Year;
        if (user.BirthDate.Value.Date > DateTime.Today.AddYears(-calculatedAge))
            calculatedAge--;

        return Math.Abs(calculatedAge - user.Age) <= 1; // Allow 1 year tolerance
    }

    private async Task<bool> BeUniqueEmailAsync(string email, CancellationToken cancellationToken)
    {
        // Simulate async database check
        await Task.Delay(50, cancellationToken);

        // In a real application, this would check against a database
        // For demonstration, we'll simulate some existing emails
        var existingEmails = new[] { "admin@test.com", "user@test.com", "test@example.com" };
        return !existingEmails.Contains(email.ToLowerInvariant());
    }
}