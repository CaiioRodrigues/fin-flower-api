using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FinFlower.Application.Auth.Dtos;
using FinFlower.Application.Cash.Dtos;
using FinFlower.Application.Entries.Dtos;
using FinFlower.Domain.Enums;
using FluentAssertions;

namespace FinFlower.Api.Tests;

public class CashOpeningTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<HttpClient> NewClientAsync()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            "Caio", $"user-{Guid.CreateVersion7():N}@example.com", "Senha#Forte1"));

        var session = (await response.Content.ReadFromJsonAsync<AuthResponse>(TestJson.Options))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return client;
    }

    private static Task<HttpResponseMessage> AddEntry(HttpClient client, decimal amount, DateOnly on) =>
        client.PostAsJsonAsync("/api/entries", new CreateEntryRequest(
            EntryType.Income, "Lançamento", amount, "Geral", on, null));

    [Fact]
    public async Task There_is_no_opening_until_someone_declares_one()
    {
        var client = await NewClientAsync();

        var response = await client.GetAsync("/api/cash/opening");

        // 204, não um 200 de corpo vazio: um 200 que não traz JSON quebra o
        // cliente justamente no caso de quem ainda não declarou nada.
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Declaring_the_opening_moves_the_cash_balance()
    {
        var client = await NewClientAsync();
        await AddEntry(client, 1_000m, new DateOnly(2026, 9, 10));

        await client.PutAsJsonAsync("/api/cash/opening", new SaveCashOpeningRequest(
            30_000m, new DateOnly(2026, 9, 1), "extrato do banco"));

        var cash = await client.GetFromJsonAsync<MonthlyCashResponse>(
            "/api/cash/monthly?from=2026-09&to=2026-09", TestJson.Options);

        cash!.Opening!.Amount.Should().Be(30_000m);
        cash.Opening.Notes.Should().Be("extrato do banco");
        cash.ClosingBalance.Should().Be(31_000m);
    }

    [Fact]
    public async Task Clearing_the_opening_gives_the_history_back()
    {
        var client = await NewClientAsync();
        await AddEntry(client, 8_000m, new DateOnly(2026, 7, 15));
        await client.PutAsJsonAsync("/api/cash/opening", new SaveCashOpeningRequest(
            30_000m, new DateOnly(2026, 9, 1), null));

        (await client.DeleteAsync("/api/cash/opening")).StatusCode
            .Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);

        var cash = await client.GetFromJsonAsync<MonthlyCashResponse>(
            "/api/cash/monthly?from=2026-07&to=2026-09", TestJson.Options);

        cash!.Opening.Should().BeNull();
        cash.ClosingBalance.Should().Be(8_000m);
    }

    [Fact]
    public async Task A_date_from_another_century_is_refused_with_a_readable_message()
    {
        var client = await NewClientAsync();

        var response = await client.PutAsJsonAsync("/api/cash/opening", new SaveCashOpeningRequest(
            1_000m, new DateOnly(26, 9, 1), null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("conferiu o saldo");
    }

    [Fact]
    public async Task One_owner_never_reads_another_ones_opening()
    {
        var alice = await NewClientAsync();
        await alice.PutAsJsonAsync("/api/cash/opening", new SaveCashOpeningRequest(
            30_000m, new DateOnly(2026, 9, 1), null));

        var bob = await NewClientAsync();
        (await bob.GetAsync("/api/cash/opening")).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task The_opening_needs_a_session()
    {
        var anonymous = factory.CreateClient();

        (await anonymous.GetAsync("/api/cash/opening")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }
}
