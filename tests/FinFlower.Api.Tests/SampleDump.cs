using System.Net.Http.Headers;
using System.Net.Http.Json;
using FinFlower.Application.Auth.Dtos;
using FinFlower.Application.Contracts.Dtos;
using FinFlower.Application.Entries.Dtos;
using FinFlower.Application.Events.Dtos;
using FinFlower.Domain.Enums;

namespace FinFlower.Api.Tests;

/// <summary>Gera amostras dos relatórios em disco, para inspeção visual.</summary>
public class SampleDump(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact(Skip = "Ferramenta de inspeção. Para gerar as amostras, remova o Skip e rode: "
        + "REPORT_SAMPLE_DIR=/caminho dotnet test --filter FullyQualifiedName~SampleDump")]
    public async Task Dump()
    {
        var outputDir = Environment.GetEnvironmentVariable("REPORT_SAMPLE_DIR") ?? "/tmp/amostras";
        Directory.CreateDirectory(outputDir);

        var client = factory.CreateClient();
        var session = (await (await client.PostAsJsonAsync("/api/auth/register",
                new RegisterRequest("Caio", $"u{Guid.CreateVersion7():N}@example.com", "Senha#Forte1")))
            .Content.ReadFromJsonAsync<AuthResponse>(TestJson.Options))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

        var cenarios = new[]
        {
            ("Festa junina", new DateOnly(2026, 6, 20), 5000m, 2000m, "Associação de Bairro", 8000m, 3),
            ("Show de rock", new DateOnly(2026, 7, 10), 12000m, 7000m, "Prefeitura Municipal", 15000m, 4),
            ("Réveillon", new DateOnly(2026, 12, 31), 20000m, 11000m, "Hotel Costa Verde", 30000m, 6),
        };

        Guid primeiro = Guid.Empty;

        foreach (var (nome, data, receita, custo, contratante, valor, parcelas) in cenarios)
        {
            var @event = (await (await client.PostAsJsonAsync("/api/events",
                    new CreateEventRequest(nome, "Produção completa", data)))
                .Content.ReadFromJsonAsync<EventDetailsResponse>(TestJson.Options))!;

            if (primeiro == Guid.Empty) primeiro = @event.Id;

            await client.PostAsJsonAsync("/api/entries", new CreateEntryRequest(
                EntryType.Income, "Venda de ingressos", receita, "Ingressos", data, @event.Id));
            await client.PostAsJsonAsync("/api/entries", new CreateEntryRequest(
                EntryType.Expense, "Estrutura e equipe", custo, "Estrutura", data, @event.Id));

            var contract = (await (await client.PostAsJsonAsync("/api/contracts",
                    new CreateContractRequest(ContractDirection.Receivable, contratante, "Patrocínio",
                        valor, PaymentMethod.Boleto, parcelas, data.AddMonths(-2), data.AddMonths(-3), @event.Id)))
                .Content.ReadFromJsonAsync<ContractResponse>(TestJson.Options))!;

            await client.PostAsJsonAsync("/api/contracts", new CreateContractRequest(
                ContractDirection.Payable, "Buffet Silva", "Alimentação",
                custo, PaymentMethod.Pix, 2, data.AddMonths(-1), data.AddMonths(-2), @event.Id));

            await client.PostAsJsonAsync($"/api/contracts/{contract.Id}/installments/1/settle",
                new SettleInstallmentRequest());
        }

        var rotas = new (string Route, string Name)[]
        {
            ("/api/reports/cash/export?format=xlsx", "caixa-por-evento.xlsx"),
            ("/api/reports/cash/export?format=pdf", "caixa-por-evento.pdf"),
            ("/api/reports/cash-flow/export?format=xlsx", "fluxo-de-caixa.xlsx"),
            ("/api/reports/cash-flow/export?format=pdf", "fluxo-de-caixa.pdf"),
            ("/api/reports/installments/export?format=xlsx", "parcelas.xlsx"),
            ("/api/reports/installments/export?format=pdf", "parcelas.pdf"),
            ($"/api/events/{primeiro}/statement/export?format=pdf", "extrato-evento.pdf"),
            ($"/api/events/{primeiro}/statement/export?format=xlsx", "extrato-evento.xlsx"),
        };

        foreach (var (route, name) in rotas)
        {
            var bytes = await (await client.GetAsync(route)).Content.ReadAsByteArrayAsync();
            await File.WriteAllBytesAsync(Path.Combine(outputDir, name), bytes);
            Console.WriteLine($"{name}: {bytes.Length} bytes");
        }
    }
}
