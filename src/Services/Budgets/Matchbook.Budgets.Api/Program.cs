using Matchbook.Budgets.Api;
using Matchbook.Budgets.Application;
using Matchbook.Budgets.Infrastructure;
using Matchbook.BuildingBlocks.Hosting;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddMatchbookDefaults("budgets");
builder.Services.AddValidation();
builder.Services.AddBudgetsApplication();
builder.Services.AddBudgetsInfrastructure(builder.Configuration);
builder.Services.AddAuthorizationBuilder().AddBudgetsPolicies();

WebApplication app = builder.Build();

app.UseMatchbookDefaults();
app.MapCostCentres();
app.MapBudgets();

app.Run();

public partial class Program;
