using Matchbook.Budgets.Api;
using Matchbook.Budgets.Api.Features.Budgets;
using Matchbook.Budgets.Api.Features.CostCentres;
using Matchbook.Budgets.Application;
using Matchbook.Budgets.Infrastructure;
using Matchbook.BuildingBlocks.Hosting;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddMatchbookDefaults("budgets");
builder.Services.AddValidation();
builder.Services.AddBudgetsApplication().AddHandlersFrom(typeof(IBudgetsDb).Assembly);
builder.Services.AddBudgetsInfrastructure(builder.Configuration);
builder.Services.AddAuthorizationBuilder().AddBudgetsPolicies();

WebApplication app = builder.Build();

app.UseMatchbookDefaults();
app.MapCostCentresEndpoints();
app.MapBudgetsEndpoints();

app.Run();

public partial class Program;
