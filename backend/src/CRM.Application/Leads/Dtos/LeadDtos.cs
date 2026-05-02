using CRM.Domain.Enums;

namespace CRM.Application.Leads.Dtos;

public record LeadDto(
    Guid Id,
    string FirstName,
    string LastName,
    string PhoneNumber,
    string? Email,
    string? State,
    WorkflowStage Stage,
    LeadDisposition Disposition,
    Guid? AssignedUserId,
    Guid? TeamId,
    bool JornayaVerified,
    DateTime CreatedAt);

public record CreateLeadDto(
    string FirstName,
    string LastName,
    string PhoneNumber,
    string? Email,
    string? Address,
    string? City,
    string? State,
    string? PostalCode,
    DateTime? DateOfBirth,
    string? Source,
    string? JornayaLeadId);

public record TransitionLeadDto(
    WorkflowStage ToStage,
    LeadDisposition Disposition,
    string? Notes);
