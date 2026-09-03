using FinFlower.Application.Common;
using FinFlower.Application.Contracts;
using FinFlower.Application.Entries.Dtos;
using FinFlower.Application.Events.Dtos;
using FinFlower.Application.Quotes.Dtos;
using FinFlower.Domain.Common;
using FinFlower.Domain.Enums;
using FluentAssertions;

namespace FinFlower.Application.Tests;

/// <summary>
/// Orçamentos e sua virada em contrato — o momento em que uma venda passa a ser
/// previsão de caixa.
/// </summary>
public class QuoteServiceTests
{
    private static readonly DateOnly Issued = new(2026, 9, 1);
    private static readonly DateOnly Valid = new(2026, 9, 30);

    private static Task<Result<QuoteResponse>> NewQuote(
        EventTestContext ctx,
        Guid? eventId = null,
        string? number = null) =>
        ctx.Quotes.CreateAsync(new CreateQuoteRequest(
            "Prefeitura Municipal", "Show de encerramento", Issued, Valid, null, eventId, number));

    private static async Task<QuoteResponse> WithItemsAsync(EventTestContext ctx, Guid? eventId = null)
    {
        var quote = (await NewQuote(ctx, eventId)).Value;
        await ctx.Quotes.AddItemAsync(quote.Id, new QuoteItemRequest("Estrutura de palco", 1m, 12_000m, "un"));
        return (await ctx.Quotes.AddItemAsync(
            quote.Id, new QuoteItemRequest("Equipe técnica", 3m, 850m, "diária"))).Value;
    }

    [Fact]
    public async Task A_new_quote_is_numbered_automatically_by_year()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();

        var first = (await NewQuote(ctx)).Value;
        var second = (await NewQuote(ctx)).Value;

