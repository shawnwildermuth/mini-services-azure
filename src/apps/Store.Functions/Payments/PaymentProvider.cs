using System.Security.Cryptography;
using System.Text;

namespace Store.Functions.Payments;

public record PaymentRequest(Guid OrderId, string OrderNumber, decimal Amount, string Currency, string CustomerEmail);

public record PaymentResult(bool Approved, string Reference, string? DeclineReason = null);

/// <summary>The provider is down or timed out. Transient: the message is retried, then dead-lettered.</summary>
public class PaymentProviderUnavailableException(string message) : Exception(message);

/// <summary>
/// Charges a customer. Implementations must treat <see cref="PaymentRequest.OrderId"/> as an
/// idempotency key: charging the same order twice must not take money twice.
/// </summary>
public interface IPaymentProvider
{
  Task<PaymentResult> ChargeAsync(PaymentRequest request, CancellationToken ct);
}

/// <summary>
/// Stand-in for a real provider such as Stripe. Approves everything, except that the customer's
/// email selects a behavior so failure paths can be exercised without code changes:
/// <c>name+decline@...</c> is declined, and <c>name+error@...</c> simulates a provider outage.
/// </summary>
public class MockPaymentProvider : IPaymentProvider
{
  public Task<PaymentResult> ChargeAsync(PaymentRequest request, CancellationToken ct)
  {
    var email = request.CustomerEmail.ToLowerInvariant();

    if (email.Contains("+error"))
      throw new PaymentProviderUnavailableException("The (mock) payment provider is unavailable.");

    // Deterministic per order, like a provider honoring an idempotency key.
    var reference = "mock_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.OrderId.ToString())))[..16].ToLowerInvariant();

    return Task.FromResult(email.Contains("+decline")
      ? new PaymentResult(false, reference, "card_declined")
      : new PaymentResult(true, reference));
  }
}
