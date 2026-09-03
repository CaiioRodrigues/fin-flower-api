using System.Globalization;
using FinFlower.Application.Abstractions;
using FinFlower.Application.Common;
using FinFlower.Application.Contracts;
using FinFlower.Application.Reports.Dtos;
using FinFlower.Application.Reports.Export;
using FinFlower.Domain.Enums;

namespace FinFlower.Application.Reports;

public interface IReportExportService
{
    Task<Result<ReportFile>> ExportCashFlowAsync(ReportFormat format, int monthsAhead, CancellationToken ct = default);
    Task<Result<ReportFile>> ExportInstallmentsAsync(ReportFormat format, CancellationToken ct = default);
    Task<Result<ReportFile>> ExportCashAsync(ReportFormat format, DateOnly? from, DateOnly? to, CancellationToken ct = default);
    Task<Result<ReportFile>> ExportEventStatementAsync(Guid eventId, ReportFormat format, CancellationToken ct = default);
    Task<Result<ReportFile>> ExportMonthlyCashAsync(ReportFormat format, string? from, string? to, CancellationToken ct = default);
}

/// <summary>
/// Monta os relatórios em formato neutro e entrega ao gerador do formato pedido.
/// Nenhuma regra de apresentação de Excel ou PDF vive aqui.
/// </summary>
public sealed class ReportExportService(
    IEventQueries events,
    IContractQueries contracts,
    ICashReportService cashReport,
    ICashFlowReportService cashFlow,
    Cash.IMonthlyCashService monthlyCash,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IEnumerable<IReportWriter> writers) : IReportExportService
{
    private static readonly CultureInfo Ptbr = CultureInfo.GetCultureInfo("pt-BR");

    private static readonly Dictionary<ContractDirection, string> DirectionLabels = new()
    {
        [ContractDirection.Receivable] = "A receber",
        [ContractDirection.Payable] = "A pagar",
    };

    private static readonly Dictionary<PaymentMethod, string> PaymentLabels = new()
    {
        [PaymentMethod.Pix] = "Pix",
        [PaymentMethod.Boleto] = "Boleto",
        [PaymentMethod.CreditCard] = "Cartão de crédito",
        [PaymentMethod.DebitCard] = "Cartão de débito",
        [PaymentMethod.BankTransfer] = "Transferência",
        [PaymentMethod.Cash] = "Dinheiro",
        [PaymentMethod.Check] = "Cheque",
        [PaymentMethod.Other] = "Outro",
    };

    private static readonly Dictionary<InstallmentStatus, string> InstallmentLabels = new()
    {
        [InstallmentStatus.Pending] = "Em aberto",
        [InstallmentStatus.Settled] = "Liquidada",
        [InstallmentStatus.Canceled] = "Cancelada",
    };

    public async Task<Result<ReportFile>> ExportCashFlowAsync(
        ReportFormat format,
        int monthsAhead,
        CancellationToken ct = default)
    {
        var report = await cashFlow.GetAsync(monthsAhead, ct);
        if (report.IsFailure) return Result.Failure<ReportFile>(report.Error!);

        return Write(format, BuildCashFlow(report.Value));
    }

    public async Task<Result<ReportFile>> ExportInstallmentsAsync(
        ReportFormat format,
        CancellationToken ct = default)
    {
        // Sem recorte de meses: o relatório de cobrança quer tudo que está em aberto.
        var report = await cashFlow.GetAsync(24, ct);
        if (report.IsFailure) return Result.Failure<ReportFile>(report.Error!);

        return Write(format, BuildInstallments(report.Value));
    }

    public async Task<Result<ReportFile>> ExportCashAsync(
        ReportFormat format,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct = default)
    {
        var report = await cashReport.GetAsync(from, to, ct);
        if (report.IsFailure) return Result.Failure<ReportFile>(report.Error!);

        return Write(format, BuildCash(report.Value, from, to));
    }

    public async Task<Result<ReportFile>> ExportEventStatementAsync(
        Guid eventId,
        ReportFormat format,
        CancellationToken ct = default)
    {
        if (currentUser.UserId is not { } ownerId)
        {
            return Result.Failure<ReportFile>(
                Error.Unauthorized("auth.required", "Sessão inválida. Faça login novamente."));
        }

        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        var @event = await events.GetDetailsAsync(eventId, ownerId, ct);
        if (@event is null)
            return Result.Failure<ReportFile>(Error.NotFound("event.not_found", "Evento não encontrado."));

        var summaries = await contracts.ListAsync(ownerId, new ContractFilter(EventId: eventId), today, ct);

        // Um evento tem poucos contratos e o extrato é gerado sob demanda, então
        // buscar as parcelas de cada um sai mais simples que uma consulta nova.
        var details = new List<Dtos.ContractWithInstallments>();
        foreach (var summary in summaries)
        {
            var full = await contracts.GetAsync(summary.Id, ownerId, today, ct);
            if (full is not null) details.Add(new Dtos.ContractWithInstallments(full));
        }

        return Write(format, BuildStatement(@event, details));
    }

    public async Task<Result<ReportFile>> ExportMonthlyCashAsync(
        ReportFormat format,
        string? from,
        string? to,
        CancellationToken ct = default)
    {
        var report = await monthlyCash.GetAsync(from, to, ct);
        if (report.IsFailure) return Result.Failure<ReportFile>(report.Error!);

        return Write(format, BuildMonthlyCash(report.Value));
    }

    private Result<ReportFile> Write(ReportFormat format, ReportDocument document)
    {
        var writer = writers.FirstOrDefault(w => w.Format == format);

        return writer is null
            ? Result.Failure<ReportFile>(Error.Validation("report.unknown_format", "Formato de relatório inválido."))
            : Result.Success(writer.Write(document));
    }

    private ReportDocument BuildCashFlow(CashFlowReportResponse report)
    {
        var months = new List<ReportRow>();
        months.Add(Row(true, "Este mês", report.CurrentMonth.Receivable, report.CurrentMonth.Payable,
            report.CurrentMonth.Net, report.CurrentMonth.InstallmentCount));

        months.AddRange(report.UpcomingMonths.Select(month => Row(false,
            MonthLabel(month.Year, month.Month), month.Receivable, month.Payable, month.Net,
            month.InstallmentCount)));

        return new ReportDocument(
            "fluxo-de-caixa",
            "Fluxo de caixa",
            $"Posição em {report.ReferenceDate.ToString("dd/MM/yyyy", Ptbr)}",
            clock.UtcNow,
            [
                new ReportMetric("Realizado", Money(report.RealizedBalance)),
                new ReportMetric("Vencido a receber", Money(report.Overdue.Receivable)),
                new ReportMetric("Vencido a pagar", Money(report.Overdue.Payable)),
                new ReportMetric("Total a receber", Money(report.TotalReceivable)),
                new ReportMetric("Total a pagar", Money(report.TotalPayable)),
                new ReportMetric("Saldo projetado", Money(report.ProjectedBalance)),
            ],
            [
                new ReportTable("Previsão por mês",
                    [
                        new ReportColumn("Mês"),
                        new ReportColumn("A receber", ReportColumnType.Money),
                        new ReportColumn("A pagar", ReportColumnType.Money),
                        new ReportColumn("Líquido", ReportColumnType.Money),
                        new ReportColumn("Parcelas", ReportColumnType.Count),
                    ],
                    months),
                InstallmentTable("Parcelas vencidas", report.Overdues),
                InstallmentTable("Próximas a vencer", report.NextDue),
            ]);
    }

    private ReportDocument BuildInstallments(CashFlowReportResponse report)
    {
        var all = report.Overdues.Concat(report.NextDue).OrderBy(i => i.DueDate).ToList();

        return new ReportDocument(
            "parcelas-em-aberto",
            "Parcelas a receber e a pagar",
            $"Posição em {report.ReferenceDate.ToString("dd/MM/yyyy", Ptbr)}",
            clock.UtcNow,
            [
                new ReportMetric("Total a receber", Money(report.TotalReceivable)),
                new ReportMetric("Total a pagar", Money(report.TotalPayable)),
                new ReportMetric("Vencido", Money(report.Overdue.Receivable + report.Overdue.Payable)),
                new ReportMetric("Parcelas em aberto", all.Count.ToString(Ptbr)),
            ],
            [
                new ReportTable("Parcelas em aberto",
                    [
                        new ReportColumn("Vencimento", ReportColumnType.Date),
                        new ReportColumn("Situação"),
                        new ReportColumn("Tipo"),
                        new ReportColumn("Contratante"),
                        new ReportColumn("Evento"),
                        new ReportColumn("Parcela", ReportColumnType.Count),
                        new ReportColumn("Pagamento"),
                        new ReportColumn("Valor", ReportColumnType.Money),
                    ],
                    [.. all.Select(i => Row(false,
                        i.DueDate,
                        i.IsOverdue ? "Vencida" : "A vencer",
                        DirectionLabels[i.Direction],
                        i.Counterparty,
                        i.EventName,
                        i.Number,
                        PaymentLabels[i.PaymentMethod],
                        i.Amount))]),
            ]);
    }

    private ReportDocument BuildCash(CashReportResponse report, DateOnly? from, DateOnly? to)
    {
        var period = (from, to) switch
        {
            (null, null) => "Todos os eventos",
            (not null, null) => $"A partir de {from!.Value.ToString("dd/MM/yyyy", Ptbr)}",
            (null, not null) => $"Até {to!.Value.ToString("dd/MM/yyyy", Ptbr)}",
            _ => $"De {from!.Value.ToString("dd/MM/yyyy", Ptbr)} a {to!.Value.ToString("dd/MM/yyyy", Ptbr)}",
        };

        var rows = report.Events
            .Select(e => Row(false, e.Name, e.EventDate, e.TotalIncome, e.TotalExpense, e.Result))
            .ToList();

        rows.Add(Row(true, "Total", null, report.TotalIncome, report.TotalExpense, report.Balance));

        return new ReportDocument(
            "caixa-por-evento",
            "Caixa por evento",
            period,
            clock.UtcNow,
            [
                new ReportMetric("Entradas", Money(report.TotalIncome)),
                new ReportMetric("Saídas", Money(report.TotalExpense)),
                new ReportMetric("Saldo", Money(report.Balance)),
                new ReportMetric("Eventos", report.EventCount.ToString(Ptbr)),
                new ReportMetric("Com lucro", report.ProfitableEventCount.ToString(Ptbr)),
                new ReportMetric("Com prejuízo", report.UnprofitableEventCount.ToString(Ptbr)),
            ],
            [
                new ReportTable("Eventos",
                    [
                        new ReportColumn("Evento"),
                        new ReportColumn("Data", ReportColumnType.Date),
                        new ReportColumn("Entradas", ReportColumnType.Money),
                        new ReportColumn("Saídas", ReportColumnType.Money),
                        new ReportColumn("Resultado", ReportColumnType.Money),
                    ],
                    rows),
            ]);
    }

    /// <summary>
    /// O caixa mês a mês. A coluna de saldo acumulado é a razão de ser deste
    /// relatório: cada linha abre com o fechamento da anterior, então a planilha
    /// lida de cima a baixo conta a história do dinheiro sem nenhuma fórmula.
    /// </summary>
    private ReportDocument BuildMonthlyCash(Cash.Dtos.MonthlyCashResponse report)
    {
        var months = report.Months
            .Select(m => Row(false,
                m.Label,
                m.OpeningBalance,
                m.Income,
                m.Expense,
                m.Result,
                m.ClosingBalance,
                m.FixedExpense,
                m.ProLabore,
                m.EntryCount))
            .ToList();

        months.Add(Row(true,
            "Total do período",
            report.OpeningBalance,
            report.TotalIncome,
            report.TotalExpense,
            report.Result,
            report.ClosingBalance,
            report.TotalFixedExpense,
            report.TotalProLabore,
            report.Months.Sum(m => m.EntryCount)));

        // As categorias de todos os meses somadas: é a pergunta seguinte de quem
        // olha o resultado — "onde foi parar o dinheiro".
        var expenseByCategory = report.Months
            .SelectMany(m => m.ExpenseByCategory)
            .GroupBy(c => c.Category)
            .Select(g => Row(false, g.Key, g.Sum(c => c.Amount), g.Sum(c => c.Count)))
            .OrderByDescending(r => (decimal)r.Cells[1]!)
            .ToList();

        var incomeByCategory = report.Months
            .SelectMany(m => m.IncomeByCategory)
            .GroupBy(c => c.Category)
            .Select(g => Row(false, g.Key, g.Sum(c => c.Amount), g.Sum(c => c.Count)))
            .OrderByDescending(r => (decimal)r.Cells[1]!)
            .ToList();

        return new ReportDocument(
            "caixa-mensal",
            "Caixa mês a mês",
            $"De {report.From} a {report.To}",
            clock.UtcNow,
            [
                new ReportMetric("Saldo inicial", Money(report.OpeningBalance)),
                new ReportMetric("Entradas", Money(report.TotalIncome)),
                new ReportMetric("Saídas", Money(report.TotalExpense)),
                new ReportMetric("Resultado", Money(report.Result)),
                new ReportMetric("Saldo final", Money(report.ClosingBalance)),
                new ReportMetric("Média mensal", Money(report.AverageMonthlyResult)),
                new ReportMetric("Custos fixos", Money(report.TotalFixedExpense)),
                new ReportMetric("Pró-labore", Money(report.TotalProLabore)),
            ],
            [
                new ReportTable("Mês a mês",
                    [
                        new ReportColumn("Mês"),
                        new ReportColumn("Saldo inicial", ReportColumnType.Money),
                        new ReportColumn("Entradas", ReportColumnType.Money),
                        new ReportColumn("Saídas", ReportColumnType.Money),
                        new ReportColumn("Resultado", ReportColumnType.Money),
                        new ReportColumn("Saldo final", ReportColumnType.Money),
                        new ReportColumn("Custos fixos", ReportColumnType.Money),
                        new ReportColumn("Pró-labore", ReportColumnType.Money),
                        new ReportColumn("Lançamentos", ReportColumnType.Count),
                    ],
                    months),
                new ReportTable("Saídas por categoria",
                    [
                        new ReportColumn("Categoria"),
                        new ReportColumn("Total", ReportColumnType.Money),
                        new ReportColumn("Lançamentos", ReportColumnType.Count),
                    ],
                    expenseByCategory),
                new ReportTable("Entradas por categoria",
                    [
                        new ReportColumn("Categoria"),
                        new ReportColumn("Total", ReportColumnType.Money),
                        new ReportColumn("Lançamentos", ReportColumnType.Count),
                    ],
                    incomeByCategory),
            ]);
    }

    private ReportDocument BuildStatement(
        Events.Dtos.EventDetailsResponse @event,
        List<Dtos.ContractWithInstallments> contractDetails)
    {
        var entries = @event.Entries
            .Select(e => Row(false,
                e.OccurredOn,
                e.Type == EntryType.Income ? "Entrada" : "Saída",
                e.Description,
                e.Category,
                e.Type == EntryType.Income ? e.Amount : -e.Amount))
            .ToList();

        entries.Add(Row(true, null, null, "Resultado do evento", null, @event.Result));

        var tables = new List<ReportTable>
        {
            new("Lançamentos",
                [
                    new ReportColumn("Data", ReportColumnType.Date),
                    new ReportColumn("Tipo"),
                    new ReportColumn("Descrição"),
                    new ReportColumn("Categoria"),
                    new ReportColumn("Valor", ReportColumnType.Money),
                ],
                entries),
        };

        if (contractDetails.Count > 0)
        {
            tables.Add(new ReportTable("Contratos",
                [
                    new ReportColumn("Contratante"),
                    new ReportColumn("Tipo"),
                    new ReportColumn("Pagamento"),
                    new ReportColumn("Parcelas", ReportColumnType.Count),
                    new ReportColumn("Total", ReportColumnType.Money),
                    new ReportColumn("Liquidado", ReportColumnType.Money),
                    new ReportColumn("Em aberto", ReportColumnType.Money),
                ],
                [.. contractDetails.Select(c => Row(false,
                    c.Contract.Counterparty,
                    DirectionLabels[c.Contract.Direction],
                    PaymentLabels[c.Contract.PaymentMethod],
                    c.Contract.Installments.Count,
                    c.Contract.TotalAmount,
                    c.Contract.SettledAmount,
                    c.Contract.OpenAmount))]));

            tables.Add(new ReportTable("Parcelas",
                [
                    new ReportColumn("Contratante"),
                    new ReportColumn("Parcela"),
                    new ReportColumn("Vencimento", ReportColumnType.Date),
                    new ReportColumn("Situação"),
                    new ReportColumn("Valor", ReportColumnType.Money),
                    new ReportColumn("Liquidado em", ReportColumnType.Date),
                ],
                [.. contractDetails.SelectMany(c => c.Contract.Installments.Select(i => Row(false,
                    c.Contract.Counterparty,
                    $"{i.Number}/{c.Contract.Installments.Count}",
                    i.DueDate,
                    i.IsOverdue ? "Vencida" : InstallmentLabels[i.Status],
                    i.SettledAmount ?? i.Amount,
                    i.SettledOn)))]));
        }

        return new ReportDocument(
            $"extrato-{Slug(@event.Name)}",
            $"Extrato — {@event.Name}",
            $"Evento de {@event.EventDate.ToString("dd/MM/yyyy", Ptbr)} · "
                + (@event.Status == EventStatus.Closed ? "fechado" : "aberto"),
            clock.UtcNow,
            [
                new ReportMetric("Entradas", Money(@event.TotalIncome)),
                new ReportMetric("Saídas", Money(@event.TotalExpense)),
                new ReportMetric("Resultado", Money(@event.Result)),
                new ReportMetric("Lançamentos", @event.Entries.Count.ToString(Ptbr)),
            ],
            tables);
    }

    private static ReportTable InstallmentTable(string title, IReadOnlyList<ScheduledInstallmentResponse> rows) =>
        new(title,
            [
                new ReportColumn("Vencimento", ReportColumnType.Date),
                new ReportColumn("Contratante"),
                new ReportColumn("Evento"),
                new ReportColumn("Tipo"),
                new ReportColumn("Pagamento"),
                new ReportColumn("Valor", ReportColumnType.Money),
            ],
            [.. rows.Select(i => Row(false,
                i.DueDate, i.Counterparty, i.EventName,
                DirectionLabels[i.Direction], PaymentLabels[i.PaymentMethod], i.Amount))]);

    private static ReportRow Row(bool emphasized, params object?[] cells) => new(cells, emphasized);

    private static string Money(decimal value) => value.ToString("C2", Ptbr);

    private static string MonthLabel(int year, int month) =>
        new DateOnly(year, month, 1).ToString("MMM/yyyy", Ptbr);

    /// <summary>Nome de arquivo sem acento nem espaço, para não depender do sistema.</summary>
    private static string Slug(string value)
    {
        var normalized = value.Normalize(System.Text.NormalizationForm.FormD);
        var ascii = new string([.. normalized.Where(c =>
            System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)]);

        var slug = new string([.. ascii.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-')]);

        return string.Join('-', slug.Split('-', StringSplitOptions.RemoveEmptyEntries));
    }
}
