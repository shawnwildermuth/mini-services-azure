using Azure.Messaging.ServiceBus;

namespace Store.Messaging;

public record DeadLetteredMessage(
  string Queue, long SequenceNumber, string MessageId, string? Subject, string? Reason, string? Description,
  int DeliveryCount, DateTimeOffset EnqueuedAt, string Body);

/// <summary>Service Bus is not configured, so there is nothing to administer.</summary>
public class ServiceBusNotConfiguredException() : Exception("Service Bus is not configured.");

/// <summary>The message exists but another operation currently holds it. Try again shortly.</summary>
public class DeadLetterBusyException() : Exception("Another operation is working on this message.");

/// <summary>Lets an operator see and act on messages that exhausted their retries.</summary>
public interface IDeadLetterAdmin
{
  Task<IReadOnlyList<DeadLetteredMessage>> PeekAsync(string queue, int max, CancellationToken ct);

  /// <summary>Sends the message back to its queue for another attempt, then removes it from the dead-letter queue.</summary>
  /// <returns>False if the message is no longer there.</returns>
  Task<bool> ReplayAsync(string queue, long sequenceNumber, CancellationToken ct);

  /// <summary>Removes the message from the dead-letter queue without processing it.</summary>
  /// <returns>False if the message is no longer there.</returns>
  Task<bool> DiscardAsync(string queue, long sequenceNumber, CancellationToken ct);
}

public class ServiceBusDeadLetterAdmin(ServiceBusClient client) : IDeadLetterAdmin
{
  private static readonly ServiceBusReceiverOptions DeadLetter = new() { SubQueue = SubQueue.DeadLetter };

  public async Task<IReadOnlyList<DeadLetteredMessage>> PeekAsync(string queue, int max, CancellationToken ct)
  {
    await using var receiver = client.CreateReceiver(queue, DeadLetter);
    var messages = await receiver.PeekMessagesAsync(max, cancellationToken: ct);
    return messages.Select(m => new DeadLetteredMessage(
      queue, m.SequenceNumber, m.MessageId, m.Subject, m.DeadLetterReason, m.DeadLetterErrorDescription,
      m.DeliveryCount, m.EnqueuedTime, m.Body.ToString())).ToList();
  }

  public Task<bool> ReplayAsync(string queue, long sequenceNumber, CancellationToken ct) =>
    SettleAsync(queue, sequenceNumber, async (receiver, message) =>
    {
      // A fresh MessageId: duplicate detection would silently drop a re-send that reuses the original id.
      await using var sender = client.CreateSender(queue);
      await sender.SendMessageAsync(new ServiceBusMessage(message.Body)
      {
        MessageId = Guid.NewGuid().ToString(),
        Subject = message.Subject,
        ContentType = message.ContentType,
        ApplicationProperties = { ["replayedFromMessageId"] = message.MessageId },
      }, ct);
      await receiver.CompleteMessageAsync(message, ct); // only after the re-send succeeded
    }, ct);

  public Task<bool> DiscardAsync(string queue, long sequenceNumber, CancellationToken ct) =>
    SettleAsync(queue, sequenceNumber, (receiver, message) => receiver.CompleteMessageAsync(message, ct), ct);

  // Service Bus can't receive one specific message by sequence number, so lock a batch, act on the match and release the
  // rest. Fine at admin scale (the DLQ should be near-empty), but it means another operator's in-flight action holds a lock
  // that hides "their" message from this batch. So when the message isn't in the batch we retry briefly, and if it still
  // exists (a peek ignores locks) but can't be locked, report it as busy rather than pretending it is gone.
  private async Task<bool> SettleAsync(
    string queue, long sequenceNumber, Func<ServiceBusReceiver, ServiceBusReceivedMessage, Task> act, CancellationToken ct)
  {
    await using var receiver = client.CreateReceiver(queue, DeadLetter);

    for (var attempt = 1; attempt <= 4; attempt++)
    {
      var batch = await receiver.ReceiveMessagesAsync(100, TimeSpan.FromSeconds(2), ct);

      ServiceBusReceivedMessage? target = null;
      foreach (var message in batch)
      {
        if (message.SequenceNumber == sequenceNumber) target = message;
        else await receiver.AbandonMessageAsync(message, cancellationToken: ct);
      }

      if (target is not null)
      {
        await act(receiver, target);
        return true;
      }

      var stillThere = (await receiver.PeekMessagesAsync(1, sequenceNumber, ct)).Any(m => m.SequenceNumber == sequenceNumber);
      if (!stillThere) return false;           // genuinely gone (someone finished first)
      if (attempt < 4) await Task.Delay(TimeSpan.FromMilliseconds(400), ct);
    }

    throw new DeadLetterBusyException();       // exists, but another operation holds it
  }
}
/// <summary>Used when Service Bus isn't configured: reads are empty, actions explain why they can't run.</summary>
public class NullDeadLetterAdmin : IDeadLetterAdmin
{
  public Task<IReadOnlyList<DeadLetteredMessage>> PeekAsync(string queue, int max, CancellationToken ct) =>
    throw new ServiceBusNotConfiguredException();

  public Task<bool> ReplayAsync(string queue, long sequenceNumber, CancellationToken ct) =>
    throw new ServiceBusNotConfiguredException();

  public Task<bool> DiscardAsync(string queue, long sequenceNumber, CancellationToken ct) =>
    throw new ServiceBusNotConfiguredException();
}
