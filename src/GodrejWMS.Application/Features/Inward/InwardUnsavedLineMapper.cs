using GodrejWMS.Application.Features.Inward.Dtos;
using GodrejWMS.Domain.Entities;

namespace GodrejWMS.Application.Features.Inward;

public static class InwardUnsavedLineMapper
{
    public static IReadOnlyList<InwardUnsavedLineDto> From(InwardTransaction transaction) =>
        transaction.UnsavedLines
            .OrderBy(u => u.SourceRow ?? int.MaxValue).ThenBy(u => u.Id)
            .Select(u => new InwardUnsavedLineDto(u.CodeText, u.QuantityBoxes, u.Kind, u.Reason, u.SourceRow, u.Description))
            .ToList();
}
