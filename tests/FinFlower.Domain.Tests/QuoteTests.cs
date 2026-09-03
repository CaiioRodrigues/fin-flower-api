using FinFlower.Domain.Common;
using FinFlower.Domain.Entities;
using FinFlower.Domain.Enums;
using FluentAssertions;

namespace FinFlower.Domain.Tests;

public class QuoteTests
{
    private static readonly DateOnly Issued = new(2026, 9, 1);
    private static readonly DateOnly Valid = new(2026, 9, 30);

    private static Quote NewQuote() => new(
        Guid.CreateVersion7(), "ORC-2026-0001", "Prefeitura Municipal",
        "Show de encerramento", Issued, Valid, null, null);

    private static Quote WithItems()
    {
        var quote = NewQuote();
        quote.AddItem("Estrutura de palco", 1m, 12_000m, "un");
        quote.AddItem("Equipe técnica", 3m, 850m, "diária");
        return quote;
    }

    [Fact]
    public void Total_is_the_sum_of_the_lines()
    {
        var quote = WithItems();

        quote.Subtotal.Should().Be(14_550m, "12000 + 3 × 850");
        quote.Total.Should().Be(14_550m);
        quote.Items.Should().HaveCount(2);
    }

    [Fact]
    public void Each_line_is_rounded_before_the_sum()
    {
        var quote = NewQuote();
        quote.AddItem("Hora técnica", 3m, 33.333m, "h");

        // O unitário é arredondado na entrada, e o total da linha sai do valor
        // arredondado: o cliente lê "3 × R$ 33,33 = R$ 99,99" e a conta fecha.
        // Guardar 33,333 e só arredondar no fim daria 100,00 numa linha que
        // mostra 33,33 — um centavo que ninguém consegue explicar.
        quote.Items.Single().UnitPrice.Should().Be(33.33m);
        quote.Items.Single().Total.Should().Be(99.99m);
        quote.Subtotal.Should().Be(99.99m);
    }

    [Fact]
    public void Discount_comes_off_the_subtotal()
    {
        var quote = WithItems();

        quote.ApplyDiscount(550m);

        quote.Total.Should().Be(14_000m);
    }

    [Fact]
    public void Discount_cannot_exceed_the_subtotal()
    {
        var quote = WithItems();

        quote.Invoking(q => q.ApplyDiscount(20_000m))
            .Should().Throw<DomainException>().WithMessage("*maior que o subtotal*");
    }

    [Fact]
    public void Items_are_numbered_in_order_and_renumbered_after_a_removal()
    {
        var quote = WithItems();
        quote.AddItem("Iluminação", 1m, 2000m, null);
        var middle = quote.OrderedItems[1];

        quote.RemoveItem(middle.Id);

        quote.OrderedItems.Select(i => i.Position).Should().Equal(1, 2);
        quote.OrderedItems.Select(i => i.Description)
            .Should().Equal("Estrutura de palco", "Iluminação");
    }

    [Fact]
    public void Empty_quote_cannot_be_sent_or_approved()
    {
        var quote = NewQuote();

        quote.Invoking(q => q.MarkAsSent())
            .Should().Throw<DomainException>().WithMessage("*sem itens*");

        quote.Invoking(q => q.Approve(Guid.CreateVersion7()))
            .Should().Throw<DomainException>().WithMessage("*sem itens*");
    }

    [Fact]
    public void Approval_records_the_contract_and_freezes_the_quote()
    {
        var quote = WithItems();
        var contractId = Guid.CreateVersion7();

        quote.Approve(contractId);

        quote.Status.Should().Be(QuoteStatus.Approved);
        quote.ContractId.Should().Be(contractId);
        quote.IsEditable.Should().BeFalse();

        // Alterar depois de virar contrato deixaria os dois em desacordo.
        quote.Invoking(q => q.AddItem("Extra", 1m, 100m, null))
            .Should().Throw<DomainException>().WithMessage("*já virou contrato*");

        quote.Invoking(q => q.Approve(Guid.CreateVersion7()))
            .Should().Throw<DomainException>().WithMessage("*já foi aprovado*");
    }

    [Fact]
    public void A_rejected_quote_has_to_be_reopened_before_changing()
    {
        var quote = WithItems();
        quote.Reject();

        quote.Invoking(q => q.AddItem("Extra", 1m, 100m, null))
            .Should().Throw<DomainException>().WithMessage("*Reabra*");

        quote.Reopen();
        quote.Status.Should().Be(QuoteStatus.Draft);
        quote.AddItem("Extra", 1m, 100m, null);
        quote.Items.Should().HaveCount(3);
    }

    [Fact]
    public void Expiry_is_read_from_the_date_not_stored()
    {
        var quote = WithItems();

        quote.IsExpired(new DateOnly(2026, 9, 30)).Should().BeFalse();
        quote.IsExpired(new DateOnly(2026, 10, 1)).Should().BeTrue();

        quote.Approve(Guid.CreateVersion7());
        quote.IsExpired(new DateOnly(2027, 1, 1))
            .Should().BeFalse("orçamento aprovado não vence");
    }

    [Fact]
    public void Sending_is_only_possible_from_a_draft()
    {
        var quote = WithItems();
        quote.MarkAsSent();

        quote.Status.Should().Be(QuoteStatus.Sent);
        quote.IsEditable.Should().BeTrue("ainda dá para renegociar com o cliente");

        quote.Invoking(q => q.MarkAsSent())
            .Should().Throw<DomainException>().WithMessage("*rascunho*");
    }

    [Fact]
    public void Validity_cannot_precede_the_issue_date()
    {
        var act = () => new Quote(
            Guid.CreateVersion7(), "ORC-1", "Cliente", "Serviço",
            new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 1), null, null);

        act.Should().Throw<DomainException>().WithMessage("*anterior à data de emissão*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Item_quantity_and_price_must_be_positive(decimal value)
    {
        var quote = NewQuote();

        quote.Invoking(q => q.AddItem("Serviço", value, 100m, null)).Should().Throw<DomainException>();
        quote.Invoking(q => q.AddItem("Serviço", 1m, value, null)).Should().Throw<DomainException>();
    }
}
