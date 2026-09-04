using System.Globalization;
using FinFlower.Application.Abstractions;
using FinFlower.Application.Quotes.Dtos;
using FinFlower.Application.Reports.Export;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace FinFlower.Infrastructure.Reports;

/// <summary>
/// A proposta comercial em PDF — o documento que o cliente recebe.
///
/// Duas decisões guiam o desenho. A primeira é que quem lê não conhece o
/// sistema: nada de jargão interno, nenhum id, nenhuma sigla. A segunda é que a
/// pergunta do cliente é sempre a mesma — o que estou contratando, por quanto e
/// até quando isso vale —, então essas três respostas ficam onde o olho cai
/// primeiro.
/// </summary>
public sealed class QuoteProposalWriter : IQuoteProposalWriter
{
    private static readonly CultureInfo Ptbr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly Color Primary = Color.FromHex("#2563eb");
    private static readonly Color PrimaryDark = Color.FromHex("#1d4ed8");
    private static readonly Color Ink = Color.FromHex("#101828");
    private static readonly Color Muted = Color.FromHex("#667085");
    private static readonly Color Line = Color.FromHex("#e3e8ef");
    private static readonly Color Panel = Color.FromHex("#f6f8fb");

    public ReportFile Write(QuoteProposal proposal)
    {
        var bytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(style => style.FontSize(10).FontColor(Ink).FontFamily(Fonts.Calibri));

                page.Header().Element(header => Header(header, proposal));
                page.Content().PaddingTop(18).Element(content => Content(content, proposal));
                page.Footer().Element(footer => Footer(footer, proposal));
            });
        }).GeneratePdf();

        // O nome do arquivo é a primeira coisa que o cliente vê no anexo: o
        // número do orçamento diz mais que "documento.pdf".
        return new ReportFile($"proposta-{Slug(proposal.Number)}.pdf", "application/pdf", bytes);
    }

    private static void Header(IContainer container, QuoteProposal proposal) =>
        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                row.ConstantItem(46).Height(46).Svg(BrandMark.Svg);

                row.RelativeItem().PaddingLeft(12).Column(identity =>
                {
                    identity.Item().Text(proposal.IssuerName).FontSize(16).Bold();
                    identity.Item().Text(proposal.IssuerEmail).FontSize(9).FontColor(Muted);
                });

                row.ConstantItem(190).AlignRight().Column(reference =>
                {
                    reference.Item().Text("PROPOSTA COMERCIAL").FontSize(9)
                        .LetterSpacing(0.12f).FontColor(Primary).SemiBold();
                    reference.Item().Text(proposal.Number).FontSize(18).Bold();
                    reference.Item().Text($"Emitida em {Date(proposal.IssuedOn)}")
                        .FontSize(9).FontColor(Muted);
                });
            });

            column.Item().PaddingTop(12).LineHorizontal(2).LineColor(Primary);
        });

    private static void Content(IContainer container, QuoteProposal proposal) =>
        container.Column(column =>
        {
            column.Spacing(18);

            column.Item().Element(block => ClientBlock(block, proposal));
            column.Item().Element(block => Items(block, proposal));
            column.Item().Element(block => Totals(block, proposal));

            if (!string.IsNullOrWhiteSpace(proposal.Notes))
                column.Item().Element(block => Notes(block, proposal.Notes));

            column.Item().Element(Acceptance);
        });

    private static void ClientBlock(IContainer container, QuoteProposal proposal) =>
        container.Background(Panel).Padding(14).Row(row =>
        {
            row.RelativeItem().Column(client =>
            {
                client.Item().Text("PARA").FontSize(8).LetterSpacing(0.1f).FontColor(Muted);
                client.Item().Text(proposal.ClientName).FontSize(13).SemiBold();

                if (!string.IsNullOrWhiteSpace(proposal.EventName))
                    client.Item().Text(proposal.EventName).FontSize(9).FontColor(Muted);
            });

            row.RelativeItem().Column(subject =>
            {
                subject.Item().Text("REFERENTE A").FontSize(8).LetterSpacing(0.1f).FontColor(Muted);
                subject.Item().Text(proposal.Title).FontSize(13).SemiBold();
            });

            row.ConstantItem(150).AlignRight().Column(validity =>
            {
                validity.Item().AlignRight().Text("VÁLIDA ATÉ").FontSize(8)
                    .LetterSpacing(0.1f).FontColor(Muted);
                validity.Item().AlignRight().Text(Date(proposal.ValidUntil)).FontSize(13).SemiBold();

                // O aviso é para quem guardou o arquivo e voltou a ele meses
                // depois — no papel não há nada que atualize a validade sozinho.
                if (proposal.IsExpired)
                {
                    validity.Item().AlignRight().Text("prazo vencido")
                        .FontSize(8).FontColor(Color.FromHex("#c1121f"));
                }
            });
        });

    private static void Items(IContainer container, QuoteProposal proposal) =>
        container.Column(column =>
        {
            column.Item().PaddingBottom(6).Text("O que está incluído").FontSize(12).SemiBold();

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.ConstantColumn(70);
                    columns.ConstantColumn(80);
                    columns.ConstantColumn(84);
                });

                table.Header(header =>
                {
                    Head(header, "Item", left: true);
                    Head(header, "Qtd.");
                    Head(header, "Valor unit.");
                    Head(header, "Total");
                });

                foreach (var item in proposal.Items)
                {
                    table.Cell().BorderBottom(0.5f).BorderColor(Line).PaddingVertical(7)
                        .Text(item.Description);

                    table.Cell().BorderBottom(0.5f).BorderColor(Line).PaddingVertical(7)
                        .AlignRight().Text(Quantity(item));

                    table.Cell().BorderBottom(0.5f).BorderColor(Line).PaddingVertical(7)
                        .AlignRight().Text(Money(item.UnitPrice));

                    table.Cell().BorderBottom(0.5f).BorderColor(Line).PaddingVertical(7)
                        .AlignRight().Text(Money(item.Total)).SemiBold();
                }
            });
        });

    private static void Head(TableCellDescriptor header, string text, bool left = false)
    {
        var cell = header.Cell().BorderBottom(1).BorderColor(PrimaryDark).PaddingBottom(5);
        var content = left ? cell : cell.AlignRight();
        content.Text(text).FontSize(9).SemiBold().FontColor(PrimaryDark);
    }

    private static void Totals(IContainer container, QuoteProposal proposal) =>
        container.AlignRight().Width(260).Column(column =>
        {
            if (proposal.DiscountAmount > 0)
            {
                Line2(column, "Subtotal", Money(proposal.Subtotal), emphasis: false);
                Line2(column, "Desconto", $"− {Money(proposal.DiscountAmount)}", emphasis: false);
            }

            column.Item().PaddingTop(6).Background(Primary).Padding(10).Row(row =>
            {
                row.RelativeItem().Text("Total").FontSize(12).SemiBold().FontColor(Colors.White);
                row.ConstantItem(140).AlignRight().Text(Money(proposal.Total))
                    .FontSize(15).Bold().FontColor(Colors.White);
            });
        });

    private static void Line2(ColumnDescriptor column, string label, string value, bool emphasis)
    {
        column.Item().PaddingVertical(3).Row(row =>
        {
            row.RelativeItem().Text(label).FontColor(Muted);
            var cell = row.ConstantItem(120).AlignRight().Text(value);
            if (emphasis) cell.SemiBold();
        });
    }

    private static void Notes(IContainer container, string? notes) =>
        container.Column(column =>
        {
            column.Item().PaddingBottom(4).Text("Condições").FontSize(12).SemiBold();
            column.Item().Text(notes).FontColor(Muted).LineHeight(1.45f);
        });

    /// <summary>
    /// A linha de aceite. Uma proposta sem lugar para o "de acordo" obriga o
    /// cliente a responder por fora, e o combinado fica só no e-mail.
    /// </summary>
    private static void Acceptance(IContainer container) =>
        container.PaddingTop(16).Column(column =>
        {
            column.Item().Text("De acordo").FontSize(12).SemiBold();
            column.Item().PaddingTop(26).Row(row =>
            {
                row.RelativeItem().Column(signature =>
                {
                    signature.Item().LineHorizontal(0.8f).LineColor(Muted);
                    signature.Item().PaddingTop(4).Text("Assinatura do contratante")
                        .FontSize(9).FontColor(Muted);
                });

                row.ConstantItem(30);

                row.ConstantItem(150).Column(date =>
                {
                    date.Item().LineHorizontal(0.8f).LineColor(Muted);
                    date.Item().PaddingTop(4).Text("Data").FontSize(9).FontColor(Muted);
                });
            });
        });

    private static void Footer(IContainer container, QuoteProposal proposal) =>
        container.PaddingTop(10).BorderTop(0.5f).BorderColor(Line).PaddingTop(6).Row(row =>
        {
            row.RelativeItem().Text($"{proposal.Number} · {proposal.IssuerName}")
                .FontSize(8).FontColor(Muted);

            row.ConstantItem(200).AlignRight().Text(text =>
            {
                text.DefaultTextStyle(style => style.FontSize(8).FontColor(Muted));
                text.Span($"Gerada em {proposal.GeneratedAt.LocalDateTime.ToString("dd/MM/yyyy", Ptbr)} · página ");
                text.CurrentPageNumber();
                text.Span(" de ");
                text.TotalPages();
            });
        });

    private static string Date(DateOnly value) => value.ToString("dd/MM/yyyy", Ptbr);

    private static string Money(decimal value) => value.ToString("C2", Ptbr);

    /// <summary>Quantidade sem casas à toa: "3" em vez de "3,000".</summary>
    private static string Quantity(QuoteItemResponse item)
    {
        var number = item.Quantity.ToString("0.###", Ptbr);
        return string.IsNullOrWhiteSpace(item.Unit) ? number : $"{number} {item.Unit}";
    }

    private static string Slug(string value) =>
        string.Concat(value.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-'))
            .Trim('-');
}
