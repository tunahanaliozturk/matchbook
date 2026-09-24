using Matchbook.BuildingBlocks.Hosting;
using Matchbook.Suppliers.Api;
using Matchbook.Suppliers.Application;
using Matchbook.Suppliers.Infrastructure;
using Microsoft.AspNetCore.OpenApi;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddMatchbookDefaults("suppliers");
builder.Services.AddValidation();
builder.Services.Configure<OpenApiOptions>("v1", static options => options.DescribeProblemCodes());
builder.Services.AddAuthorizationBuilder().AddSupplierPolicies();
builder.Services.AddSuppliersApplication();
builder.Services.AddSuppliersInfrastructure(builder.Configuration);

WebApplication app = builder.Build();

app.UseMatchbookDefaults();
app.MapSupplierEndpoints();

await app.RunAsync();

public partial class Program;
