namespace Matchbook.Payables.Api;

internal static class OpenApiRefusals
{
    /// <summary>
    /// Declares the problem responses an endpoint can give: always 401 and 403, since every endpoint needs a token and
    /// a role, plus <paramref name="statuses"/>.
    /// </summary>
    public static RouteHandlerBuilder ProducesRefusals(this RouteHandlerBuilder endpoint, params int[] statuses)
    {
        endpoint.ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status403Forbidden);

        foreach (int status in statuses)
        {
            endpoint.ProducesProblem(status);
        }

        return endpoint;
    }
}
