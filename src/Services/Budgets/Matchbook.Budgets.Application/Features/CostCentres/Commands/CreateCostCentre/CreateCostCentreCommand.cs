using Matchbook.SharedKernel;

namespace Matchbook.Budgets.Application.Features.CostCentres.Commands.CreateCostCentre;

/// <param name="Id">Optional. The client's id for this request; repeating it returns the first result.</param>
public sealed record CreateCostCentreCommand(Guid? Id, string Code, string Name, Guid ManagerId) : ICommand<CostCentreView>;
