using GodrejWMS.Application.Common.Interfaces;
using GodrejWMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GodrejWMS.Infrastructure.Services;

public sealed class UserDisplayNameService(AppDbContext db) : IUserDisplayNameService
{
    public async Task<IReadOnlyDictionary<string, string>> GetDisplayNamesAsync(
        IEnumerable<string> userIds,
        CancellationToken cancellationToken = default)
    {
        var ids = userIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<string, string>();

        return await db.Users.AsNoTracking()
            .Where(user => ids.Contains(user.Id))
            .ToDictionaryAsync(
                user => user.Id,
                user => !string.IsNullOrWhiteSpace(user.UserName)
                    ? user.UserName
                    : !string.IsNullOrWhiteSpace(user.FullName)
                        ? user.FullName
                        : user.Email ?? "Unknown user",
                cancellationToken);
    }
}
