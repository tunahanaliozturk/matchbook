using Matchbook.BuildingBlocks.Hosting;
using Matchbook.Requisitions.Api;
using Matchbook.Requisitions.Api.Features.Approvals;
using Matchbook.Requisitions.Api.Features.Requisitions;
using Matchbook.Requisitions.Application;
using Matchbook.Requisitions.Infrastructure;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddMatchbookDefaults("requisitions");
builder.Services.AddValidation();
builder.Services.AddRequisitionsApplication().AddHandlersFrom(typeof(IRequisitionsDb).Assembly);
builder.Services.AddRequisitionsInfrastructure(builder.Configuration);
builder.Services.AddAuthorizationBuilder().AddRequisitionPolicies();

WebApplication app = builder.Build();

app.UseMatchbookDefaults();
app.MapRequisitionsEndpoints();
app.MapApprovalsEndpoints();

app.Run();

public partial class Program;
