using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Store.Domain.Messaging;
using Store.Functions.Fulfillment;

namespace Store.Functions.Functions;

/// <summary>payment-captured -> book the carrier, mark the order shipped, consume the reserved stock.</summary>
public class FulfillmentFunction(FulfillmentService fulfillment, ILogger<FulfillmentFunction> logger)
{
  [Function("Fulfillment")]
  public async Task Run(
    [ServiceBusTrigger(Queues.PaymentCaptured, Connection = "ServiceBus")] ServiceBusReceivedMessage message,
    ServiceBusMessageActions actions,
    CancellationToken ct)
  {
    PaymentCaptured? captured;
    try
    {
      captured = JsonSerializer.Deserialize<PaymentCaptured>(message.Body.ToString(), JsonSerializerOptions.Web);
    }
    catch (JsonException)
    {
      captured = null;
    }

    if (captured is null)
    {
      logger.LogError("Dead-lettering message {MessageId}: not a valid PaymentCaptured", message.MessageId);
      await actions.DeadLetterMessageAsync(message, deadLetterReason: "InvalidPayload", cancellationToken: ct);
      return;
    }

    var result = await fulfillment.ProcessAsync(captured, ct);

    if (result.Outcome == FulfillmentOutcome.OrderNotFound)
      await actions.DeadLetterMessageAsync(message, deadLetterReason: "OrderNotFound",
        deadLetterErrorDescription: $"Order {captured.OrderId} does not exist.", cancellationToken: ct);
  }
}
