using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Store.Domain.Messaging;
using Store.Functions.Billing;
using Store.Messaging;

namespace Store.Functions.Functions;

/// <summary>
/// order-placed -> charge the customer -> payment-captured.
/// Failure handling is delegated to Service Bus: an exception abandons the message, it is redelivered
/// (the queue's MaxDeliveryCount, 5 locally) and then dead-lettered. Problems that retrying can never
/// fix are dead-lettered straight away with a reason.
/// </summary>
public class BillingFunction(BillingService billing, IMessagePublisher publisher, ILogger<BillingFunction> logger)
{
  [Function("Billing")]
  public async Task Run(
    [ServiceBusTrigger(Queues.OrderPlaced, Connection = "ServiceBus")] ServiceBusReceivedMessage message,
    ServiceBusMessageActions actions,
    CancellationToken ct)
  {
    OrderPlaced? placed;
    try
    {
      placed = JsonSerializer.Deserialize<OrderPlaced>(message.Body.ToString(), JsonSerializerOptions.Web);
    }
    catch (JsonException)
    {
      placed = null;
    }

    if (placed is null)
    {
      logger.LogError("Dead-lettering message {MessageId}: not a valid OrderPlaced", message.MessageId);
      await actions.DeadLetterMessageAsync(message, deadLetterReason: "InvalidPayload", cancellationToken: ct);
      return;
    }

    var result = await billing.ProcessAsync(placed, ct);

    switch (result.Outcome)
    {
      case BillingOutcome.OrderNotFound:
        await actions.DeadLetterMessageAsync(message, deadLetterReason: "OrderNotFound",
          deadLetterErrorDescription: $"Order {placed.OrderId} does not exist.", cancellationToken: ct);
        break;

      case BillingOutcome.Captured:
        // Sent explicitly (an output binding would not let us control the message id/properties). If the send
        // fails the exception abandons this message; the redelivery finds the order already paid and sends again.
        // The payment id is the message id, so the queue's duplicate detection collapses repeated sends.
        await publisher.PublishAsync(
          Queues.PaymentCaptured, result.Message!.PaymentId, nameof(PaymentCaptured),
          JsonSerializer.Serialize(result.Message, JsonSerializerOptions.Web), ct);
        break;
    }
  }
}
