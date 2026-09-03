using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ClosedXML.Excel;
using FinFlower.Application.Auth.Dtos;
using FinFlower.Application.Contracts.Dtos;
using FinFlower.Application.Entries.Dtos;
using FinFlower.Application.Events.Dtos;
using FinFlower.Domain.Enums;
using FluentAssertions;

namespace FinFlower.Api.Tests;

public class ReportExportTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly EventDate = new(2026, 12, 12);

    private async Task<HttpClient> NewAuthenticatedClientAsync()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            "Caio", $"user-{Guid.CreateVersion7():N}@example.com", "Senha#Forte1"));

        var session = (await response.Content.ReadFromJsonAsync<AuthResponse>(TestJson.Options))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return client;
    }

    /// <summary>Um evento com lançamento e um contrato de R$ 9.000 em 3x.</summary>
    private static async Task<(Guid EventId, ContractResponse Contract)> ArrangeAsync(HttpClient client)
    {
        var @event = (await (await client.PostAsJsonAsync("/api/events",
                new CreateEventRequest("Festa de Ano Novo", "Réveillon", EventDate)))
            .Content.ReadFromJsonAsync<EventDetailsResponse>(TestJson.Options))!;

        await client.PostAsJsonAsync("/api/entries", new CreateEntryRequest(
            EntryType.Expense, "Aluguel do espaço", 2500m, "Estrutura", EventDate, @event.Id));

        var contract = (await (await client.PostAsJsonAsync("/api/contracts",
                new CreateContractRequest(
                    ContractDirection.Receivable, "Prefeitura Municipal", "Show de encerramento",
                    9000m, PaymentMethod.Boleto, 3, new DateOnly(2026, 10, 5), new DateOnly(2026, 9, 1), @event.Id)))
            .Content.ReadFromJsonAsync<ContractResponse>(TestJson.Options))!;

        return (@event.Id, contract);
    }

    private static XLWorkbook OpenWorkbook(byte[] content) => new(new MemoryStream(content));

    private static string AllText(XLWorkbook workbook) =>
        string.Join('\n', workbook.Worksheets.SelectMany(sheet =>
            sheet.CellsUsed().Select(cell => cell.GetFormattedString())));

    [Theory]
    [InlineData("/api/reports/cash/export")]
    [InlineData("/api/reports/cash-flow/export")]
    [InlineData("/api/reports/installments/export")]
    public async Task Export_requires_authentication(string route)
    {
        var response = await factory.CreateClient().GetAsync($"{route}?format=xlsx");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_unknown_format_is_rejected()
    {
        var client = await NewAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/reports/cash/export?format=docx");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("xlsx");
    }

    [Fact]
    public async Task The_cash_report_in_excel_carries_numbers_not_text()
    {
        var client = await NewAuthenticatedClientAsync();
        await ArrangeAsync(client);

        var response = await client.GetAsync("/api/reports/cash/export?format=xlsx");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType
            .Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        response.Content.Headers.ContentDisposition!.FileName.Should().Contain("caixa-por-evento");

        using var workbook = OpenWorkbook(await response.Content.ReadAsByteArrayAsync());
        var sheet = workbook.Worksheet("Eventos");

        sheet.Cell(2, 1).GetString().Should().Be("Festa de Ano Novo");
        sheet.Cell(2, 2).DataType.Should().Be(XLDataType.DateTime, "data precisa ser data para ordenar");

        // Valor como número é o motivo de exportar para Excel: dá para somar.
        sheet.Cell(2, 4).DataType.Should().Be(XLDataType.Number);
        sheet.Cell(2, 4).GetValue<decimal>().Should().Be(2500m);
        sheet.Cell(2, 4).Style.NumberFormat.Format.Should().Contain("R$");
    }

    [Fact]
    public async Task The_cash_flow_in_excel_has_one_sheet_per_table()
    {
        var client = await NewAuthenticatedClientAsync();
        await ArrangeAsync(client);

        var response = await client.GetAsync("/api/reports/cash-flow/export?format=xlsx&monthsAhead=3");

        using var workbook = OpenWorkbook(await response.Content.ReadAsByteArrayAsync());

        workbook.Worksheets.Select(s => s.Name).Should()
            .Contain(["Resumo", "Previsão por mês", "Próximas a vencer"]);

        AllText(workbook).Should().Contain("Saldo projetado").And.Contain("Prefeitura Municipal");
    }

    [Fact]
    public async Task The_installments_report_lists_the_open_ones_with_payment_method()
    {
        var client = await NewAuthenticatedClientAsync();
        await ArrangeAsync(client);

        var response = await client.GetAsync("/api/reports/installments/export?format=xlsx");

        using var workbook = OpenWorkbook(await response.Content.ReadAsByteArrayAsync());
        var sheet = workbook.Worksheet("Parcelas em aberto");
        var text = AllText(workbook);

        // Três parcelas mais o cabeçalho.
        sheet.LastRowUsed()!.RowNumber().Should().Be(4);
        text.Should().Contain("Boleto").And.Contain("A receber").And.Contain("A vencer");
    }

    [Fact]
    public async Task The_event_statement_in_pdf_is_a_real_pdf()
    {
        var client = await NewAuthenticatedClientAsync();
        var (eventId, _) = await ArrangeAsync(client);

        var response = await client.GetAsync($"/api/events/{eventId}/statement/export?format=pdf");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("%PDF");
        bytes.Length.Should().BeGreaterThan(1000);

        // O nome do arquivo perde acento para não depender do sistema de quem baixa.
        response.Content.Headers.ContentDisposition!.FileName
            .Should().Contain("extrato-festa-de-ano-novo");
    }

    [Fact]
    public async Task The_event_statement_in_excel_brings_entries_contracts_and_installments()
    {
        var client = await NewAuthenticatedClientAsync();
        var (eventId, _) = await ArrangeAsync(client);

        var response = await client.GetAsync($"/api/events/{eventId}/statement/export?format=xlsx");

        using var workbook = OpenWorkbook(await response.Content.ReadAsByteArrayAsync());
        var text = AllText(workbook);

        workbook.Worksheets.Select(s => s.Name).Should()
            .Contain(["Resumo", "Lançamentos", "Contratos", "Parcelas"]);

        text.Should().Contain("Aluguel do espaço").And.Contain("Prefeitura Municipal");
        text.Should().Contain("Resultado do evento");
    }

    [Fact]
    public async Task Every_report_is_also_generated_as_pdf()
    {
        var client = await NewAuthenticatedClientAsync();
        await ArrangeAsync(client);

        var routes = new[]
        {
            "/api/reports/cash/export?format=pdf",
            "/api/reports/cash-flow/export?format=pdf",
            "/api/reports/installments/export?format=pdf",
        };

        foreach (var route in routes)
        {
            var response = await client.GetAsync(route);
            response.StatusCode.Should().Be(HttpStatusCode.OK, "rota {0}", route);

            var bytes = await response.Content.ReadAsByteArrayAsync();
            Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("%PDF");
        }
    }

    [Fact]
    public async Task Another_users_event_statement_answers_404()
    {
        var alice = await NewAuthenticatedClientAsync();
        var (eventId, _) = await ArrangeAsync(alice);

        var bob = await NewAuthenticatedClientAsync();
        var response = await bob.GetAsync($"/api/events/{eventId}/statement/export?format=pdf");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_report_only_carries_the_data_of_who_asked_for_it()
    {
        var alice = await NewAuthenticatedClientAsync();
        await ArrangeAsync(alice);

        var bob = await NewAuthenticatedClientAsync();
        var response = await bob.GetAsync("/api/reports/cash-flow/export?format=xlsx");

        using var workbook = OpenWorkbook(await response.Content.ReadAsByteArrayAsync());

        AllText(workbook).Should().NotContain("Prefeitura Municipal")
            .And.NotContain("Festa de Ano Novo");
    }
}
