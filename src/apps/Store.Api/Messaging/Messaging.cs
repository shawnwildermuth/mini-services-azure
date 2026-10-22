using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Store.Data;
using Store.Messaging;

namespace Store.Api.Messaging;

public class OutboxSettings
{
  public int PollIntervalSeconds { get; set; } = 2;
  public int BatchSize { get; set; } = 20;
}

public class ServiceBusSettings
{
  /// <summary>Connection string; used for local development (the emulator) or when no identity is available.</summary>
  public string? ConnectionString { get; set; }
  /// <summary>e.g. myns.servicebus.windows.net. Used with the app's managed identity in Azure.</summary>
  public string? FullyQualifiedNamespace { get; set; }

  public bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionString) || !string.IsNullOrWhiteSpace(FullyQualifiedNamespace);
}

/// <summary>Publishes pending outbox rows. Separate from the hosted service so it can be driven directly in tests.</summary>
public class OutboxProcessor(
  StoreDbContext db, IMessagePublisher publisher, IOptions<OutboxSettings> options, TimeProvider clock, ILogger<OutboxProcessor> logger)
{
  /// <returns>How many messages were published.</returns>
  public async Task<int> ProcessBatchAsync(CancellationToken ct)
  {
    var now = clock.GetUtcNow();
    var due = await db.OutboxMessages
      .Where(m => m.ProcessedAt == null && m.NextAttemptAt <= now)
      .OrderBy(m => m.CreatedAt)
      .Take(options.Value.BatchSize)
      .ToListAsync(ct);

    var published = 0;
    foreach (var message in due)
    {
      try
      {
        await publisher.PublishAsync(message.Destination, message.Id, message.Type, message.Payload, ct);
        message.ProcessedAt = clock.GetUtcNow();
        message.LastError = null;
        published++;
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        // Back off exponentially (2s, 4s, ... capped at 5 minutes) and try again later.
        message.Attempts++;
        message.LastError = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
        message.NextAttemptAt = now.AddSeconds(Math.Min(Math.Pow(2, message.Attempts), 300));
        logger.LogWarning(ex, "Publishing outbox message {MessageId} to {Queue} failed (attempt {Attempts})",
          message.Id, message.Destination, message.Attempts);
      }

      // Save per message so a crash can only ever repeat the one in flight (which duplicate detection drops).
      await db.SaveChangesAsync(ct);
    }
    return published;
  }
}

/// <summary>Polls the outbox. Runs only when Service Bus is configured.</summary>
public class OutboxDispatcherService(IServiceScopeFactory scopes, IOptions<OutboxSettings> options, ILogger<OutboxDispatcherService> logger)
  : BackgroundService
{
  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    var interval = TimeSpan.FromSeconds(Math.Max(options.Value.PollIntervalSeconds, 1));
    using var timer = new PeriodicTimer(interval);

    do
    {
      try
      {
        // Keep draining while there is a full batch waiting, then go back to sleep.
        int published;
        do
        {
          await using var scope = scopes.CreateAsyncScope();
          published = await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessBatchAsync(stoppingToken);
        } while (published >= options.Value.BatchSize && !stoppingToken.IsCancellationRequested);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        logger.LogError(ex, "Outbox dispatch cycle failed");
      }
    } while (await WaitAsync(timer, stoppingToken));
  }

  private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
  {
    try { return await timer.WaitForNextTickAsync(ct); }
    catch (OperationCanceledException) { return false; }
  }
}

public static class MessagingExtensions
{
  public static IServiceCollection AddMessaging(this IServiceCollection services, IConfiguration configuration, ILogger logger)
  {
    services.Configure<OutboxSettings>(configuration.GetSection("Outbox"));
    services.Configure<ServiceBusSettings>(configuration.GetSection("ServiceBus"));
    services.AddScoped<OutboxProcessor>();

    var settings = configuration.GetSection("ServiceBus").Get<ServiceBusSettings>() ?? new();
    if (settings.IsConfigured)
    {
      // One client (one connection) shared by publishing and dead-letter administration; the container disposes it.
      services.AddSingleton(_ => ServiceBusClients.Create(settings.ConnectionString, settings.FullyQualifiedNamespace));
      services.AddSingleton<IMessagePublisher>(sp => new ServiceBusPublisher(sp.GetRequiredService<ServiceBusClient>()));
      services.AddSingleton<IDeadLetterAdmin>(sp => new ServiceBusDeadLetterAdmin(sp.GetRequiredService<ServiceBusClient>()));
      services.AddHostedService<OutboxDispatcherService>();
    }
    else
    {
      services.AddSingleton<IDeadLetterAdmin, NullDeadLetterAdmin>();
      logger.LogWarning("ServiceBus is not configured; orders will queue in the outbox but nothing will be published.");
    }
    return services;
  }
}
