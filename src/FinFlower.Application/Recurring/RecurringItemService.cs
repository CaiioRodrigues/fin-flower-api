using FinFlower.Application.Abstractions;
using FinFlower.Application.Cash;
using FinFlower.Application.Common;
using FinFlower.Application.Recurring.Dtos;
using FinFlower.Domain.Entities;
using FinFlower.Domain.Enums;
using FinFlower.Domain.ValueObjects;

namespace FinFlower.Application.Recurring;

public interface IRecurringItemService
{
    Task<Result<RecurringMonthResponse>> ListAsync(RecurringFilter filter, string? competence, CancellationToken ct = default);
    Task<Result<RecurringItemResponse>> CreateAsync(CreateRecurringItemRequest request, CancellationToken ct = default);
    Task<Result<RecurringItemResponse>> UpdateAsync(Guid itemId, UpdateRecurringItemRequest request, CancellationToken ct = default);
    Task<Result<RecurringItemResponse>> SetActiveAsync(Guid itemId, bool active, CancellationToken ct = default);
    Task<Result> DeleteAsync(Guid itemId, CancellationToken ct = default);
    Task<Result<GenerateMonthResponse>> GenerateMonthAsync(string? competence, IReadOnlyList<Guid>? itemIds, CancellationToken ct = default);
}

