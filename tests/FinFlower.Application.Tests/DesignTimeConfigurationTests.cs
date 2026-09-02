using FinFlower.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Design;

namespace FinFlower.Application.Tests;

public class DesignTimeConfigurationTests
{
    /// <summary>
    /// Uma IDesignTimeDbContextFactory tem precedência sobre a configuração da
    /// aplicação nas ferramentas do EF. Já houve uma aqui, com uma connection
    /// string fixa: o Update-Database ia para o servidor errado e o erro que
    /// aparecia era um timeout de TCP, sem nenhuma pista da causa.
    ///
    /// Sem fábrica, as ferramentas montam o contexto pelo host da API e leem o
    /// appsettings e os segredos de usuário como qualquer outra execução.
    /// </summary>
    [Fact]
    public void No_design_time_factory_overrides_the_application_configuration()
    {
        var factories = typeof(AppDbContext).Assembly
            .GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false })
            .Where(type => type.GetInterfaces().Any(contract =>
                contract.IsGenericType
                && contract.GetGenericTypeDefinition() == typeof(IDesignTimeDbContextFactory<>)))
            .ToList();

        factories.Should().BeEmpty(
            "uma fábrica de design-time silenciaria a configuração real e mandaria as migrations para outro banco");
    }
}
