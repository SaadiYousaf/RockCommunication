using CRM.Application.Common.Exceptions;
using CRM.Application.Common.Interfaces;
using CRM.Application.Common.Workflow;
using CRM.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace CRM.Application.Workflows;

public record WorkflowActionDto(Guid Id, string ActionType, string? ParametersJson, int Order);

public record WorkflowRuleDto(Guid Id, string Name, string EventType, string? ConditionJson,
    int Priority, bool IsActive, bool ContinueOnError, string? Description,
    IReadOnlyList<WorkflowActionDto> Actions);

public record WorkflowExecutionDto(Guid Id, Guid RuleId, string EventType, string Status,
    DateTime StartedAt, DateTime? CompletedAt, string? Error);

public record ListWorkflowRulesQuery(string? EventType = null) : IRequest<IReadOnlyList<WorkflowRuleDto>>;
public record GetWorkflowRuleQuery(Guid Id) : IRequest<WorkflowRuleDto>;
public record DeleteWorkflowRuleCommand(Guid Id) : IRequest<Unit>;

public record UpsertWorkflowRuleDto(Guid? Id, string Name, string EventType, string? ConditionJson,
    int Priority, bool IsActive, bool ContinueOnError, string? Description,
    IReadOnlyList<UpsertWorkflowActionDto> Actions);

public record UpsertWorkflowActionDto(string ActionType, string? ParametersJson, int Order);
public record UpsertWorkflowRuleCommand(UpsertWorkflowRuleDto Input) : IRequest<WorkflowRuleDto>;

public class UpsertWorkflowRuleValidator : AbstractValidator<UpsertWorkflowRuleCommand>
{
    public UpsertWorkflowRuleValidator()
    {
        RuleFor(x => x.Input.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Input.EventType).NotEmpty().MaximumLength(60);
    }
}

public record AvailableEventTypesQuery() : IRequest<IReadOnlyList<string>>;
public record AvailableActionTypesQuery() : IRequest<IReadOnlyList<string>>;

public record ListExecutionsQuery(Guid? RuleId, int Take = 50) : IRequest<IReadOnlyList<WorkflowExecutionDto>>;

