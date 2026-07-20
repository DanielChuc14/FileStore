using FluentValidation;

namespace FileStore.Application.Features.Auth.Login;

public class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(c => c.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(c => c.Password).NotEmpty().MaximumLength(256);
    }
}
