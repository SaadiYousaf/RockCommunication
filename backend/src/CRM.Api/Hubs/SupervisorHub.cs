using CRM.Application.CallCenter;
using CRM.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace CRM.Api.Hubs;

[Authorize(Roles = $"{Roles.Admin},{Roles.ProgramManager},{Roles.TeamLead}")]
public class SupervisorHub : Hub
{
    private readonly IMediator _mediator;
    public SupervisorHub(IMediator mediator) => _mediator = mediator;

    public Task<IReadOnlyList<LiveAgentDto>> Snapshot() =>
        _mediator.Send(new LiveAgentBoardQuery());
}
