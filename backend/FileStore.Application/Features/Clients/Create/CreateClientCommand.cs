using FileStore.Application.Features.Clients.Common;
using FluentValidation;
using MediatR;

namespace FileStore.Application.Features.Clients.Create;

public record CreateClientCommand(
    string Email,
    string Name,
    long QuotaBytes,
    int? TrashRetentionDays,
    long? MaxFileSizeBytes) : IRequest<ClientDto>;

public class CreateClientCommandValidator : AbstractValidator<CreateClientCommand>
{
    public CreateClientCommandValidator()
    {
        RuleFor(c => c.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(c => c.Name).NotEmpty().MaximumLength(200);
        RuleFor(c => c.QuotaBytes).GreaterThan(0);
        RuleFor(c => c.TrashRetentionDays).InclusiveBetween(1, 365).When(c => c.TrashRetentionDays.HasValue);
        RuleFor(c => c.MaxFileSizeBytes).GreaterThan(0).When(c => c.MaxFileSizeBytes.HasValue);
    }
}
