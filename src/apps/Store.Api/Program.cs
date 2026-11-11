using System.Text.Json.Serialization;
using Azure.Extensions.AspNetCore.DataProtection.Keys;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Store.Api;
using Store.Api.Addresses;
using Store.Api.Admin;
using Store.Api.Auth;
using Store.Api.Carts;
using Store.Api.Catalog;
using Store.Api.Health;
using Store.Api.Messaging;
using Store.Api.Orders;
using Store.Api.Security;
using Store.Data;
using Store.Data.Seeding;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults(); // OpenTelemetry + service discovery (the Aspire dashboard in local development)

if (!builder.Environment.IsDevelopment())
  builder.Logging.AddJsonConsole();

builder.Services.AddDbContext<StoreDbContext>(options =>
  options.UseSqlServer(
    builder.Configuration.GetConnectionString("StoreDb"),
    sql =>
    {
      sql.MigrationsAssembly(typeof(StoreDbContext).Assembly.GetName().Name);
      sql.EnableRetryOnFailure(); // Azure SQL drops connections now and then; retry transient failures
    }));

// Token-signing keys live in the database (shared by all instances); in Azure they are additionally wrapped by a
// Key Vault key, so a copy of the table alone is useless.
// (Skipped when the build merely starts the app to write the OpenAPI document: there is no database then, and a failing
// key-ring read at start-up would just bury real errors in the build output.)
var generatingOpenApiDocument = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";
if (!generatingOpenApiDocument)
{
  var dataProtection = builder.Services.AddDataProtection().SetApplicationName("PartsStore").PersistKeysToDbContext<StoreDbContext>();
  if (builder.Configuration["DataProtection:KeyVaultKeyUri"] is { Length: > 0 } keyVaultKey)
    dataProtection.ProtectKeysWithAzureKeyVault(new Uri(keyVaultKey), new DefaultAzureCredential());
}

// Telemetry goes to Application Insights when the platform provides a connection string (it doesn't locally).
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
  builder.Services.AddOpenTelemetry().UseAzureMonitor();

var redis = builder.Configuration.GetConnectionString("Redis");
if (string.IsNullOrEmpty(redis))
  builder.Services.AddDistributedMemoryCache();
else
  builder.Services.AddStackExchangeRedisCache(options =>
  {
    options.Configuration = redis;
    options.InstanceName = "partsstore:";
  });

// The SPAs authenticate with bearer tokens, so CORS needs no credentials.
//var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
  policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var startupLogger = LoggerFactory.Create(logging => logging.AddConsole()).CreateLogger("Startup");

builder.Services.AddCustomerAuth();
builder.Services.AddAdminAuth(builder.Configuration, builder.Environment, startupLogger);
builder.Services.AddApiRateLimiting(builder.Configuration);
builder.Services.AddCatalog(builder.Configuration);
builder.Services.AddCart();
builder.Services.AddOrders();
builder.Services.AddMessaging(builder.Configuration, startupLogger);
// Numbers are plain JSON numbers (not also strings), which keeps the OpenAPI schema and generated clients simple.
builder.Services.ConfigureHttpJsonOptions(options =>
{
  options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
  options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()); // statuses are readable strings
});
builder.Services.AddValidation();
builder.Services.AddProblemDetails();
builder.Services.AddApiSecurity();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks()
  .AddDbContextCheck<StoreDbContext>("database", tags: ["ready"])
  .AddCheck<CacheHealthCheck>("cache", tags: ["ready"]);

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("Database:MigrateAndSeed"))
{
  using var scope = app.Services.CreateScope();
  var db = scope.ServiceProvider.GetRequiredService<StoreDbContext>();
  if (db.Database.IsRelational()) await db.Database.MigrateAsync();
  else await db.Database.EnsureCreatedAsync();
  await CatalogSeeder.SeedAsync(db);
}

app.UseApiSecurity();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
  app.MapOpenApi();

app.MapHealthChecks("/health/live", new() { Predicate = _ => false }).DisableRateLimiting();
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") }).DisableRateLimiting();
app.MapCatalogEndpoints();
app.MapAuthEndpoints();
app.MapCartEndpoints();
app.MapOrderEndpoints();
app.MapAdminEndpoints();
app.MapAddressEndpoints();

app.Run();

public partial class Program;
