using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Store.Api.Admin;
using Store.Api.Messaging;
using Store.Data;
using Store.Messaging;

namespace Store.Api.Tests;

/// <summary>Hosts the API against an in-memory database and cache, seeded with the sample catalog.</summary>
public class ApiFactory : WebApplicationFactory<Program>
{
  private readonly string _dbName = Guid.NewGuid().ToString();

  public TestEmailSender Emails { get; } = new();
  public FakePublisher Publisher { get; } = new();
  public TestClock Clock { get; } = new();
  public FakeDeadLetterAdmin DeadLetters { get; } = new();
  public CapturingLoggerProvider Logs { get; } = new();

  /// <summary>A client carrying an admin access token (roles default to just "Admin").</summary>
  public HttpClient CreateAdminClient(params string[] roles)
  {
    var client = CreateClient();
    var token = Services.GetRequiredService<DevTokenIssuer>().Issue("Test Admin", roles.Length == 0 ? ["Admin"] : roles);
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    return client;
  }

  protected override void ConfigureWebHost(IWebHostBuilder builder)
  {
    builder.UseEnvironment("Development"); // enables migrate/seed on startup
    builder.ConfigureServices(services =>
    {
      // Swap SQL Server for the in-memory provider.
      foreach (var d in services.Where(d =>
                 d.ServiceType == typeof(DbContextOptions<StoreDbContext>) ||
                 d.ServiceType.IsGenericType && d.ServiceType.GetGenericTypeDefinition().Name.StartsWith("IDbContextOptionsConfiguration") &&
                 d.ServiceType.GenericTypeArguments[0] == typeof(StoreDbContext)).ToList())
        services.Remove(d);

      services.AddDbContext<StoreDbContext>(o => o.UseInMemoryDatabase(_dbName));

      // Swap Redis for an in-process cache.
      services.RemoveAll<IDistributedCache>();
      services.AddDistributedMemoryCache();

      // No real Service Bus: tests drive OutboxProcessor by hand against a fake publisher and a controllable clock.
      services.RemoveAll<IHostedService>();
      services.RemoveAll<IMessagePublisher>();
      services.AddSingleton<IMessagePublisher>(Publisher);
      services.RemoveAll<TimeProvider>();
      services.AddSingleton<TimeProvider>(Clock);
      services.AddSingleton<ILoggerProvider>(Logs);
      services.RemoveAll<IDeadLetterAdmin>();
      services.AddSingleton<IDeadLetterAdmin>(DeadLetters);

      // Capture emails instead of logging them, and lift rate limits (tests override them where needed).
      services.RemoveAll<IEmailSender<AppUser>>();
      services.AddSingleton<IEmailSender<AppUser>>(Emails);
      services.Configure<RateLimitSettings>(o =>
      {
        o.GlobalPerMinute = 100_000;
        o.AuthPerMinute = 100_000;
      });
    });
  }

  public const string Password = "Passw0rd!";

  /// <summary>Registers a new unique user, logs in, and returns a client carrying the bearer token.</summary>
  public async Task<AuthenticatedClient> CreateUserAsync()
  {
    var email = $"user-{Guid.NewGuid():N}@example.com";
    var client = CreateClient();

    var register = await client.PostAsJsonAsync("/api/auth/register", new { email, password = Password });
    register.EnsureSuccessStatusCode();

    var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
    login.EnsureSuccessStatusCode();
    var tokens = (await login.Content.ReadFromJsonAsync<TokenResponse>())!;

    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
    return new AuthenticatedClient(client, email, tokens);
  }
}

public record PublishedMessage(string Queue, Guid MessageId, string Type, string Json);

public class FakePublisher : IMessagePublisher
{
  private readonly ConcurrentQueue<PublishedMessage> _published = new();

  public IReadOnlyCollection<PublishedMessage> Published => _published;

  /// <summary>When set, every publish throws it.</summary>
  public Exception? Failure { get; set; }

  public Task PublishAsync(string queue, Guid messageId, string type, string json, CancellationToken ct)
  {
    if (Failure is not null) throw Failure;
    _published.Enqueue(new PublishedMessage(queue, messageId, type, json));
    return Task.CompletedTask;
  }
}

/// <summary>Collects every log line the app writes so tests can assert on what was (and wasn't) logged.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
  public ConcurrentQueue<(string Category, LogLevel Level, string Message)> Entries { get; } = new();

  public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Entries);
  public void Dispose() { }

  private sealed class CapturingLogger(string category, ConcurrentQueue<(string, LogLevel, string)> entries) : ILogger
  {
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
      entries.Enqueue((category, logLevel, formatter(state, exception)));
  }
}

public class FakeDeadLetterAdmin : IDeadLetterAdmin
{
  private readonly List<DeadLetteredMessage> _messages = [];
  public List<DeadLetteredMessage> Replayed { get; } = [];

  /// <summary>A message another operator is working on right now: settling it reports 'busy'.</summary>
  public long? BusySequence { get; set; }

  public void Add(DeadLetteredMessage message) => _messages.Add(message);

  public Task<IReadOnlyList<DeadLetteredMessage>> PeekAsync(string queue, int max, CancellationToken ct) =>
    Task.FromResult<IReadOnlyList<DeadLetteredMessage>>(_messages.Where(m => m.Queue == queue).Take(max).ToList());

  public Task<bool> ReplayAsync(string queue, long sequenceNumber, CancellationToken ct)
  {
    if (sequenceNumber == BusySequence) throw new DeadLetterBusyException();
    var message = _messages.FirstOrDefault(m => m.Queue == queue && m.SequenceNumber == sequenceNumber);
    if (message is null) return Task.FromResult(false);
    _messages.Remove(message);
    Replayed.Add(message);
    return Task.FromResult(true);
  }

  public Task<bool> DiscardAsync(string queue, long sequenceNumber, CancellationToken ct) =>
    sequenceNumber == BusySequence ? throw new DeadLetterBusyException() : Task.FromResult(_messages.RemoveAll(m => m.Queue == queue && m.SequenceNumber == sequenceNumber) > 0);
}

public class TestClock : TimeProvider
{
  private DateTimeOffset _now = DateTimeOffset.UtcNow;
  public override DateTimeOffset GetUtcNow() => _now;
  public void Advance(TimeSpan by) => _now += by;
}

public record TokenResponse(string AccessToken, string RefreshToken);

public record AuthenticatedClient(HttpClient Http, string Email, TokenResponse Tokens);

public class TestEmailSender : IEmailSender<AppUser>
{
  private readonly ConcurrentDictionary<string, string> _resetCodes = new();

  public string? ResetCodeFor(string email) => _resetCodes.GetValueOrDefault(email);

  public Task SendConfirmationLinkAsync(AppUser user, string email, string confirmationLink) => Task.CompletedTask;
  public Task SendPasswordResetLinkAsync(AppUser user, string email, string resetLink) => Task.CompletedTask;

  public Task SendPasswordResetCodeAsync(AppUser user, string email, string resetCode)
  {
    _resetCodes[email] = resetCode;
    return Task.CompletedTask;
  }
}
