using FinFlower.Application.Abstractions;
using FinFlower.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinFlower.Infrastructure.Persistence.Repositories;

public sealed class EventRepository(AppDbContext context) : IEventRepository
{
    public Task<Event?> GetByIdAsync(Guid id, Guid ownerId, CancellationToken cancellationToken = default) =>
        context.Events
            .Include(e => e.Entries)
            // O filtro por dono faz parte da consulta, não de uma checagem posterior:
            // não existe caminho que carregue o evento de outra pessoa.
            .FirstOrDefaultAsync(e => e.Id == id && e.OwnerId == ownerId, cancellationToken);

    public void Add(Event @event) => context.Events.Add(@event);
}
