using FluentValidation;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Pullout.Dtos;
using MediatR;

namespace GodrejWMS.Application.Features.Pullout.Commands;

/// <summary>Same intake as <see cref="ImportPulloutFromExcelCommand"/>, but for a Loading Sheet
/// copy-pasted straight out of Excel (tab-separated cells, as the clipboard carries them) instead
/// of uploaded as a file - for the real-time case of several pullouts a day where re-saving and
/// re-uploading a growing workbook each time is slower than just pasting the latest block.</summary>
public sealed record ImportPulloutFromPasteCommand(string PastedText, int WarehouseId) : IRequest<PulloutResultDto>;

public sealed class ImportPulloutFromPasteValidator : AbstractValidator<ImportPulloutFromPasteCommand>
{
    public ImportPulloutFromPasteValidator()
    {
        RuleFor(x => x.PastedText).NotEmpty().WithMessage("Paste the Loading Sheet (header and rows) before importing.");
    }
}

public sealed class ImportPulloutFromPasteHandler(IExcelService excel, IMediator mediator, IApplicationDbContext db)
    : IRequestHandler<ImportPulloutFromPasteCommand, PulloutResultDto>
{
    public async Task<PulloutResultDto> Handle(ImportPulloutFromPasteCommand request, CancellationToken cancellationToken)
    {
        var (rows, headerFields, indentText) = excel.ReadPulloutPaste(request.PastedText);

        if (rows.Count == 0)
        {
            throw new InvalidOperationException(
                "No data rows were recognized in the pasted text. Make sure the \"Code\" column header and the rows below it were included in the copy.");
        }

        var clientIndentNumber = await PulloutIndentDuplicateGuard.CheckAsync(db, indentText, cancellationToken);

        var lines = rows
            .Select(r => new SubmitPulloutLine(r.MaterialCode, r.QuantityBoxes, r.PlantName, r.Priority, r.MaterialDescription))
            .ToList();

        return await mediator.Send(
            new SubmitPulloutCommand(lines, HeaderFields: headerFields, WarehouseId: request.WarehouseId, ClientIndentNumber: clientIndentNumber),
            cancellationToken);
    }
}
