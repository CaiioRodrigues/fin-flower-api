using FinFlower.Domain.Enums;

namespace FinFlower.Application.Events.Dtos;

public sealed record CreateEventRequest(string Name, string? Description, DateOnly EventDate);

public sealed record UpdateEventRequest(string Name, string? Description, DateOnly EventDate);

public sealed record CreateEntryRequest(
    EntryType Type,
    string Description,
    decimal Amount,
    string Category,
    DateOnly OccurredOn) : Validators.IEntryFields;

public sealed record UpdateEntryRequest(
    EntryType Type,
    string Description,
    decimal Amount,
    string Category,
    DateOnly OccurredOn) : Validators.IEntryFields;

public sealed record EntryResponse(
    Guid Id,
    EntryType Type,
    string Description,
    decimal Amount,
    string Category,
    DateOnly OccurredOn);

/// <summary>Evento na listagem: os totais já vêm calculados, sem os lançamentos.</summary>
public sealed record EventSummaryResponse(
    Guid Id,
    string Name,
    string? Description,
    DateOnly EventDate,
    EventStatus Status,
    decimal TotalIncome,
    decimal TotalExpense,
    decimal Result,
    bool IsProfitable,
    int EntryCount);

/// <summary>Evento aberto: os mesmos totais mais tudo que foi cadastrado nele.</summary>
public sealed record EventDetailsResponse(
    Guid Id,
    string Name,
    string? Description,
    DateOnly EventDate,
    EventStatus Status,
    decimal TotalIncome,
    decimal TotalExpense,
    decimal Result,
    bool IsProfitable,
    IReadOnlyList<EntryResponse> Entries);

/// <summary>Filtros da listagem. Todos opcionais.</summary>
public sealed record EventFilter(DateOnly? From = null, DateOnly? To = null, EventStatus? Status = null);
