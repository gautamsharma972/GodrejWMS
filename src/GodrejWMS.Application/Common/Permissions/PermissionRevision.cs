using System.Threading;

namespace GodrejWMS.Application.Common.Permissions;

/// <summary>
/// Process-wide revision used to invalidate permission caches in existing Blazor circuits after
/// the role-permission matrix changes. No permission data is stored here.
/// </summary>
public sealed class PermissionRevision
{
    private long _value;

    public long Value => Interlocked.Read(ref _value);

    public void Increment() => Interlocked.Increment(ref _value);
}
