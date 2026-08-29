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
    DateTime CreatedAt,
    /// <summary>
    /// Who owns this lead, resolved for display. Null means it is unclaimed and sitting in a pool.
    /// Ownership was previously only visible on the detail page, so a list gave no way to tell your
    /// leads from anyone else's.
    /// </summary>
    string? AssignedUserName = null);

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
