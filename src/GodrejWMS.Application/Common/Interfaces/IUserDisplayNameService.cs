namespace GodrejWMS.Application.Common.Interfaces;

public interface IUserDisplayNameService
{
    Task<IReadOnlyDictionary<string, string>> GetDisplayNamesAsync(
        IEnumerable<string> userIds,
        CancellationToken cancellationToken = default);
}
