using Matchbook.Payables.Application;
using Microsoft.Extensions.Options;

namespace Matchbook.Payables.Api;

internal static class PayerConfiguration
{
    /// <summary>
    /// Binds the <c>Payer</c> section (<c>Name</c>, <c>Iban</c>, <c>Bic</c>) and refuses to start without a valid one:
    /// a wrong debtor account would otherwise surface only when a treasurer downloads the first bank file.
    /// </summary>
    public static IServiceCollection AddPayerAccount(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PayerAccount>()
            .Bind(configuration.GetSection("Payer"))
            .Validate(
                static payer => payer.IsValid(),
                "Payer:Name, Payer:Iban and Payer:Bic must describe the account payment runs pay from, with a valid IBAN and BIC.")
            .ValidateOnStart();
        services.AddSingleton(static provider => provider.GetRequiredService<IOptions<PayerAccount>>().Value);
        return services;
    }
}