/// <summary>
/// Gastos fixos e pró-labore. São a mesma mecânica — um valor que se repete todo
/// mês — e por isso um motor só; o que muda é a <see cref="RecurringKind"/>, que
/// separa as telas e permite responder "quanto do mês é retirada de sócio".
///
/// Gerar a competência é idempotente: a chave única (item, mês) no banco garante
/// que rodar duas vezes o mesmo mês não duplique nada, e a consulta prévia faz
/// isso sem depender de exceção.
/// </summary>
public sealed class RecurringItemService(
    IRecurringItemRepository items,
    IEntryRepository entries,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IUnitOfWork unitOfWork) : IRecurringItemService
{
    private static readonly Error NoSession =
        Error.Unauthorized("auth.required", "Sessão inválida. Faça login novamente.");

    private static Error ItemNotFound() =>
        Error.NotFound("recurring_item.not_found", "Item fixo não encontrado.");

    public async Task<Result<RecurringMonthResponse>> ListAsync(
        RecurringFilter filter,
        string? competence,
        CancellationToken ct = default)
    {
        if (currentUser.UserId is not { } ownerId)
            return Result.Failure<RecurringMonthResponse>(NoSession);

        var month = ReadCompetence(competence);
        if (month.IsFailure) return Result.Failure<RecurringMonthResponse>(month.Error!);

        var target = month.Value;
        var found = await items.ListAsync(ownerId, filter, ct);
        var generated = await entries.GetGeneratedRecurringMonthsAsync(ownerId, target, ct);

        var responses = found
            .Select(item => ToResponse(item, target, generated.Contains((item.Id, target.FirstDay))))
            .ToList();

        var pending = responses.Where(r => r is { DueInMonth: true, GeneratedForMonth: false }).ToList();

        return Result.Success(new RecurringMonthResponse(
            target.ToString(),
            MonthLabel.For(target),
            TotalFixedExpense: Total(responses, RecurringKind.FixedExpense),
            TotalProLabore: Total(responses, RecurringKind.ProLabore),
            TotalFixedIncome: Total(responses, RecurringKind.FixedIncome),
            PendingAmount: pending.Sum(r => r.Amount),
            PendingCount: pending.Count,
            Items: responses));

        // Só o que vale para a competência entra no total do mês: um item que
        // começa em março não pode inflar o previsto de janeiro.
        static decimal Total(List<RecurringItemResponse> all, RecurringKind kind) =>
            all.Where(r => r.Kind == kind && r.DueInMonth).Sum(r => r.Amount);
    }

    public async Task<Result<RecurringItemResponse>> CreateAsync(
        CreateRecurringItemRequest request,
        CancellationToken ct = default)
    {
        if (currentUser.UserId is not { } ownerId)
            return Result.Failure<RecurringItemResponse>(NoSession);

        var item = new RecurringItem(
            ownerId,
            request.Kind,
            request.Description,
            request.Amount,
            request.Category,
            request.DayOfMonth,
            YearMonth.Parse(request.StartMonth),
            ParseOptional(request.EndMonth),
            request.Notes);

        items.Add(item);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(ToResponse(item, item.Start, generated: false));
    }

    public Task<Result<RecurringItemResponse>> UpdateAsync(
        Guid itemId,
        UpdateRecurringItemRequest request,
        CancellationToken ct = default) =>
        MutateAsync(itemId, item => item.UpdateDetails(
            request.Description,
            request.Amount,
            request.Category,
            request.DayOfMonth,
            ParseOptional(request.EndMonth),
            request.Notes), ct);

    public Task<Result<RecurringItemResponse>> SetActiveAsync(
        Guid itemId,
        bool active,
        CancellationToken ct = default) =>
        MutateAsync(itemId, item =>
        {
            if (active) item.Activate();
            else item.Deactivate();
        }, ct);

    public async Task<Result> DeleteAsync(Guid itemId, CancellationToken ct = default)
    {
        var loaded = await LoadAsync(itemId, ct);
        if (loaded.IsFailure) return Result.Failure(loaded.Error!);

        // Exclusão lógica: os lançamentos já gerados continuam no caixa, e é o
        // que se espera — o aluguel de março foi pago mesmo que o contrato acabe.
        loaded.Value.MarkAsDeleted(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }

    public async Task<Result<GenerateMonthResponse>> GenerateMonthAsync(
        string? competence,
        IReadOnlyList<Guid>? itemIds,
        CancellationToken ct = default)
    {
        if (currentUser.UserId is not { } ownerId)
            return Result.Failure<GenerateMonthResponse>(NoSession);

        var month = ReadCompetence(competence);
        if (month.IsFailure) return Result.Failure<GenerateMonthResponse>(month.Error!);

        var target = month.Value;
        var all = await items.ListAsync(ownerId, new RecurringFilter(OnlyActive: true), ct);
        var generated = await entries.GetGeneratedRecurringMonthsAsync(ownerId, target, ct);

        // Lista vazia significa "o mês inteiro"; lista com ids, só os escolhidos.
        var selected = itemIds is { Count: > 0 }
            ? all.Where(item => itemIds.Contains(item.Id)).ToList()
            : all.ToList();

        var due = selected.Where(item => item.IsDueIn(target)).ToList();
        var pending = due.Where(item => !generated.Contains((item.Id, target.FirstDay))).ToList();

        var created = pending.Select(item => item.GenerateEntry(target)).ToList();

        if (created.Count > 0)
        {
            entries.AddRange(created);
            await unitOfWork.SaveChangesAsync(ct);
        }

        return Result.Success(new GenerateMonthResponse(
            target.ToString(),
            created.Count,
            due.Count - created.Count,
            created.Sum(e => e.Amount),
            [.. created.Select(e => e.Description)]));
    }

    /// <summary>Em branco, a competência é o mês corrente — o caso comum da tela.</summary>
    private Result<YearMonth> ReadCompetence(string? competence)
    {
        if (string.IsNullOrWhiteSpace(competence))
            return Result.Success(YearMonth.From(DateOnly.FromDateTime(clock.UtcNow.UtcDateTime)));

        return YearMonth.TryParse(competence, out var parsed)
            ? Result.Success(parsed)
            : Result.Failure<YearMonth>(Error.Validation(
                "recurring.invalid_competence",
                "A competência deve estar no formato aaaa-mm, como 2026-09."));
    }

    private static YearMonth? ParseOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : YearMonth.Parse(value);

    private async Task<Result<RecurringItem>> LoadAsync(Guid itemId, CancellationToken ct)
    {
        if (currentUser.UserId is not { } ownerId)
            return Result.Failure<RecurringItem>(NoSession);

        var item = await items.GetByIdAsync(itemId, ownerId, ct);

        return item is null
            ? Result.Failure<RecurringItem>(ItemNotFound())
            : Result.Success(item);
    }

    private async Task<Result<RecurringItemResponse>> MutateAsync(
        Guid itemId,
        Action<RecurringItem> mutate,
        CancellationToken ct)
    {
        var loaded = await LoadAsync(itemId, ct);
        if (loaded.IsFailure) return Result.Failure<RecurringItemResponse>(loaded.Error!);

        mutate(loaded.Value);
        await unitOfWork.SaveChangesAsync(ct);

        var current = YearMonth.From(DateOnly.FromDateTime(clock.UtcNow.UtcDateTime));
        return Result.Success(ToResponse(loaded.Value, current, generated: false));
    }

    private static RecurringItemResponse ToResponse(RecurringItem item, YearMonth competence, bool generated)
    {
        var due = item.IsDueIn(competence);

        return new RecurringItemResponse(
            item.Id,
            item.Kind,
            item.EntryType,
            item.Description,
            item.Amount,
            item.Category,
            item.DayOfMonth,
            item.Start.ToString(),
            item.End?.ToString(),
            item.IsActive,
            item.Notes,
            generated,
            due,
            due ? item.DueDateIn(competence) : null);
    }
}
