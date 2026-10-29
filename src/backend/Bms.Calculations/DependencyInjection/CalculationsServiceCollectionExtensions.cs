using Bms.Application.Abstractions.Calculations;
using Bms.Calculations.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bms.Calculations.DependencyInjection;

public static class CalculationsServiceCollectionExtensions
{
    public static IServiceCollection AddBmsCalculations(this IServiceCollection services)
    {
        // One registration per calculation type — a new type is a new line here.
        services.AddScoped<ICalculationHandler, TotalizerDeltaHandler>();
        services.AddScoped<ICalculationHandler, FormulaHandler>();
        services.AddScoped<ICalculationHandler, TimeWeightedAverageHandler>();
        services.AddScoped<ICalculationHandler, EquivalentContinuousLevelHandler>();
        services.AddScoped<ICalculationHandler, BaselineDeltaHandler>();

        services.AddScoped<ICalculationEngine, CalculationEngine>();
        services.AddScoped<ICalculationReader, CalculationReader>();
        services.AddScoped<DerivedParameterCalculations>();
        services.TryAddSingleton(TimeProvider.System);
        return services;
    }
}
