using Matchbook.BuildingBlocks.Hosting;
using Matchbook.Requisitions.Api;
using Matchbook.Requisitions.Application;
using Matchbook.Requisitions.Infrastructure;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddMatchbookDefaults("requisitions");
builder.Services.AddValidation();
builder.Services.AddRequisitionsApplication();
builder.Services.AddRequisitionsInfrastructure(builder.Configuration);
builder.Services.AddAuthorizationBuilder().AddRequisitionPolicies();

WebApplication app = builder.Build();

app.UseMatchbookDefaults();
app.MapRequisitions();
app.MapApprovals();

app.Run();

public partial class Program;