        first.Number.Should().Be("ORC-2026-0001");
        second.Number.Should().Be("ORC-2026-0002");
    }

    [Fact]
    public async Task A_number_chosen_by_hand_is_respected_and_cannot_repeat()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        await NewQuote(ctx, number: "PROP-42");

        var duplicate = await NewQuote(ctx, number: "PROP-42");

        duplicate.Error!.Code.Should().Be("quote.duplicate_number");
    }

    [Fact]
    public async Task Items_build_the_total()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();

        var quote = await WithItemsAsync(ctx);

        quote.Subtotal.Should().Be(14_550m);
        quote.Total.Should().Be(14_550m);
        quote.Items.Select(i => i.Position).Should().Equal(1, 2);
    }

    [Fact]
    public async Task A_discount_lowers_the_total()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        var quote = await WithItemsAsync(ctx);

        var discounted = (await ctx.Quotes.ApplyDiscountAsync(quote.Id, new ApplyDiscountRequest(550m))).Value;

        discounted.DiscountAmount.Should().Be(550m);
        discounted.Total.Should().Be(14_000m);
    }

    [Fact]
    public async Task Approving_generates_a_contract_with_the_installments()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        var quote = await WithItemsAsync(ctx);
        await ctx.Quotes.ApplyDiscountAsync(quote.Id, new ApplyDiscountRequest(550m));

        var approved = (await ctx.Quotes.ApproveAsync(quote.Id, new ApproveQuoteRequest(
            PaymentMethod.Boleto, 4, new DateOnly(2026, 10, 5), Issued))).Value;

        approved.Status.Should().Be(QuoteStatus.Approved);
        approved.ContractId.Should().NotBeNull();

        var contract = (await ctx.Contracts.GetAsync(approved.ContractId!.Value)).Value;
        contract.TotalAmount.Should().Be(14_000m, "o contrato nasce com o total já descontado");
        contract.Direction.Should().Be(ContractDirection.Receivable);
        contract.Counterparty.Should().Be("Prefeitura Municipal");
        contract.QuoteNumber.Should().Be(quote.Number);
        contract.Installments.Should().HaveCount(4);
        contract.Installments.Sum(i => i.Amount).Should().Be(14_000m);
        contract.Installments[0].DueDate.Should().Be(new DateOnly(2026, 10, 5));
    }

    [Fact]
    public async Task An_approved_quote_carries_its_event_into_the_contract()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        var @event = (await ctx.Events.CreateAsync(
            new CreateEventRequest("Aniversário da cidade", null, new DateOnly(2026, 11, 20)))).Value;
        var quote = await WithItemsAsync(ctx, @event.Id);

        var approved = (await ctx.Quotes.ApproveAsync(quote.Id, new ApproveQuoteRequest(
            PaymentMethod.Pix, 1, new DateOnly(2026, 11, 20), Issued))).Value;

        var contract = (await ctx.Contracts.GetAsync(approved.ContractId!.Value)).Value;
        contract.EventId.Should().Be(@event.Id);
        contract.EventName.Should().Be("Aniversário da cidade");
    }

    [Fact]
    public async Task Settling_an_installment_of_an_approved_quote_reaches_the_cash_box()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        var quote = await WithItemsAsync(ctx);
        var approved = (await ctx.Quotes.ApproveAsync(quote.Id, new ApproveQuoteRequest(
            PaymentMethod.Boleto, 2, new DateOnly(2026, 10, 5), Issued))).Value;

        await ctx.Contracts.SettleInstallmentAsync(
            approved.ContractId!.Value, 1, new Contracts.Dtos.SettleInstallmentRequest());

        // O caminho inteiro: orçamento → contrato → parcela → caixa.
        var october = (await ctx.MonthlyCash.GetAsync("2026-10", "2026-10")).Value.Months.Single();
        october.Income.Should().Be(7_275m);
        october.ContractIncome.Should().Be(7_275m);

        var entry = (await ctx.Entries.ListAsync(new EntryFilter(), 1, 50)).Value.Entries.Single();
        entry.Source.Should().Be(EntrySource.Contract);
        entry.IsEditable.Should().BeFalse();
    }

    [Fact]
    public async Task An_empty_quote_cannot_be_approved_and_leaves_no_contract_behind()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        var quote = (await NewQuote(ctx)).Value;

        var act = async () => await ctx.Quotes.ApproveAsync(quote.Id, new ApproveQuoteRequest(
            PaymentMethod.Pix, 1, new DateOnly(2026, 10, 5), Issued));

        await act.Should().ThrowAsync<DomainException>().WithMessage("*sem itens*");

        // O contrato só é gravado depois do aval do domínio: nada fica órfão.
        (await ctx.Contracts.ListAsync(new ContractFilter())).Value.Should().BeEmpty();
    }

    [Fact]
    public async Task An_approved_quote_no_longer_changes()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        var quote = await WithItemsAsync(ctx);
        await ctx.Quotes.ApproveAsync(quote.Id, new ApproveQuoteRequest(
            PaymentMethod.Pix, 1, new DateOnly(2026, 10, 5), Issued));

        var act = async () => await ctx.Quotes.AddItemAsync(
            quote.Id, new QuoteItemRequest("Extra", 1m, 500m, null));

        await act.Should().ThrowAsync<DomainException>().WithMessage("*já virou contrato*");
    }

    [Fact]
    public async Task An_approved_quote_cannot_be_deleted()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        var quote = await WithItemsAsync(ctx);
        await ctx.Quotes.ApproveAsync(quote.Id, new ApproveQuoteRequest(
            PaymentMethod.Pix, 1, new DateOnly(2026, 10, 5), Issued));

        (await ctx.Quotes.DeleteAsync(quote.Id)).Error!.Code.Should().Be("quote.already_approved");
    }

    [Fact]
    public async Task A_rejected_quote_can_be_reopened_and_renegotiated()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        var quote = await WithItemsAsync(ctx);
        await ctx.Quotes.SendAsync(quote.Id);
        await ctx.Quotes.RejectAsync(quote.Id);

        await ctx.Quotes.ReopenAsync(quote.Id);
        var renegotiated = (await ctx.Quotes.ApplyDiscountAsync(
            quote.Id, new ApplyDiscountRequest(2_000m))).Value;

        renegotiated.Status.Should().Be(QuoteStatus.Draft);
        renegotiated.Total.Should().Be(12_550m);
    }

    [Fact]
    public async Task Expiry_is_reported_from_the_clock()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        var quote = await WithItemsAsync(ctx);

        (await ctx.Quotes.GetAsync(quote.Id)).Value.IsExpired.Should().BeFalse();

        ctx.Clock.UtcNow = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        (await ctx.Quotes.GetAsync(quote.Id)).Value.IsExpired.Should().BeTrue();
    }

    [Fact]
    public async Task The_listing_can_be_filtered_by_status()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        var draft = await WithItemsAsync(ctx);
        var sent = await WithItemsAsync(ctx);
        await ctx.Quotes.SendAsync(sent.Id);

        var sentOnly = (await ctx.Quotes.ListAsync(new QuoteFilter(Status: QuoteStatus.Sent))).Value;

        sentOnly.Should().ContainSingle().Which.Id.Should().Be(sent.Id);
        sentOnly.Single().Total.Should().Be(14_550m);
        draft.Status.Should().Be(QuoteStatus.Draft);
    }

    [Fact]
    public async Task Another_users_quote_is_out_of_reach()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        var quote = await WithItemsAsync(ctx);

        ctx.ActAs();
        var read = await ctx.Quotes.GetAsync(quote.Id);
        var addItem = await ctx.Quotes.AddItemAsync(quote.Id, new QuoteItemRequest("x", 1m, 1m, null));
        var approve = await ctx.Quotes.ApproveAsync(quote.Id, new ApproveQuoteRequest(
            PaymentMethod.Pix, 1, new DateOnly(2026, 10, 5), Issued));

        new[] { read.Error, addItem.Error, approve.Error }
            .Should().AllSatisfy(error => error!.Type.Should().Be(ErrorType.NotFound));

        (await ctx.Quotes.ListAsync(new QuoteFilter())).Value.Should().BeEmpty();
    }

    [Fact]
    public async Task The_same_number_can_repeat_across_different_users()
    {
        using var ctx = new EventTestContext();
        ctx.ActAs();
        await NewQuote(ctx, number: "PROP-1");

        ctx.ActAs();
        var other = await NewQuote(ctx, number: "PROP-1");

        // A unicidade é por dono: o número de um não pode limitar o do outro.
        other.IsSuccess.Should().BeTrue();
    }
}
