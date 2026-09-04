using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FinFlower.Application.Auth.Dtos;
using FinFlower.Application.Quotes.Dtos;
using FluentAssertions;

namespace FinFlower.Api.Tests;

/// <summary>
/// A proposta é o único documento do sistema que sai da empresa e chega a um
/// cliente. Um erro aqui não é um relatório torto: é a imagem de quem enviou.
/// </summary>
public class QuoteProposalTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<HttpClient> NewClientAsync(string name = "Caio Rodrigues")
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            name, $"user-{Guid.CreateVersion7():N}@example.com", "Senha#Forte1"));

        var session = (await response.Content.ReadFromJsonAsync<AuthResponse>(TestJson.Options))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return client;
    }

    private static async Task<QuoteResponse> ArrangeQuoteAsync(HttpClient client)
    {
        var quote = (await (await client.PostAsJsonAsync("/api/quotes", new CreateQuoteRequest(
                "Prefeitura Municipal", "Show de encerramento",
                new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30),
                "Pagamento em até 3 parcelas, a primeira 30 dias após a assinatura.", null)))
            .Content.ReadFromJsonAsync<QuoteResponse>(TestJson.Options))!;

        await client.PostAsJsonAsync($"/api/quotes/{quote.Id}/items",
            new QuoteItemRequest("Estrutura de palco 12x8m", 1m, 12_000m, "un"));
        await client.PostAsJsonAsync($"/api/quotes/{quote.Id}/items",
            new QuoteItemRequest("Equipe técnica", 3m, 850m, "diária"));
        await client.PutAsJsonAsync($"/api/quotes/{quote.Id}/discount", new ApplyDiscountRequest(550m));

        return (await client.GetFromJsonAsync<QuoteResponse>($"/api/quotes/{quote.Id}", TestJson.Options))!;
    }

    [Fact]
    public async Task The_proposal_is_a_real_pdf_named_after_the_quote()
    {
        var client = await NewClientAsync();
        var quote = await ArrangeQuoteAsync(client);

        var response = await client.GetAsync($"/api/quotes/{quote.Id}/proposal");
        var bytes = await response.Content.ReadAsByteArrayAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        bytes.Take(4).Should().Equal("%PDF"u8.ToArray());

        // O nome do anexo é a primeira coisa que o cliente lê.
        response.Content.Headers.ContentDisposition!.FileName
            .Should().Contain("proposta").And.Contain("orc");
    }

    [Fact]
    public async Task The_proposal_carries_what_the_client_needs_to_decide()
    {
        var client = await NewClientAsync("Caio Rodrigues");
        var quote = await ArrangeQuoteAsync(client);

        var bytes = await (await client.GetAsync($"/api/quotes/{quote.Id}/proposal"))
            .Content.ReadAsByteArrayAsync();

        var text = PdfText.Extract(bytes);

        foreach (var anchor in new[]
        {
            quote.Number,           // identificação
            "Prefeitura Municipal", // para quem
            "Caio Rodrigues",       // de quem
            "Show de encerramento", // sobre o quê
            "30/09/2026",           // até quando vale
            "Estrutura de palco",   // o que está incluído
            "Equipe técnica",
            "De acordo",            // onde assinar
        })
        {
            text.Should().Contain(anchor, $"a proposta precisa mostrar '{anchor}'");
        }
    }

    [Fact]
    public async Task The_proposal_shows_the_client_how_the_total_was_reached()
    {
        var client = await NewClientAsync();
        var quote = await ArrangeQuoteAsync(client);

        var bytes = await (await client.GetAsync($"/api/quotes/{quote.Id}/proposal"))
            .Content.ReadAsByteArrayAsync();

        var text = PdfText.Extract(bytes);

        // Um total que aparece sozinho vira discussão por e-mail. O cliente
        // precisa conseguir refazer a conta olhando só para o papel. O "R$" fica
        // fora das âncoras porque o separador entre símbolo e número muda com a
        // versão do ICU, e não é isso que o teste está protegendo.
        text.Should().Contain("R$");
        text.Should().Contain("12.000,00");  // 1 x estrutura
        text.Should().Contain("2.550,00");   // 3 x equipe
        text.Should().Contain("14.550,00");  // subtotal
        text.Should().Contain("550,00");     // desconto
        text.Should().Contain("14.000,00");  // total
    }

    [Fact]
    public async Task Another_users_quote_has_no_proposal()
    {
        var alice = await NewClientAsync();
        var quote = await ArrangeQuoteAsync(alice);

        var bob = await NewClientAsync();
        var response = await bob.GetAsync($"/api/quotes/{quote.Id}/proposal");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_quote_without_items_still_produces_a_document()
    {
        var client = await NewClientAsync();
        var quote = (await (await client.PostAsJsonAsync("/api/quotes", new CreateQuoteRequest(
                "Cliente", "Rascunho", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), null, null)))
            .Content.ReadFromJsonAsync<QuoteResponse>(TestJson.Options))!;

        var response = await client.GetAsync($"/api/quotes/{quote.Id}/proposal");

        // Imprimir um rascunho vazio é legítimo: serve de modelo para preencher
        // à mão. O que não pode é a geração quebrar.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsByteArrayAsync()).Take(4).Should().Equal("%PDF"u8.ToArray());
    }

    [Fact]
    public async Task The_proposal_needs_a_session()
    {
        var anonymous = factory.CreateClient();

        var response = await anonymous.GetAsync($"/api/quotes/{Guid.CreateVersion7()}/proposal");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
