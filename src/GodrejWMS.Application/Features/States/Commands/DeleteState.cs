using GodrejWMS.Application.Common.Exceptions;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.States.Commands;

/// <summary>Deletes a state, unless it has dependent cities.</summary>
public sealed record DeleteStateCommand(int Id) : IRequest;

public sealed class DeleteStateHandler(IApplicationDbContext db)
    : IRequestHandler<DeleteStateCommand>
{
    public async Task Handle(DeleteStateCommand request, CancellationToken cancellationToken)
    {
        var state = await db.States.FindAsync([request.Id], cancellationToken)
            ?? throw new NotFoundException(nameof(State), request.Id);

        var cityCount = await db.Cities.CountAsync(c => c.StateId == request.Id, cancellationToken);
        if (cityCount > 0)
        {
            throw new InvalidOperationException(
                $"Cannot delete '{state.Name}' - it has {cityCount} cit(ies) under it. Deactivate it instead.");
        }

        db.States.Remove(state);
        await db.SaveChangesAsync(cancellationToken);
    }
}
