using Store.Domain.Orders;

namespace Store.Functions.Notifications;

/// <summary>Tells customers what happened to their order.</summary>
public interface ICustomerNotifier
{
  Task PaymentDeclinedAsync(Order order, string? reason, CancellationToken ct);
  Task OrderShippedAsync(Order order, Shipment shipment, CancellationToken ct);
}

/// <summary>Placeholder until an email provider exists: records that a message would be sent. Contains no personal data.</summary>
public class LoggingCustomerNotifier(ILogger<LoggingCustomerNotifier> logger) : ICustomerNotifier
{
  public Task PaymentDeclinedAsync(Order order, string? reason, CancellationToken ct)
  {
    logger.LogInformation("[email stub] payment declined notice for order {OrderNumber} ({Reason})", order.OrderNumber, reason);
    return Task.CompletedTask;
  }

  public Task OrderShippedAsync(Order order, Shipment shipment, CancellationToken ct)
  {
    logger.LogInformation("[email stub] shipped notice for order {OrderNumber} via {Carrier} {Tracking}",
      order.OrderNumber, shipment.Carrier, shipment.TrackingNumber);
    return Task.CompletedTask;
  }
}
