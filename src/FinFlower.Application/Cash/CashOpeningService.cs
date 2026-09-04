using FinFlower.Application.Abstractions;
using FinFlower.Application.Cash.Dtos;
using FinFlower.Application.Common;
using FinFlower.Domain.Entities;

namespace FinFlower.Application.Cash;

public interface ICashOpeningService
{
    Task<Result<CashOpeningResponse?>> GetAsync(CancellationToken ct = default);

    Task<Result<CashOpeningResponse>> SaveAsync(SaveCashOpeningRequest request, CancellationToken ct = default);

    Task<Result> ClearAsync(CancellationToken ct = default);
}

/// <summary>
/// O saldo que o dono declara ter no dia em que começa a usar o sistema.
///
/// É um valor por dono, e sobrescrever é a operação normal: quem errou o número
/// do extrato corrige, não cria um segundo. Não há histórico de correções de
/// propósito — o saldo inicial não é um lançamento, é uma âncora, e uma âncora
/// com várias versões deixa de ancorar.
/// </summary>
public sealed class CashOpeningService(
    ICashOpeningRepository openings,
    IEntryQueries entries,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork) : ICashOpeningService
{
    private static readonly Error NoSession =
        Error.Unauthorized("auth.required", "Sessão inválida. Faça login novamente.");

    public async Task<Result<CashOpeningResponse?>> GetAsync(CancellationToken ct = default)
    {
        if (currentUser.UserId is not { } ownerId)
            return Result.Failure<CashOpeningResponse?>(NoSession);

        var opening = await openings.GetAsync(ownerId, ct);

        return Result.Success(opening is null ? null : await ReadAsync(opening, ownerId, ct));
    }

    public async Task<Result<CashOpeningResponse>> SaveAsync(
        SaveCashOpeningRequest request,
        CancellationToken ct = default)
    {
        if (currentUser.UserId is not { } ownerId)
            return Result.Failure<CashOpeningResponse>(NoSession);

        var opening = await openings.GetAsync(ownerId, ct);

        if (opening is null)
        {
            opening = new CashOpening(ownerId, request.Amount, request.OccurredOn, request.Notes);
            openings.Add(opening);
        }
        else
        {
            opening.Change(request.Amount, request.OccurredOn, request.Notes);
        }

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(await ReadAsync(opening, ownerId, ct));
    }

    public async Task<Result> ClearAsync(CancellationToken ct = default)
    {
        if (currentUser.UserId is not { } ownerId)
            return Result.Failure(NoSession);

        var opening = await openings.GetAsync(ownerId, ct);
        if (opening is null) return Result.Success();

        // Remover devolve o caixa ao comportamento antigo — saldo é a soma do que
        // foi digitado —, e os lançamentos que estavam fora da conta voltam a ela.
        openings.Remove(opening);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }

    private async Task<CashOpeningResponse> ReadAsync(
        CashOpening opening,
        Guid ownerId,
        CancellationToken ct) =>
        new(opening.Amount,
            opening.OccurredOn,
            opening.Notes,
            await entries.CountBeforeAsync(ownerId, opening.OccurredOn, ct));
}
