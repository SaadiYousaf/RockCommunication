using CRM.Application.Leads.Commands;
using CRM.Domain.Enums;
using Xunit;

namespace CRM.Application.Tests;

public class WorkflowTransitionTests
{
    [Theory]
    [InlineData(WorkflowStage.New, WorkflowStage.Fronted, true)]
    [InlineData(WorkflowStage.Fronted, WorkflowStage.Verified, true)]
    [InlineData(WorkflowStage.Verified, WorkflowStage.Closed, true)]
    [InlineData(WorkflowStage.Closed, WorkflowStage.Validated, true)]
    [InlineData(WorkflowStage.Validated, WorkflowStage.Funded, true)]
    [InlineData(WorkflowStage.Funded, WorkflowStage.Followup, true)]
    [InlineData(WorkflowStage.Lost, WorkflowStage.Winback, true)]
    [InlineData(WorkflowStage.New, WorkflowStage.Closed, false)]
    [InlineData(WorkflowStage.New, WorkflowStage.Validated, false)]
    [InlineData(WorkflowStage.Funded, WorkflowStage.New, false)]
    public void CanTransition_Honors_StateMachine(WorkflowStage from, WorkflowStage to, bool expected)
    {
        Assert.Equal(expected, TransitionLeadHandler.CanTransition(from, to));
    }
}
