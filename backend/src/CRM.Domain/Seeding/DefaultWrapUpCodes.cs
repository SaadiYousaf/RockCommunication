using CRM.Domain.Entities;

namespace CRM.Domain.Seeding;

/// <summary>
/// The baseline set of call wrap-up codes (dispositions) every agency needs to be
/// operational — an agent can't complete a call without one. Seeded when an agency
/// is created and backfilled on startup, so no agency is ever left with an empty
/// Disposition picker.
/// </summary>
public static class DefaultWrapUpCodes
{
    public static IReadOnlyList<WrapUpCode> For(Guid agencyId) => new[]
    {
        new WrapUpCode { AgencyId = agencyId, Code = "SALE",       Label = "Closed sale",       IsSale = true,  IsContact = true,  IsRetry = false, IsActive = true },
        new WrapUpCode { AgencyId = agencyId, Code = "INTERESTED", Label = "Interested",        IsSale = false, IsContact = true,  IsRetry = true,  IsActive = true },
        new WrapUpCode { AgencyId = agencyId, Code = "NOT_INT",    Label = "Not interested",    IsSale = false, IsContact = true,  IsRetry = false, IsActive = true },
        new WrapUpCode { AgencyId = agencyId, Code = "CALLBACK",   Label = "Schedule callback", IsSale = false, IsContact = true,  IsRetry = true,  IsActive = true },
        new WrapUpCode { AgencyId = agencyId, Code = "VM",         Label = "Voicemail left",    IsSale = false, IsContact = false, IsRetry = true,  IsActive = true },
        new WrapUpCode { AgencyId = agencyId, Code = "NO_ANS",     Label = "No answer",         IsSale = false, IsContact = false, IsRetry = true,  IsActive = true },
        new WrapUpCode { AgencyId = agencyId, Code = "WRONG_NUM",  Label = "Wrong number",      IsSale = false, IsContact = false, IsRetry = false, IsActive = true },
        new WrapUpCode { AgencyId = agencyId, Code = "DNC",        Label = "Do not call",       IsSale = false, IsContact = true,  IsRetry = false, IsActive = true },
    };
}
