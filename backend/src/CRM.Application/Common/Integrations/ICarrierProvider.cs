namespace CRM.Application.Common.Integrations;

public record CarrierApplicationRequest(
    string FirstName, string LastName, string Email, string Phone, DateTime DateOfBirth,
    decimal MonthlyPremium, IDictionary<string, object> AdditionalFields);

public record CarrierApplicationResult(
    bool Accepted, string? PolicyNumber, string? CarrierReferenceId,
    string Status, string? Reason, DateTime SubmittedAt);

public interface ICarrierProvider
{
    string CarrierCode { get; }
    Task<CarrierApplicationResult> SubmitApplicationAsync(CarrierApplicationRequest request, CancellationToken ct = default);
    Task<CarrierApplicationResult> GetStatusAsync(string carrierReferenceId, CancellationToken ct = default);
}

public interface ICarrierRegistry
{
    ICarrierProvider Get(string carrierCode);
    IReadOnlyList<string> AvailableCarriers { get; }
}
