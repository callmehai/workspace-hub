using FluentValidation;
using WorkspaceHub.Application.DTOs.Contacts;

namespace WorkspaceHub.Application.Validators;

public class ContactProfileValidator : AbstractValidator<ContactProfileDto>
{
    public ContactProfileValidator()
    {
        RuleFor(x => x.GivenName).MaximumLength(128);
        RuleFor(x => x.FamilyName).MaximumLength(128);

        RuleForEach(x => x.Emails).ChildRules(email =>
        {
            email.RuleFor(e => e.Value).NotEmpty().EmailAddress().MaximumLength(320);
            email.RuleFor(e => e.Label).MaximumLength(32);
        });

        RuleForEach(x => x.Phones).ChildRules(phone =>
        {
            phone.RuleFor(p => p.Value).NotEmpty().MaximumLength(32);
            phone.RuleFor(p => p.Label).MaximumLength(32);
        });

        RuleFor(x => x.Birthday).ChildRules(b =>
        {
            b.RuleFor(x => x!.Month).InclusiveBetween(1, 12).When(x => x!.Month.HasValue);
            b.RuleFor(x => x!.Day).InclusiveBetween(1, 31).When(x => x!.Day.HasValue);
            b.RuleFor(x => x!.Year).InclusiveBetween(1, 9999).When(x => x!.Year.HasValue);
        }).When(x => x.Birthday != null);

        RuleFor(x => x.Organization).ChildRules(o =>
        {
            o.RuleFor(x => x!.Name).MaximumLength(256);
            o.RuleFor(x => x!.Title).MaximumLength(256);
        }).When(x => x.Organization != null);
    }
}
