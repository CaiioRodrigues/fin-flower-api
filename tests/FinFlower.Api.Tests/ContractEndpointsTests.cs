using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FinFlower.Application.Auth.Dtos;
using FinFlower.Application.Contracts.Dtos;
using FinFlower.Application.Events.Dtos;
using FinFlower.Application.Reports.Dtos;
using FinFlower.Domain.Enums;
using FluentAssertions;

namespace FinFlower.Api.Tests;

public class ContractEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly DateOnly EventDate = new(2026, 12, 12);
    private static readonly DateOnly FirstDue = new(2026, 10, 5);

    /// <summary>Um PDF mínimo, com a assinatura que o domínio confere.</summary>
    private static byte[] PdfBytes(string marker = "contrato") =>
        Encoding.ASCII.GetBytes($"%PDF-1.4\n% {marker}\n1 0 obj<</Type/Catalog>>endobj\ntrailer<<>>\n%%EOF");

    private async Task<HttpClient> NewAuthenticatedClientAsync()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(
            "Caio", $"user-{Guid.CreateVersion7():N}@example.com", "Senha#Forte1"));

        var session = (await response.Content.ReadFromJsonAsync<AuthResponse>(TestJson.Options))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return client;
    }

    private static async Task<(Guid EventId, ContractResponse Contract)> ArrangeAsync(HttpClient client)
    {
        var @event = (await (await client.PostAsJsonAsync("/api/events",
                new CreateEventRequest("Festa de Ano Novo", null, EventDate)))
            .Content.ReadFromJsonAsync<EventDetailsResponse>(TestJson.Options))!;

        var response = await client.PostAsJsonAsync($"/api/events/{@event.Id}/contracts", new CreateContractRequest(
            ContractDirection.Receivable, "Prefeitura Municipal", "Show de encerramento",
            9000m, PaymentMethod.Boleto, 3, FirstDue, new DateOnly(2026, 9, 1)));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (@event.Id, (await response.Content.ReadFromJsonAsync<ContractResponse>(TestJson.Options))!);
    }

    private static MultipartFormDataContent FileContent(byte[] bytes, string fileName, string contentType = "application/pdf")
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    [Fact]
    public async Task Every_contract_route_requires_authentication()
    {
        var anonymous = factory.CreateClient();
        var id = Guid.CreateVersion7();

        var responses = new[]
        {
            await anonymous.GetAsync("/api/contracts"),
            await anonymous.GetAsync($"/api/contracts/{id}"),
            await anonymous.GetAsync($"/api/contracts/{id}/document"),
            await anonymous.PostAsync($"/api/contracts/{id}/installments/1/settle", null),
            await anonymous.GetAsync("/api/reports/cash-flow"),
        };

        responses.Should().AllSatisfy(r => r.StatusCode.Should().Be(HttpStatusCode.Unauthorized));
    }

    [Fact]
    public async Task Full_cycle_of_a_contract_with_installments()
    {
        var client = await NewAuthenticatedClientAsync();
        var (eventId, contract) = await ArrangeAsync(client);

        contract.Installments.Should().HaveCount(3);
        contract.Installments.Sum(i => i.Amount).Should().Be(9000m);

        var settled = await client.PostAsJsonAsync(
            $"/api/contracts/{contract.Id}/installments/1/settle",
            new SettleInstallmentRequest());
        settled.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterSettle = (await settled.Content.ReadFromJsonAsync<ContractResponse>(TestJson.Options))!;
        afterSettle.SettledAmount.Should().Be(3000m);
        afterSettle.OpenAmount.Should().Be(6000m);

        // E o lançamento apareceu no evento.
        var @event = (await client.GetFromJsonAsync<EventDetailsResponse>(
            $"/api/events/{eventId}", TestJson.Options))!;
        @event.TotalIncome.Should().Be(3000m);
    }

    [Fact]
    public async Task A_pdf_can_be_attached_downloaded_and_removed()
    {
        var client = await NewAuthenticatedClientAsync();
        var (_, contract) = await ArrangeAsync(client);
        var bytes = PdfBytes();

        var upload = await client.PostAsync($"/api/contracts/{contract.Id}/document",
            FileContent(bytes, "contrato-assinado.pdf"));
        upload.StatusCode.Should().Be(HttpStatusCode.OK);

        var metadata = (await upload.Content.ReadFromJsonAsync<AttachmentResponse>(TestJson.Options))!;
        metadata.FileName.Should().Be("contrato-assinado.pdf");
        metadata.SizeInBytes.Should().Be(bytes.Length);

        var download = await client.GetAsync($"/api/contracts/{contract.Id}/document");
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        download.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        (await download.Content.ReadAsByteArrayAsync()).Should().Equal(bytes);

        (await client.DeleteAsync($"/api/contracts/{contract.Id}/document"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync($"/api/contracts/{contract.Id}/document"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_file_that_is_not_a_pdf_is_refused_even_disguised()
    {
        var client = await NewAuthenticatedClientAsync();
        var (_, contract) = await ArrangeAsync(client);

        // Extensão e content-type dizem PDF; o conteúdo é HTML com script.
        var disguised = Encoding.ASCII.GetBytes("<html><script>alert(1)</script></html>");

        var response = await client.PostAsync($"/api/contracts/{contract.Id}/document",
            FileContent(disguised, "contrato.pdf"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("PDF");
    }

    [Fact]
    public async Task The_download_always_declares_pdf_and_a_safe_file_name()
    {
        var client = await NewAuthenticatedClientAsync();
        var (_, contract) = await ArrangeAsync(client);

        // Nome com travessia de diretório.
        await client.PostAsync($"/api/contracts/{contract.Id}/document",
            FileContent(PdfBytes(), "../../../web.config"));

        var download = await client.GetAsync($"/api/contracts/{contract.Id}/document");
        var disposition = download.Content.Headers.ContentDisposition!.FileName;

        download.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        disposition.Should().NotContain("..").And.NotContain("/");
    }

    [Fact]
    public async Task Another_users_contract_answers_404_on_every_route()
    {
        var alice = await NewAuthenticatedClientAsync();
        var (_, contract) = await ArrangeAsync(alice);
        await alice.PostAsync($"/api/contracts/{contract.Id}/document", FileContent(PdfBytes(), "contrato.pdf"));

        var bob = await NewAuthenticatedClientAsync();

        var responses = new[]
        {
            await bob.GetAsync($"/api/contracts/{contract.Id}"),
            await bob.GetAsync($"/api/contracts/{contract.Id}/document"),
            await bob.DeleteAsync($"/api/contracts/{contract.Id}"),
            await bob.PostAsJsonAsync($"/api/contracts/{contract.Id}/installments/1/settle",
                new SettleInstallmentRequest()),
            await bob.PostAsync($"/api/contracts/{contract.Id}/document", FileContent(PdfBytes(), "x.pdf")),
        };

        responses.Should().AllSatisfy(r => r.StatusCode.Should().Be(HttpStatusCode.NotFound));
    }

    [Fact]
    public async Task The_cash_flow_report_answers_this_month_versus_the_next_ones()
    {
        var client = await NewAuthenticatedClientAsync();
        var (_, contract) = await ArrangeAsync(client);
        await client.PostAsJsonAsync($"/api/contracts/{contract.Id}/installments/1/settle",
            new SettleInstallmentRequest());

        var report = (await client.GetFromJsonAsync<CashFlowReportResponse>(
            "/api/reports/cash-flow?monthsAhead=4", TestJson.Options))!;

        report.TotalReceivable.Should().Be(6000m, "restam duas parcelas em aberto");
        report.RealizedBalance.Should().Be(3000m);
        report.ProjectedBalance.Should().Be(9000m);
        report.UpcomingMonths.Should().HaveCount(4);
    }

    [Fact]
    public async Task Invalid_contract_data_is_rejected_with_field_errors()
    {
        var client = await NewAuthenticatedClientAsync();
        var (eventId, _) = await ArrangeAsync(client);

        var response = await client.PostAsJsonAsync($"/api/events/{eventId}/contracts", new CreateContractRequest(
            ContractDirection.Receivable, "", null, -100m, PaymentMethod.Pix, 0, FirstDue, EventDate));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Counterparty").And.Contain("TotalAmount").And.Contain("InstallmentCount");
    }
}