public class WorkflowAdminHandler :
    IRequestHandler<ListWorkflowRulesQuery, IReadOnlyList<WorkflowRuleDto>>,
    IRequestHandler<GetWorkflowRuleQuery, WorkflowRuleDto>,
    IRequestHandler<UpsertWorkflowRuleCommand, WorkflowRuleDto>,
    IRequestHandler<DeleteWorkflowRuleCommand, Unit>,
    IRequestHandler<AvailableEventTypesQuery, IReadOnlyList<string>>,
    IRequestHandler<AvailableActionTypesQuery, IReadOnlyList<string>>,
    IRequestHandler<ListExecutionsQuery, IReadOnlyList<WorkflowExecutionDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IWorkflowActionRegistry _actions;

    public WorkflowAdminHandler(IApplicationDbContext db, ICurrentUser user, IWorkflowActionRegistry actions)
    {
        _db = db; _user = user; _actions = actions;
    }

    public async Task<IReadOnlyList<WorkflowRuleDto>> Handle(ListWorkflowRulesQuery request, CancellationToken ct)
    {
        EnsureManager();
        var q = _db.WorkflowRules.Include(r => r.Actions).Where(r => r.AgencyId == _user.AgencyId);
        if (!string.IsNullOrEmpty(request.EventType)) q = q.Where(r => r.EventType == request.EventType);
        return await q.OrderBy(r => r.Priority).Select(r => Map(r)).ToListAsync(ct);
    }

    public async Task<WorkflowRuleDto> Handle(GetWorkflowRuleQuery request, CancellationToken ct)
    {
        EnsureManager();
        var r = await _db.WorkflowRules.Include(x => x.Actions)
            .FirstOrDefaultAsync(x => x.Id == request.Id && x.AgencyId == _user.AgencyId, ct)
            ?? throw new NotFoundException(nameof(WorkflowRule), request.Id);
        return Map(r);
    }

    public async Task<WorkflowRuleDto> Handle(UpsertWorkflowRuleCommand request, CancellationToken ct)
    {
        EnsureManager();
        var input = request.Input;
        WorkflowRule r;
        if (input.Id is { } id)
        {
            r = await _db.WorkflowRules.Include(x => x.Actions)
                .FirstOrDefaultAsync(x => x.Id == id && x.AgencyId == _user.AgencyId, ct)
                ?? throw new NotFoundException(nameof(WorkflowRule), id);
            _db.WorkflowActions.RemoveRange(r.Actions);
            r.Actions.Clear();
        }
        else
        {
            r = new WorkflowRule { AgencyId = _user.AgencyId!.Value };
            _db.WorkflowRules.Add(r);
        }
        r.Name = input.Name.Trim();
        r.EventType = input.EventType.Trim();
        r.ConditionJson = input.ConditionJson;
        r.Priority = input.Priority;
        r.IsActive = input.IsActive;
        r.ContinueOnError = input.ContinueOnError;
        r.Description = input.Description;

        foreach (var a in input.Actions.OrderBy(x => x.Order))
        {
            r.Actions.Add(new WorkflowAction
            {
                AgencyId = r.AgencyId,
                ActionType = a.ActionType,
                ParametersJson = a.ParametersJson,
                Order = a.Order
            });
        }
        await _db.SaveChangesAsync(ct);
        return Map(r);
    }

    public async Task<Unit> Handle(DeleteWorkflowRuleCommand request, CancellationToken ct)
    {
        EnsureManager();
        var r = await _db.WorkflowRules.FirstOrDefaultAsync(x => x.Id == request.Id && x.AgencyId == _user.AgencyId, ct)
            ?? throw new NotFoundException(nameof(WorkflowRule), request.Id);
        _db.WorkflowRules.Remove(r);
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }

    public Task<IReadOnlyList<string>> Handle(AvailableEventTypesQuery request, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(new[]
        {
            WorkflowEventTypes.LeadCreated, WorkflowEventTypes.LeadStageChanged,
            WorkflowEventTypes.CallCompleted, WorkflowEventTypes.SaleClosed,
            WorkflowEventTypes.SaleValidated, WorkflowEventTypes.SaleFunded,
            WorkflowEventTypes.CallbackDue
        });

    public Task<IReadOnlyList<string>> Handle(AvailableActionTypesQuery request, CancellationToken ct)
    {
        EnsureManager();
        return Task.FromResult(_actions.AvailableActions);
    }

    public async Task<IReadOnlyList<WorkflowExecutionDto>> Handle(ListExecutionsQuery request, CancellationToken ct)
    {
        EnsureManager();
        var q = _db.WorkflowExecutions.Where(e => e.AgencyId == _user.AgencyId);
        if (request.RuleId is { } rid) q = q.Where(e => e.RuleId == rid);
        return await q.OrderByDescending(e => e.StartedAt).Take(Math.Min(request.Take, 200))
            .Select(e => new WorkflowExecutionDto(e.Id, e.RuleId, e.EventType, e.Status,
                e.StartedAt, e.CompletedAt, e.Error))
            .ToListAsync(ct);
    }

    private void EnsureManager()
    {
        if (_user.AgencyId is null) throw new ForbiddenAccessException();
        if (!_user.Roles.Contains("Admin") && !_user.Roles.Contains("ProgramManager"))
            throw new ForbiddenAccessException();
    }

    private static WorkflowRuleDto Map(WorkflowRule r) => new(r.Id, r.Name, r.EventType,
        r.ConditionJson, r.Priority, r.IsActive, r.ContinueOnError, r.Description,
        r.Actions.OrderBy(a => a.Order).Select(a => new WorkflowActionDto(a.Id, a.ActionType, a.ParametersJson, a.Order)).ToList());
}
