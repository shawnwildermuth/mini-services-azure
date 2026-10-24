// Local development orchestration: `aspire run` (or F5 on this project) starts everything the store needs, wires the
// connection strings, and opens the Aspire dashboard (logs, traces, metrics, health for every resource).
// Production is unaffected: Azure is provisioned from src/infra (Bicep) and the images from the Dockerfiles.
var builder = DistributedApplication.CreateBuilder(args);

// --- Backing services (containers; data volumes keep the database across runs) -------------------------------------

var sql = builder.AddSqlServer("sql")
  .WithDataVolume()
  .WithLifetime(ContainerLifetime.Persistent);
var db = sql.AddDatabase("StoreDb", databaseName: "PartsStore");

var redis = builder.AddRedis("Redis");

// The Service Bus emulator limits queue settings (message TTL up to 1 hour, duplicate detection up to 5 minutes).
var serviceBus = builder.AddAzureServiceBus("servicebus").RunAsEmulator();
foreach (var queue in new[] { "order-placed", "payment-captured" })
{
  serviceBus.AddServiceBusQueue(queue).WithProperties(q =>
  {
    q.DeadLetteringOnMessageExpiration = true;
    q.DefaultMessageTimeToLive = TimeSpan.FromHours(1);
    q.LockDuration = TimeSpan.FromMinutes(1);
    q.MaxDeliveryCount = 5;
    q.RequiresDuplicateDetection = true;
    q.DuplicateDetectionHistoryTimeWindow = TimeSpan.FromMinutes(5);
  });
}

var storage = builder.AddAzureStorage("storage").RunAsEmulator(); // Azurite, for the Functions host

// --- Applications --------------------------------------------------------------------------------------------------

var api = builder.AddProject<Projects.Store_Api>("api")
  .WithReference(db, connectionName: "StoreDb")
  .WithReference(redis, connectionName: "Redis")
  .WithEnvironment("ServiceBus__ConnectionString", serviceBus)
  // The end-to-end suite signs in and registers hundreds of times a minute; the production limits would throttle it.
  .WithEnvironment("RateLimiting__AuthPerMinute", "100000")
  .WithEnvironment("RateLimiting__GlobalPerMinute", "1000000")
  .WaitFor(db)
  .WaitFor(redis)
  .WaitFor(serviceBus)
  .WithHttpHealthCheck("/health/ready");

builder.AddAzureFunctionsProject<Projects.Store_Functions>("functions")
  .WithHostStorage(storage)
  .WithReference(db, connectionName: "StoreDb")
  .WithEnvironment("ServiceBus", serviceBus) // the triggers' connection setting is named "ServiceBus"
  .WaitFor(api) // the API migrates the database at start-up
  .WaitFor(serviceBus);

// The Vite dev servers keep the ports the API's CORS settings, the e2e suite and the docs expect (5173 / 5174).
// Their API address comes from public/env.js (http://localhost:5233, the API's launchSettings port).
builder.AddViteApp("store-web", "../store-web")
  .WithEndpoint("http", e => { e.Port = 5173; e.TargetPort = 5173; e.IsProxied = false; })
  .WaitFor(api);

builder.AddViteApp("admin-web", "../admin-web")
  .WithEndpoint("http", e => { e.Port = 5174; e.TargetPort = 5174; e.IsProxied = false; })
  .WaitFor(api);

builder.Build().Run();
