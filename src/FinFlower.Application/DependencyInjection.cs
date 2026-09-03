using FinFlower.Application.Auth;
using FinFlower.Application.Cash;
using FinFlower.Application.Contracts;
using FinFlower.Application.Entries;
using FinFlower.Application.Events;
using FinFlower.Application.Quotes;
using FinFlower.Application.Recurring;
using FinFlower.Application.Reports;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace FinFlower.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<DependencyInjectionMarker>(ServiceLifetime.Singleton);
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IEventService, EventService>();
        services.AddScoped<IEntryService, EntryService>();
        services.AddScoped<IMonthlyCashService, MonthlyCashService>();
        services.AddScoped<IRecurringItemService, RecurringItemService>();
        services.AddScoped<IQuoteService, QuoteService>();
        services.AddScoped<ICashReportService, CashReportService>();
        services.AddScoped<IContractService, ContractService>();
        services.AddScoped<ICashFlowReportService, CashFlowReportService>();
        services.AddScoped<IReportExportService, ReportExportService>();

        return services;
    }
}

/// <summary>Âncora para varrer o assembly sem depender de nomes de tipo.</summary>
internal sealed class DependencyInjectionMarker;
