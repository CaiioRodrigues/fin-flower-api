using FinFlower.Application.Abstractions;
using FinFlower.Application.Contracts;
using FinFlower.Application.Contracts.Dtos;
using FinFlower.Application.Reports.Dtos;
using FinFlower.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinFlower.Infrastructure.Persistence.Queries;

public sealed class ContractQueries(AppDbContext context) : IContractQueries
{
    /// <summary>Quantas parcelas futuras o fluxo de caixa mostra por padrão.</summary>
    private const int MaxUpcomingListed = 20;

    public async Task<IReadOnlyList<ContractSummaryResponse>> ListAsync(
        Guid ownerId,
        ContractFilter filter,
        DateOnly today,
        CancellationToken cancellationToken = default)
    {
        var query = context.Contracts.AsNoTracking().Where(c => c.OwnerId == ownerId);

        if (filter.EventId is { } eventId) query = query.Where(c => c.EventId == eventId);
        if (filter.Direction is { } direction) query = query.Where(c => c.Direction == direction);
        if (filter.OnlyOpen == true)
            query = query.Where(c => c.Installments.Any(i => i.Status == InstallmentStatus.Pending));

        var rows = await query
            .OrderByDescending(c => c.SignedOn)
            .Select(c => new
            {
                c.Id,
                c.EventId,
                EventName = context.Events.Where(e => e.Id == c.EventId).Select(e => e.Name).First(),
                c.Direction,
                c.Counterparty,
                c.TotalAmount,
                c.PaymentMethod,
                Settled = c.Installments
                    .Where(i => i.Status == InstallmentStatus.Settled)
                    .Sum(i => (decimal?)(i.SettledAmount ?? i.Amount)) ?? 0m,
                Open = c.Installments
                    .Where(i => i.Status == InstallmentStatus.Pending)
                    .Sum(i => (decimal?)i.Amount) ?? 0m,
                Overdue = c.Installments
                    .Where(i => i.Status == InstallmentStatus.Pending && i.DueDate < today)
                    .Sum(i => (decimal?)i.Amount) ?? 0m,
                NextDueDate = c.Installments
                    .Where(i => i.Status == InstallmentStatus.Pending)
                    .Min(i => (DateOnly?)i.DueDate),
                InstallmentCount = c.Installments.Count,
                HasAttachment = context.ContractAttachments.Any(a => a.ContractId == c.Id),
            })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(r => new ContractSummaryResponse(
            r.Id, r.EventId, r.EventName, r.Direction, r.Counterparty, r.TotalAmount,
            r.PaymentMethod, r.Settled, r.Open, r.Overdue, r.NextDueDate, r.InstallmentCount,
            r.HasAttachment))];
    }

    public async Task<ContractResponse?> GetAsync(
        Guid contractId,
        Guid ownerId,
        DateOnly today,
        CancellationToken cancellationToken = default)
    {
        var row = await context.Contracts
            .AsNoTracking()
            .Where(c => c.Id == contractId && c.OwnerId == ownerId)
            .Select(c => new
            {
                Contract = c,
                Settled = c.Installments
                    .Where(i => i.Status == InstallmentStatus.Settled)
                    .Sum(i => (decimal?)(i.SettledAmount ?? i.Amount)) ?? 0m,
                Open = c.Installments
                    .Where(i => i.Status == InstallmentStatus.Pending)
                    .Sum(i => (decimal?)i.Amount) ?? 0m,
                Overdue = c.Installments
                    .Where(i => i.Status == InstallmentStatus.Pending && i.DueDate < today)
                    .Sum(i => (decimal?)i.Amount) ?? 0m,
                Installments = c.Installments
                    .OrderBy(i => i.Number)
                    .Select(i => new InstallmentResponse(
                        i.Number,
                        i.Amount,
                        i.DueDate,
                        i.Status,
                        i.Status == InstallmentStatus.Pending && i.DueDate < today,
                        i.SettledOn,
                        i.SettledAmount,
                        i.EntryId))
                    .ToList(),
                // Só os metadados do anexo: o conteúdo fica no banco.
                Attachment = context.ContractAttachments
                    .Where(a => a.ContractId == c.Id)
                    .Select(a => new AttachmentResponse(a.FileName, a.SizeInBytes, a.UploadedAt))
                    .FirstOrDefault(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null) return null;

        var contract = row.Contract;

        return new ContractResponse(
            contract.Id,
            contract.EventId,
            contract.Direction,
            contract.Counterparty,
            contract.Description,
            contract.TotalAmount,
            contract.PaymentMethod,
            contract.SignedOn,
            row.Settled,
            row.Open,
            row.Overdue,
            row.Installments.All(i => i.Status != InstallmentStatus.Pending),
            row.Attachment,
            row.Installments);
    }

    public async Task<CashFlowReportResponse> GetCashFlowAsync(
        Guid ownerId,
        DateOnly today,
        int monthsAhead,
        CancellationToken cancellationToken = default)
    {
        // Realizado: o mesmo saldo que a tela de eventos mostra.
        var realized = await context.Entries
            .AsNoTracking()
            .Where(e => context.Events.Any(ev => ev.Id == e.EventId && ev.OwnerId == ownerId && !ev.IsDeleted))
            .SumAsync(e => (decimal?)(e.Type == EntryType.Income ? e.Amount : -e.Amount), cancellationToken) ?? 0m;

        var open = await context.Installments
            .AsNoTracking()
            .Where(i => i.Status == InstallmentStatus.Pending)
            .Where(i => context.Contracts.Any(c => c.Id == i.ContractId && c.OwnerId == ownerId && !c.IsDeleted))
            .Select(i => new OpenInstallment(
                i.ContractId,
                context.Contracts.Where(c => c.Id == i.ContractId).Select(c => c.EventId).First(),
                context.Contracts
                    .Where(c => c.Id == i.ContractId)
                    .Select(c => context.Events.Where(e => e.Id == c.EventId).Select(e => e.Name).First())
                    .First(),
                context.Contracts.Where(c => c.Id == i.ContractId).Select(c => c.Counterparty).First(),
                context.Contracts.Where(c => c.Id == i.ContractId).Select(c => c.Direction).First(),
                i.Number,
                i.Amount,
                i.DueDate))
            .ToListAsync(cancellationToken);

        // A partir daqui é agrupamento sobre um conjunto já reduzido: só as
        // parcelas em aberto do usuário.
        var overdue = open.Where(i => i.DueDate < today).ToList();
        var scheduled = open.Where(i => i.DueDate >= today).ToList();

        var currentMonth = Forecast(scheduled, today.Year, today.Month);

        var upcoming = Enumerable.Range(1, monthsAhead)
            .Select(offset => new DateOnly(today.Year, today.Month, 1).AddMonths(offset))
            .Select(month => Forecast(scheduled, month.Year, month.Month))
            .ToList();

        var totalReceivable = open.Where(i => i.Direction == ContractDirection.Receivable).Sum(i => i.Amount);
        var totalPayable = open.Where(i => i.Direction == ContractDirection.Payable).Sum(i => i.Amount);

        return new CashFlowReportResponse(
            today,
            realized,
            new OverdueSummaryResponse(
                overdue.Where(i => i.Direction == ContractDirection.Receivable).Sum(i => i.Amount),
                overdue.Where(i => i.Direction == ContractDirection.Payable).Sum(i => i.Amount),
                overdue.Count),
            currentMonth,
            upcoming,
            totalReceivable,
            totalPayable,
            realized + totalReceivable - totalPayable,
            [.. overdue.OrderBy(i => i.DueDate).Select(i => i.ToResponse(true))],
            [.. scheduled.OrderBy(i => i.DueDate).Take(MaxUpcomingListed).Select(i => i.ToResponse(false))]);
    }

    private static MonthlyForecastResponse Forecast(IEnumerable<OpenInstallment> installments, int year, int month)
    {
        var ofMonth = installments.Where(i => i.DueDate.Year == year && i.DueDate.Month == month).ToList();

        var receivable = ofMonth.Where(i => i.Direction == ContractDirection.Receivable).Sum(i => i.Amount);
        var payable = ofMonth.Where(i => i.Direction == ContractDirection.Payable).Sum(i => i.Amount);

        return new MonthlyForecastResponse(year, month, receivable, payable, receivable - payable, ofMonth.Count);
    }

    /// <summary>Parcela em aberto com o contexto do contrato, achatada para agrupar.</summary>
    private sealed record OpenInstallment(
        Guid ContractId,
        Guid EventId,
        string EventName,
        string Counterparty,
        ContractDirection Direction,
        int Number,
        decimal Amount,
        DateOnly DueDate)
    {
        public ScheduledInstallmentResponse ToResponse(bool isOverdue) => new(
            ContractId, EventId, EventName, Counterparty, Direction, Number, Amount, DueDate, isOverdue);
    }
}
