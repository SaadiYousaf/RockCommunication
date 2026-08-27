using CRM.Application.Common.Commission;
using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Domain.Common;
using FluentValidation;
using MediatR;

namespace CRM.Application.Admin;

public record AgencyCommissionRuleDto(string RuleName, decimal? Amount, decimal? Threshold, bool Enabled);

public record ListCommissionConfigQuery() : IRequest<IReadOnlyList<AgencyCommissionRuleDto>>;
public record UpsertCommissionConfigCommand(AgencyCommissionRuleDto Input) : IRequest<AgencyCommissionRuleDto>;

public class UpsertCommissionConfigValidator : AbstractValidator<UpsertCommissionConfigCommand>
{
    public UpsertCommissionConfigValidator()
    {
        RuleFor(x => x.Input.RuleName).NotEmpty();
        RuleFor(x => x.Input.Amount).GreaterThanOrEqualTo(0).When(x => x.Input.Amount.HasValue);
        // A rule pays this amount on every qualifying sale, agency-wide — cap it so a typo can't
        // mint payouts. Threshold had NO rule at all: a negative one made a gated rule fire on
        // every sale.
        RuleFor(x => x.Input.Amount).LessThanOrEqualTo(1_000_000m)
            .When(x => x.Input.Amount.HasValue)
            .WithMessage("That commission amount looks too large — check the figure.");
        RuleFor(x => x.Input.Threshold).InclusiveBetween(0m, 10_000_000m)
            .When(x => x.Input.Threshold.HasValue)
            .WithMessage("The threshold must be zero or more.");
    }
}

public class CommissionConfigHandler :
    IRequestHandler<ListCommissionConfigQuery, IReadOnlyList<AgencyCommissionRuleDto>>,
    IRequestHandler<UpsertCommissionConfigCommand, AgencyCommissionRuleDto>
{
    private readonly IAgencyCommissionConfigProvider _config;
    private readonly ICurrentUser _user;

    public CommissionConfigHandler(IAgencyCommissionConfigProvider config, ICurrentUser user)
    {
        _config = Guard.AgainstNull(config);
        _user = Guard.AgainstNull(user);
    }

    public async Task<IReadOnlyList<AgencyCommissionRuleDto>> Handle(ListCommissionConfigQuery request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureAdmin();
        var rules = await _config.GetAllAsync(_user.AgencyId!.Value, ct);
        return rules.Select(r => new AgencyCommissionRuleDto(r.RuleName, r.Amount, r.Threshold, r.Enabled)).ToList();
    }

    public async Task<AgencyCommissionRuleDto> Handle(UpsertCommissionConfigCommand request, CancellationToken ct)
    {
        Guard.AgainstNull(request);
        EnsureAdmin();
        var input = request.Input;
        await _config.UpsertAsync(_user.AgencyId!.Value,
            new AgencyCommissionRule(input.RuleName, input.Amount, input.Threshold, input.Enabled), ct);
        return input;
    }

    // Permission enforcement is handled at the controller via [HasPermission].
    // This backstop only checks that we have a tenant context.
    private void EnsureAdmin()
    {
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
    }
}
