namespace CRM.Domain.Enums;

/// <summary>An employee's job title. Distinct from their app login <see cref="Roles"/> — an
/// Office Boy has a designation but may hold no system role.</summary>
public enum EmployeeDesignation
{
    Fronter = 0,
    Verifier = 1,
    Closer = 2,
    SubmissionAgent = 3,
    TeamLead = 4,
    HR = 5,
    OfficeBoy = 6,
    Manager = 7,
    Other = 99,
}

public enum Gender
{
    Male = 0,
    Female = 1,
    Other = 2,
}

public enum MaritalStatus
{
    Single = 0,
    Married = 1,
    Divorced = 2,
    Widowed = 3,
}

/// <summary>Where the employee sits in their employment lifecycle.</summary>
public enum EmploymentStatus
{
    Probation = 0,
    Permanent = 1,
    Contract = 2,
    Intern = 3,
    Resigned = 4,
    Terminated = 5,
}

/// <summary>Where a job candidate sits in the hiring pipeline.</summary>
public enum OfferStatus
{
    Applied = 0,
    Interviewed = 1,
    Offered = 2,
    Accepted = 3,
    Declined = 4,
    Rejected = 5,
    OnHold = 6,
    Hired = 7,
}

/// <summary>An employee's attendance for a single day. Drives payroll present/absent/late counts.</summary>
public enum AttendanceStatus
{
    Present = 0,
    Absent = 1,
    Late = 2,
    HalfDay = 3,
    /// <summary>Approved leave.</summary>
    Leave = 4,
    /// <summary>No call, no show.</summary>
    Ncns = 5,
}

/// <summary>Which uploaded image slot on an employee record a file belongs to.</summary>
public enum EmployeeImageKind
{
    Photo = 0,
    IdCardFront = 1,
    IdCardBack = 2,
}

/// <summary>Relationship of the emergency/guardian contact to the employee.</summary>
public enum GuardianRelationship
{
    Father = 0,
    Mother = 1,
    Spouse = 2,
    Brother = 3,
    Sister = 4,
    Son = 5,
    Daughter = 6,
    Guardian = 7,
    Other = 99,
}
