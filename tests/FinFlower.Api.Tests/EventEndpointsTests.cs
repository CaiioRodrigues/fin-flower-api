using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FinFlower.Application.Auth.Dtos;
using FinFlower.Application.Events.Dtos;
using FinFlower.Application.Reports.Dtos;
using FinFlower.Domain.Enums;
using FluentAssertions;

namespace FinFlower.Api.Tests;

public class EventEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly EventDate = new(2026, 12, 12);

    /// <summary>Cria uma conta nova e devolve um cliente já autenticado com ela.</summary>
    private async Task<HttpClient> NewAuthenticatedClientAsync()
    {
        var client = factory.CreateClient();
        var registration = new RegisterRequest(
            "Caio",
            $"user-{Guid.CreateVersion7():N}@example.com",
            "Senha#Forte1");

        var response = await client.PostAsJsonAsync("/api/auth/register", registration);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var session = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

        return client;
    }

    private static async Task<EventDetailsResponse> CreateEventAsync(
        HttpClient client,
        string name = "Festa de Ano Novo")
    {
        var response = await client.PostAsJsonAsync(
            "/api/events",
            new CreateEventRequest(name, "Réveillon na praia", EventDate));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<EventDetailsResponse>(TestJson.Options))!;
    }

    private static Task<HttpResponseMessage> AddEntryAsync(
        HttpClient client,
        Guid eventId,
        EntryType type,
        decimal amount,
        string description = "Lançamento") =>
        client.PostAsJsonAsync(
            $"/api/events/{eventId}/entries",
            new CreateEntryRequest(type, description, amount, "Geral", EventDate));

    [Fact]
    public async Task Every_event_route_requires_authentication()
    {
        var anonymous = factory.CreateClient();
        var id = Guid.CreateVersion7();

        var responses = new[]
        {
            await anonymous.GetAsync("/api/events"),
            await anonymous.GetAsync($"/api/events/{id}"),
            await anonymous.PostAsJsonAsync("/api/events", new CreateEventRequest("x", null, EventDate)),
            await anonymous.DeleteAsync($"/api/events/{id}"),
            await anonymous.GetAsync("/api/reports/cash"),
        };

        responses.Should().AllSatisfy(r => r.StatusCode.Should().Be(HttpStatusCode.Unauthorized));
    }

    [Fact]
    public async Task Full_cycle_of_an_event_with_its_entries()
    {
        var client = await NewAuthenticatedClientAsync();
        var @event = await CreateEventAsync(client);

        (await AddEntryAsync(client, @event.Id, EntryType.Income, 8000m, "Ingressos"))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await AddEntryAsync(client, @event.Id, EntryType.Expense, 3000m, "Aluguel do espaço"))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await AddEntryAsync(client, @event.Id, EntryType.Expense, 2500m, "Buffet"))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var details = (await client.GetFromJsonAsync<EventDetailsResponse>($"/api/events/{@event.Id}", TestJson.Options))!;

        details.TotalIncome.Should().Be(8000m);
        details.TotalExpense.Should().Be(5500m);
        details.Result.Should().Be(2500m);
        details.IsProfitable.Should().BeTrue();
        details.Entries.Should().HaveCount(3);
        details.Entries.Should().Contain(e => e.Description == "Buffet");
    }

    [Fact]
    public async Task Entry_can_be_edited_and_removed()
    {
        var client = await NewAuthenticatedClientAsync();
        var @event = await CreateEventAsync(client);
        var created = (await (await AddEntryAsync(client, @event.Id, EntryType.Income, 100m))
            .Content.ReadFromJsonAsync<EntryResponse>(TestJson.Options))!;

        var edited = await client.PutAsJsonAsync(
            $"/api/events/{@event.Id}/entries/{created.Id}",
            new UpdateEntryRequest(EntryType.Expense, "Reembolso", 30m, "Outros", EventDate));
        edited.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterEdit = (await client.GetFromJsonAsync<EventDetailsResponse>($"/api/events/{@event.Id}", TestJson.Options))!;
        afterEdit.Result.Should().Be(-30m);

        var removed = await client.DeleteAsync($"/api/events/{@event.Id}/entries/{created.Id}");
        removed.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterRemoval = (await client.GetFromJsonAsync<EventDetailsResponse>($"/api/events/{@event.Id}", TestJson.Options))!;
        afterRemoval.Entries.Should().BeEmpty();
        afterRemoval.Result.Should().Be(0m);
    }

    [Fact]
    public async Task Closed_event_rejects_changes_with_400()
    {
        var client = await NewAuthenticatedClientAsync();
        var @event = await CreateEventAsync(client);
        (await client.PostAsync($"/api/events/{@event.Id}/close", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var blocked = await AddEntryAsync(client, @event.Id, EntryType.Income, 100m);

        blocked.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await blocked.Content.ReadAsStringAsync()).Should().Contain("evento fechado");

        (await client.PostAsync($"/api/events/{@event.Id}/reopen", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await AddEntryAsync(client, @event.Id, EntryType.Income, 100m))
            .StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Invalid_entry_is_rejected_with_field_errors()
    {
        var client = await NewAuthenticatedClientAsync();
        var @event = await CreateEventAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/events/{@event.Id}/entries",
            new CreateEntryRequest(EntryType.Income, "", -50m, "", EventDate));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Description").And.Contain("Amount").And.Contain("Category");
    }

    [Fact]
    public async Task Another_users_event_answers_404_on_every_route()
    {
        var alice = await NewAuthenticatedClientAsync();
        var @event = await CreateEventAsync(alice, "Festa da Alice");
        var entry = (await (await AddEntryAsync(alice, @event.Id, EntryType.Income, 500m))
            .Content.ReadFromJsonAsync<EntryResponse>(TestJson.Options))!;

        var bob = await NewAuthenticatedClientAsync();

        var responses = new[]
        {
            await bob.GetAsync($"/api/events/{@event.Id}"),
            await bob.PutAsJsonAsync($"/api/events/{@event.Id}", new UpdateEventRequest("Sequestrado", null, EventDate)),
            await bob.DeleteAsync($"/api/events/{@event.Id}"),
            await bob.PostAsync($"/api/events/{@event.Id}/close", null),
            await AddEntryAsync(bob, @event.Id, EntryType.Income, 1m),
            await bob.DeleteAsync($"/api/events/{@event.Id}/entries/{entry.Id}"),
        };

        responses.Should().AllSatisfy(r => r.StatusCode.Should().Be(HttpStatusCode.NotFound));

        // E o evento da Alice continua intacto.
        var untouched = (await alice.GetFromJsonAsync<EventDetailsResponse>($"/api/events/{@event.Id}", TestJson.Options))!;
        untouched.Name.Should().Be("Festa da Alice");
        untouched.TotalIncome.Should().Be(500m);
    }

    [Fact]
    public async Task Listing_only_returns_the_callers_events()
    {
        var alice = await NewAuthenticatedClientAsync();
        await CreateEventAsync(alice, "Festa da Alice");

        var bob = await NewAuthenticatedClientAsync();
        await CreateEventAsync(bob, "Festa do Bob");

        var bobList = (await bob.GetFromJsonAsync<List<EventSummaryResponse>>("/api/events", TestJson.Options))!;

        bobList.Should().ContainSingle().Which.Name.Should().Be("Festa do Bob");
    }

    [Fact]
    public async Task Cash_report_consolidates_the_events_of_the_caller()
    {
        var client = await NewAuthenticatedClientAsync();

        var profitable = await CreateEventAsync(client, "Show de rock");
        await AddEntryAsync(client, profitable.Id, EntryType.Income, 12_000m);
        await AddEntryAsync(client, profitable.Id, EntryType.Expense, 7000m);

        var loss = await CreateEventAsync(client, "Workshop");
        await AddEntryAsync(client, loss.Id, EntryType.Income, 800m);
        await AddEntryAsync(client, loss.Id, EntryType.Expense, 2300m);

        var report = (await client.GetFromJsonAsync<CashReportResponse>("/api/reports/cash", TestJson.Options))!;

        report.EventCount.Should().Be(2);
        report.ProfitableEventCount.Should().Be(1);
        report.UnprofitableEventCount.Should().Be(1);
        report.TotalIncome.Should().Be(12_800m);
        report.TotalExpense.Should().Be(9300m);
        report.Balance.Should().Be(3500m);
    }

    [Fact]
    public async Task Cash_report_rejects_an_inverted_period()
    {
        var client = await NewAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/reports/cash?from=2026-12-01&to=2026-01-01");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Entry_type_travels_as_text_in_the_json()
    {
        var client = await NewAuthenticatedClientAsync();
        var @event = await CreateEventAsync(client);
        await AddEntryAsync(client, @event.Id, EntryType.Expense, 42m, "Café");

        var json = await client.GetStringAsync($"/api/events/{@event.Id}");

        // O front lê "Expense"/"Open" em vez de 2/1 — contrato legível e estável.
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("status").GetString().Should().Be("Open");
        document.RootElement.GetProperty("entries")[0].GetProperty("type").GetString().Should().Be("Expense");
    }
}
