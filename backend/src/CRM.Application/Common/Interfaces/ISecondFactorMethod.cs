namespace CRM.Application.Common.Interfaces;

public enum SecondFactorKind { Totp, EmailOtp }

public record SecondFactorEnrollment(SecondFactorKind Kind, string? Secret, string? QrCodeUri);

public interface ISecondFactorMethod
{
    SecondFactorKind Kind { get; }

    Task<SecondFactorEnrollment> BeginEnrollmentAsync(Guid userId, CancellationToken ct = default);
    Task<bool> VerifyAsync(Guid userId, string code, CancellationToken ct = default);
    Task ChallengeAsync(Guid userId, CancellationToken ct = default);
}
