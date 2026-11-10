using FluentValidation;
using GodrejWMS.Application.Common;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Application.Features.PlatformAdmin.Logs.Dtos;
using MediatR;

namespace GodrejWMS.Application.Features.PlatformAdmin.Logs.Queries;

/// <summary>Which days have a log file on disk right now - drives the date picker on the hidden
/// Platform Admin log viewer. Platform-Admin-only, same as every other query in this folder.</summary>
public sealed record GetLogFileDatesQuery : IRequest<IReadOnlyList<LogFileSummaryDto>>;

public sealed class GetLogFileDatesValidator : AbstractValidator<GetLogFileDatesQuery>
{
    public GetLogFileDatesValidator(ICurrentUserService currentUser)
    {
        RuleFor(x => x)
            .Must(_ => currentUser.IsInRole(KnownRoles.PlatformAdmin))
            .WithMessage("Only Platform Admin can view this.");
    }
}

public sealed class GetLogFileDatesHandler(ILogArchiveReader reader)
    : IRequestHandler<GetLogFileDatesQuery, IReadOnlyList<LogFileSummaryDto>>
{
    public Task<IReadOnlyList<LogFileSummaryDto>> Handle(GetLogFileDatesQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<LogFileSummaryDto> result = reader.ListLogFiles()
            .OrderByDescending(f => f.Date)
            .Select(f => new LogFileSummaryDto(f.Date, f.SizeBytes))
            .ToList();
        return Task.FromResult(result);
    }
}
