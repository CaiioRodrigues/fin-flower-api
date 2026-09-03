using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ClosedXML.Excel;
using FinFlower.Application.Auth.Dtos;
using FinFlower.Application.Cash.Dtos;
using FinFlower.Application.Entries.Dtos;
using FinFlower.Application.Events.Dtos;
using FinFlower.Application.Quotes.Dtos;
using FinFlower.Application.Recurring.Dtos;
using FinFlower.Domain.Enums;
using FluentAssertions;

namespace FinFlower.Api.Tests;

/// <summary>
/// O caminho completo pela API: livro-caixa, itens fixos, orçamento virando
/// contrato e o fechamento mensal que soma tudo isso.
/// </summary>
public class CashLedgerEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<HttpClient> NewAuthenticatedClientAsync()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            "Caio", $"user-{Guid.CreateVersion7():N}@example.com", "Senha#Forte1"));

        var session = (await response.Content.ReadFromJsonAsync<AuthResponse>(TestJson.Options))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return client;
    }

    private static Task<HttpResponseMessage> PostEntry(
        HttpClient client,
        EntryType type,
        decimal amount,
        DateOnly on,
        string category = "Geral",
        Guid? eventId = null) =>
        client.PostAsJsonAsync("/api/entries", new CreateEntryRequest(
            type, "Lançamento", amount, category, on, eventId));

    [Fact]
    public async Task Every_new_route_requires_authentication()
    {
        var anonymous = factory.CreateClient();
        var id = Guid.CreateVersion7();

        var responses = new[]
        {
            await anonymous.GetAsync("/api/entries"),
            await anonymous.GetAsync("/api/entries/categories"),
            await anonymous.DeleteAsync($"/api/entries/{id}"),
            await anonymous.GetAsync("/api/cash/monthly"),
            await anonymous.GetAsync("/api/recurring-items"),
            await anonymous.PostAsync("/api/recurring-items/generate", null),
            await anonymous.GetAsync("/api/quotes"),
            await anonymous.GetAsync($"/api/quotes/{id}"),
            await anonymous.GetAsync("/api/reports/monthly/export"),
        };

        responses.Should().AllSatisfy(r => r.StatusCode.Should().Be(HttpStatusCode.Unauthorized));
    }

    [Fact]
    public async Task Money_moves_without_an_event()
    {
        var client = await NewAuthenticatedClientAsync();

        var created = await PostEntry(client, EntryType.Expense, 1_200m, new DateOnly(2026, 9, 5), "Escritório");
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var entry = (await created.Content.ReadFromJsonAsync<LedgerEntryResponse>(TestJson.Options))!;
        entry.EventId.Should().BeNull();
        entry.Source.Should().Be(EntrySource.Manual);
        entry.SignedAmount.Should().Be(-1_200m);
        entry.Competence.Should().Be("2026-09");
    }

    [Fact]
    public async Task The_ledger_filters_and_totals_the_whole_period_not_just_the_page()
    {
        var client = await NewAuthenticatedClientAsync();
        var @event = (await (await client.PostAsJsonAsync("/api/events",
                new CreateEventRequest("Casamento Silva", null, new DateOnly(2026, 9, 20))))
            .Content.ReadFromJsonAsync<EventDetailsResponse>(TestJson.Options))!;

        await PostEntry(client, EntryType.Income, 15_000m, new DateOnly(2026, 9, 20), "Serviços", @event.Id);
        await PostEntry(client, EntryType.Expense, 4_000m, new DateOnly(2026, 9, 18), "Fornecedores", @event.Id);
        await PostEntry(client, EntryType.Expense, 1_200m, new DateOnly(2026, 9, 5), "Escritório");
        await PostEntry(client, EntryType.Expense, 900m, new DateOnly(2026, 8, 5), "Escritório");

        var september = (await client.GetFromJsonAsync<LedgerPageResponse>(
            "/api/entries?from=2026-09-01&to=2026-09-30", TestJson.Options))!;
        september.TotalCount.Should().Be(3);
        september.TotalIncome.Should().Be(15_000m);
        september.TotalExpense.Should().Be(5_200m);
        september.Result.Should().Be(9_800m);

        var withoutEvent = (await client.GetFromJsonAsync<LedgerPageResponse>(
            "/api/entries?withoutEvent=true", TestJson.Options))!;
        withoutEvent.TotalCount.Should().Be(2);

        var ofEvent = (await client.GetFromJsonAsync<LedgerPageResponse>(
            $"/api/entries?eventId={@event.Id}", TestJson.Options))!;
        ofEvent.TotalCount.Should().Be(2);
        ofEvent.Entries.Should().AllSatisfy(e => e.EventName.Should().Be("Casamento Silva"));

        // Uma página menor não muda os totais: eles são do filtro inteiro.
        var firstPage = (await client.GetFromJsonAsync<LedgerPageResponse>(
            "/api/entries?pageSize=1", TestJson.Options))!;
        firstPage.Entries.Should().HaveCount(1);
        firstPage.TotalCount.Should().Be(4);
        firstPage.TotalExpense.Should().Be(6_100m);
    }

    [Fact]
    public async Task Categories_are_offered_back_for_the_form()
    {
        var client = await NewAuthenticatedClientAsync();
        await PostEntry(client, EntryType.Expense, 100m, new DateOnly(2026, 9, 1), "Marketing");
        await PostEntry(client, EntryType.Expense, 200m, new DateOnly(2026, 9, 2), "Escritório");
        await PostEntry(client, EntryType.Expense, 300m, new DateOnly(2026, 9, 3), "Marketing");

        var categories = (await client.GetFromJsonAsync<string[]>(
            "/api/entries/categories", TestJson.Options))!;

        categories.Should().Equal("Escritório", "Marketing");
    }

    [Fact]
    public async Task The_monthly_close_chains_the_balance_month_by_month()
    {
        var client = await NewAuthenticatedClientAsync();

        await PostEntry(client, EntryType.Income, 10_000m, new DateOnly(2026, 7, 10));
        await PostEntry(client, EntryType.Expense, 4_000m, new DateOnly(2026, 7, 20));
        await PostEntry(client, EntryType.Expense, 2_000m, new DateOnly(2026, 8, 5));

        var cash = (await client.GetFromJsonAsync<MonthlyCashResponse>(
            "/api/cash/monthly?from=2026-07&to=2026-09", TestJson.Options))!;

        cash.Months.Should().HaveCount(3);
        cash.Months[0].ClosingBalance.Should().Be(6_000m);
        cash.Months[1].OpeningBalance.Should().Be(6_000m);
        cash.Months[1].ClosingBalance.Should().Be(4_000m);
        cash.Months[2].EntryCount.Should().Be(0, "setembro entra vazio, mas entra");
        cash.ClosingBalance.Should().Be(4_000m);
        cash.Months[0].Label.Should().Be("jul/2026");
    }

    [Fact]
    public async Task A_malformed_competence_answers_400()
    {
        var client = await NewAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/cash/monthly?from=julho");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("aaaa-mm");
    }

    [Fact]
    public async Task Fixed_costs_and_pro_labore_are_generated_once_per_month()
    {
        var client = await NewAuthenticatedClientAsync();

        await client.PostAsJsonAsync("/api/recurring-items", new CreateRecurringItemRequest(
            RecurringKind.FixedExpense, "Aluguel", 2_500m, "Estrutura", 10, "2026-01", null, null));
        await client.PostAsJsonAsync("/api/recurring-items", new CreateRecurringItemRequest(
            RecurringKind.ProLabore, "Retirada do sócio", 6_000m, "Sócios", 5, "2026-01", null, null));

        var first = (await (await client.PostAsJsonAsync("/api/recurring-items/generate",
                new { competence = "2026-09" }))
            .Content.ReadFromJsonAsync<GenerateMonthResponse>(TestJson.Options))!;
        first.Generated.Should().Be(2);
        first.GeneratedAmount.Should().Be(8_500m);

        // Clicar de novo é o comportamento esperado de quem opera.
        var second = (await (await client.PostAsJsonAsync("/api/recurring-items/generate",
                new { competence = "2026-09" }))
            .Content.ReadFromJsonAsync<GenerateMonthResponse>(TestJson.Options))!;
        second.Generated.Should().Be(0);
        second.AlreadyExisted.Should().Be(2);

        var month = (await client.GetFromJsonAsync<MonthlyCashResponse>(
            "/api/cash/monthly?from=2026-09&to=2026-09", TestJson.Options))!.Months.Single();
        month.Expense.Should().Be(8_500m);
        month.FixedExpense.Should().Be(2_500m);
        month.ProLabore.Should().Be(6_000m);
    }

    [Fact]
    public async Task A_quote_becomes_a_contract_and_then_cash()
    {
        var client = await NewAuthenticatedClientAsync();

        var quote = (await (await client.PostAsJsonAsync("/api/quotes", new CreateQuoteRequest(
                "Prefeitura Municipal", "Show de encerramento",
                new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), null, null)))
            .Content.ReadFromJsonAsync<QuoteResponse>(TestJson.Options))!;

        quote.Number.Should().StartWith("ORC-2026-");

        await client.PostAsJsonAsync($"/api/quotes/{quote.Id}/items",
            new QuoteItemRequest("Estrutura de palco", 1m, 12_000m, "un"));
        var withItems = (await (await client.PostAsJsonAsync($"/api/quotes/{quote.Id}/items",
                new QuoteItemRequest("Equipe técnica", 3m, 850m, "diária")))
            .Content.ReadFromJsonAsync<QuoteResponse>(TestJson.Options))!;
        withItems.Total.Should().Be(14_550m);

        await client.PutAsJsonAsync($"/api/quotes/{quote.Id}/discount", new ApplyDiscountRequest(550m));
        await client.PostAsync($"/api/quotes/{quote.Id}/send", null);

        var approved = (await (await client.PostAsJsonAsync($"/api/quotes/{quote.Id}/approve",
                new ApproveQuoteRequest(PaymentMethod.Boleto, 2, new DateOnly(2026, 10, 5), new DateOnly(2026, 9, 2))))
            .Content.ReadFromJsonAsync<QuoteResponse>(TestJson.Options))!;

        approved.Status.Should().Be(QuoteStatus.Approved);
        approved.ContractId.Should().NotBeNull();

        var contract = (await client.GetFromJsonAsync<Application.Contracts.Dtos.ContractResponse>(
            $"/api/contracts/{approved.ContractId}", TestJson.Options))!;
        contract.TotalAmount.Should().Be(14_000m);
        contract.Installments.Should().HaveCount(2);

        var settled = await client.PostAsJsonAsync(
            $"/api/contracts/{approved.ContractId}/installments/1/settle",
            new Application.Contracts.Dtos.SettleInstallmentRequest());
        settled.StatusCode.Should().Be(HttpStatusCode.OK);

        var october = (await client.GetFromJsonAsync<MonthlyCashResponse>(
            "/api/cash/monthly?from=2026-10&to=2026-10", TestJson.Options))!.Months.Single();
        october.Income.Should().Be(7_000m);
        october.ContractIncome.Should().Be(7_000m);
    }

    [Fact]
    public async Task An_entry_that_came_from_a_contract_cannot_be_deleted_from_the_ledger()
    {
        var client = await NewAuthenticatedClientAsync();
        var contract = (await (await client.PostAsJsonAsync("/api/contracts",
                new Application.Contracts.Dtos.CreateContractRequest(
                    ContractDirection.Receivable, "Cliente", null, 1_000m,
                    PaymentMethod.Pix, 1, new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 1))))
            .Content.ReadFromJsonAsync<Application.Contracts.Dtos.ContractResponse>(TestJson.Options))!;

        await client.PostAsJsonAsync($"/api/contracts/{contract.Id}/installments/1/settle",
            new Application.Contracts.Dtos.SettleInstallmentRequest());

        var entry = (await client.GetFromJsonAsync<LedgerPageResponse>("/api/entries", TestJson.Options))!
            .Entries.Single();
        entry.IsEditable.Should().BeFalse();

        var response = await client.DeleteAsync($"/api/entries/{entry.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Estorne a parcela");
    }

    [Fact]
    public async Task The_monthly_report_exports_with_a_running_balance_column()
    {
        var client = await NewAuthenticatedClientAsync();
        await PostEntry(client, EntryType.Income, 10_000m, new DateOnly(2026, 7, 10));
        await PostEntry(client, EntryType.Expense, 4_000m, new DateOnly(2026, 8, 20));

        var response = await client.GetAsync("/api/reports/monthly/export?format=xlsx&from=2026-07&to=2026-08");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentDisposition!.FileName.Should().Contain("caixa-mensal");

        using var stream = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet("Mês a mês");

        sheet.Cell(2, 1).GetString().Should().Be("jul/2026");
        sheet.Cell(2, 6).DataType.Should().Be(XLDataType.Number, "saldo tem de ser número para somar");
        sheet.Cell(2, 6).GetValue<decimal>().Should().Be(10_000m);
        sheet.Cell(3, 6).GetValue<decimal>().Should().Be(6_000m, "agosto fecha com o acumulado");
    }

    [Fact]
    public async Task The_monthly_report_is_also_a_real_pdf()
    {
        var client = await NewAuthenticatedClientAsync();
        await PostEntry(client, EntryType.Income, 1_000m, new DateOnly(2026, 9, 10));

        var response = await client.GetAsync("/api/reports/monthly/export?format=pdf");
        var bytes = await response.Content.ReadAsByteArrayAsync();

        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        bytes.Take(4).Should().Equal("%PDF"u8.ToArray());
    }

    [Fact]
    public async Task Another_users_money_never_leaks_into_the_close()
    {
        var alice = await NewAuthenticatedClientAsync();
        await PostEntry(alice, EntryType.Income, 50_000m, new DateOnly(2026, 9, 1));

        var bob = await NewAuthenticatedClientAsync();
        var cash = (await bob.GetFromJsonAsync<MonthlyCashResponse>(
            "/api/cash/monthly?from=2026-09&to=2026-09", TestJson.Options))!;

        cash.OpeningBalance.Should().Be(0m);
        cash.ClosingBalance.Should().Be(0m);
        (await bob.GetFromJsonAsync<LedgerPageResponse>("/api/entries", TestJson.Options))!
            .TotalCount.Should().Be(0);
    }
}
