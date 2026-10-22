using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Store.Data;
using Store.Functions.Billing;
using Store.Functions.Fulfillment;
using Store.Functions.Notifications;
using Store.Functions.Payments;
using Store.Functions.Shipping;
using Store.Messaging;

var builder = FunctionsApplication.CreateBuilder(args);

builder.Services.AddDbContext<StoreDbContext>(options =>
  options.UseSqlServer(
    builder.Configuration.GetConnectionString("StoreDb"),
    sql => sql.EnableRetryOnFailure()));

// Telemetry goes to Application Insights when the platform provides a connection string (it doesn't locally).
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
  builder.Services.AddApplicationInsightsTelemetryWorkerService();
  builder.Services.ConfigureFunctionsApplicationInsights();
}

builder.Services.AddSingleton(TimeProvider.System);

// The triggers read the "ServiceBus" app setting themselves. Sending uses the same setting: a connection string
// locally (the emulator), or "ServiceBus__fullyQualifiedNamespace" with managed identity in Azure.
builder.Services.AddSingleton<IMessagePublisher>(_ => new ServiceBusPublisher(ServiceBusClients.Create(
  builder.Configuration["ServiceBus"],
  builder.Configuration["ServiceBus:fullyQualifiedNamespace"])));

// Swap these for real integrations (Stripe, UPS/DHL APIs, an email service) without touching the functions.
builder.Services.AddSingleton<IPaymentProvider, MockPaymentProvider>();
builder.Services.AddSingleton<IShippingCarrier, SimulatedUps>();
builder.Services.AddSingleton<IShippingCarrier, SimulatedDhl>();
builder.Services.AddSingleton<ICustomerNotifier, LoggingCustomerNotifier>();

builder.Services.AddScoped<BillingService>();
builder.Services.AddScoped<FulfillmentService>();

builder.Build().Run();
