using FluentValidation;
using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Application.Features.AopDescriptionLocationPreferences.Commands;

/// <summary>Replicates every active location-range mapping of one AOP Description onto another, so a
/// similar product family doesn't have to be mapped range by range. Each copied range goes through the
/// same validation as a hand-entered one; a range the target can't take (it already exists, or overlaps
/// one of the target's own active ranges) is skipped and reported, never silently dropped.</summary>
public sealed record CopyAopDescriptionLocationPreferencesCommand(int SourceAopDescriptionId, int TargetAopDescriptionId)
    : IRequest<CopyAopDescriptionLocationPreferencesResult>;

public sealed record CopiedPreferenceRange(string RackCode, string FromLocationCode, string ToLocationCode);

public sealed record SkippedPreferenceRange(string RackCode, string FromLocationCode, string ToLocationCode, string Reason);

public sealed record CopyAopDescriptionLocationPreferencesResult(
    IReadOnlyList<CopiedPreferenceRange> Copied,
    IReadOnlyList<SkippedPreferenceRange> Skipped);

public sealed class CopyAopDescriptionLocationPreferencesValidator : AbstractValidator<CopyAopDescriptionLocationPreferencesCommand>
{
    public CopyAopDescriptionLocationPreferencesValidator()
    {
        RuleFor(x => x.SourceAopDescriptionId).GreaterThan(0).WithMessage("Select the AOP Description to copy from.");
        RuleFor(x => x.TargetAopDescriptionId).GreaterThan(0).WithMessage("Select the AOP Description to copy to.");
        RuleFor(x => x).Must(x => x.SourceAopDescriptionId != x.TargetAopDescriptionId)
            .WithMessage("Source and target AOP Description must be different.");
    }
}

public sealed class CopyAopDescriptionLocationPreferencesHandler(
    IApplicationDbContext db,
    IDateTimeProvider clock,
    ICurrentUserService currentUser)
    : IRequestHandler<CopyAopDescriptionLocationPreferencesCommand, CopyAopDescriptionLocationPreferencesResult>
{
    public async Task<CopyAopDescriptionLocationPreferencesResult> Handle(
        CopyAopDescriptionLocationPreferencesCommand request, CancellationToken cancellationToken)
    {
        var sourceRanges = await db.AopDescriptionLocationPreferences.AsNoTracking()
            .Where(x => x.AopDescriptionId == request.SourceAopDescriptionId && x.IsActive)
            .OrderBy(x => x.FromPalletPosition.Rack.Code).ThenBy(x => x.FromPalletPosition.Column).ThenBy(x => x.FromPalletPosition.Level)
            .Select(x => new
            {
                x.FromPalletPositionId,
                x.ToPalletPositionId,
                RackCode = x.FromPalletPosition.Rack.Code,
                From = x.FromPalletPosition.LocationCode,
                To = x.ToPalletPosition.LocationCode
            })
            .ToListAsync(cancellationToken);

        if (sourceRanges.Count == 0)
        {
            throw new InvalidOperationException("The source AOP Description has no active location mappings to copy.");
        }

        var copied = new List<CopiedPreferenceRange>();
        var skipped = new List<SkippedPreferenceRange>();
        var now = clock.UtcNow;

        foreach (var range in sourceRanges)
        {
            try
            {
                await AopDescriptionLocationPreferenceRules.ValidateAsync(
                    db, request.TargetAopDescriptionId, range.FromPalletPositionId, range.ToPalletPositionId,
                    isActive: true, excludingId: null, cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                skipped.Add(new SkippedPreferenceRange(range.RackCode, range.From, range.To, ex.Message));
                continue;
            }

            db.AopDescriptionLocationPreferences.Add(new AopDescriptionLocationPreference
            {
                AopDescriptionId = request.TargetAopDescriptionId,
                FromPalletPositionId = range.FromPalletPositionId,
                ToPalletPositionId = range.ToPalletPositionId,
                IsActive = true,
                CreatedAt = now,
                CreatedByUserId = currentUser.UserId
            });
            // Saved per range so the next range's duplicate/overlap check sees it.
            await db.SaveChangesAsync(cancellationToken);
            copied.Add(new CopiedPreferenceRange(range.RackCode, range.From, range.To));
        }

        return new CopyAopDescriptionLocationPreferencesResult(copied, skipped);
    }
}
