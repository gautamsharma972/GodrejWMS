using FluentValidation;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.Inward.Dtos;
using MediatR;

namespace GodrejWMS.Application.Features.Inward.Commands;

/// <summary>Bulk inward upload: parses an "inward" sheet-shaped Excel file, then delegates to <see cref="SubmitInwardCommand"/>.</summary>
public sealed record ImportInwardFromExcelCommand(Stream FileStream) : IRequest<InwardResultDto>;

public sealed class ImportInwardFromExcelValidator : AbstractValidator<ImportInwardFromExcelCommand>
{
    public ImportInwardFromExcelValidator()
    {
        RuleFor(x => x.FileStream).NotNull();
    }
}

public sealed class ImportInwardFromExcelHandler(IExcelService excel, IMediator mediator)
    : IRequestHandler<ImportInwardFromExcelCommand, InwardResultDto>
{
    public Task<InwardResultDto> Handle(ImportInwardFromExcelCommand request, CancellationToken cancellationToken)
    {
        var rows = excel.ReadInwardUpload(request.FileStream);

        var lines = rows
            .Select(r => new SubmitInwardLine(r.MaterialCode, r.QuantityBoxes, r.MfgMonthText))
            .ToList();

        return mediator.Send(new SubmitInwardCommand(lines), cancellationToken);
    }
}
