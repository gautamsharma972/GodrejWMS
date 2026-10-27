using FluentValidation;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Pullout.Dtos;
using MediatR;

namespace GodrejWMS.Application.Features.Pullout.Commands;

/// <summary>Bulk pullout upload: parses a "Format inventory pullout-upload" shaped Excel file, then delegates to <see cref="SubmitPulloutCommand"/>.</summary>
public sealed record ImportPulloutFromExcelCommand(Stream FileStream, int WarehouseId) : IRequest<PulloutResultDto>;

public sealed class ImportPulloutFromExcelValidator : AbstractValidator<ImportPulloutFromExcelCommand>
{
    public ImportPulloutFromExcelValidator()
    {
        RuleFor(x => x.FileStream).NotNull();
    }
}

public sealed class ImportPulloutFromExcelHandler(IExcelService excel, IMediator mediator)
    : IRequestHandler<ImportPulloutFromExcelCommand, PulloutResultDto>
{
    public Task<PulloutResultDto> Handle(ImportPulloutFromExcelCommand request, CancellationToken cancellationToken)
    {
        var (rows, headerFields) = excel.ReadPulloutUpload(request.FileStream);

        var lines = rows
            .Select(r => new SubmitPulloutLine(r.MaterialCode, r.QuantityBoxes, r.PlantName, r.Priority, r.MaterialDescription))
            .ToList();

        return mediator.Send(new SubmitPulloutCommand(lines, HeaderFields: headerFields, WarehouseId: request.WarehouseId), cancellationToken);
    }
}
