using Matchbook.BuildingBlocks.Hosting;
using Matchbook.Payables.Api;
using Matchbook.Payables.Api.Invoices;
using Matchbook.Payables.Api.PaymentRuns;
using Matchbook.Payables.Application;
using Matchbook.Payables.Infrastructure;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddMatchbookDefaults("payables");
builder.Services.AddValidation();
builder.Services.AddPayablesInfrastructure(builder.Configuration);
builder.Services.AddPayablesApplication();
builder.Services.AddPayerAccount(builder.Configuration);
builder.Services.AddAuthorizationBuilder().AddPayablesPolicies();

WebApplication app = builder.Build();

app.UseMatchbookDefaults();
app.MapInvoices();
app.MapPaymentRuns();

await app.RunAsync();

public partial class Program;
