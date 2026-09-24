using Matchbook.BuildingBlocks.Hosting;
using Matchbook.Payables.Api;
using Matchbook.Payables.Api.Features.Invoices;
using Matchbook.Payables.Api.Features.PaymentRuns;
using Matchbook.Payables.Application;
using Matchbook.Payables.Infrastructure;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddMatchbookDefaults("payables");
builder.Services.AddValidation();
builder.Services.AddPayablesInfrastructure(builder.Configuration);
builder.Services.AddPayablesApplication().AddHandlersFrom(typeof(IPayablesDb).Assembly);
builder.Services.AddPayerAccount(builder.Configuration);
builder.Services.AddAuthorizationBuilder().AddPayablesPolicies();

WebApplication app = builder.Build();

app.UseMatchbookDefaults();
app.MapInvoicesEndpoints();
app.MapPaymentRunsEndpoints();

await app.RunAsync();

public partial class Program;
