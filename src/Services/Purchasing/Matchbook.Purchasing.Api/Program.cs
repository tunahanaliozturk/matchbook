using Matchbook.BuildingBlocks.Hosting;
using Matchbook.BuildingBlocks.Security;
using Matchbook.Purchasing.Api;
using Matchbook.Purchasing.Infrastructure;
using Matchbook.SharedKernel;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddMatchbookDefaults(ServiceCollectionExtensions.ServiceName);
builder.Services.AddValidation();
builder.Services.AddPurchasingInfrastructure(builder.Configuration);
builder.Services.AddAuthorizationBuilder()
    .AddRolePolicy(Policies.Read, Roles.Buyer, Roles.Receiver, Roles.Auditor)
    .AddRolePolicy(Policies.Buy, Roles.Buyer)
    .AddRolePolicy(Policies.Receive, Roles.Receiver);

WebApplication app = builder.Build();

app.UseMatchbookDefaults();
app.MapPurchaseOrders();

app.Run();

public partial class Program;
