using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GodrejWMS.Infrastructure.Persistence;

/// <summary>
/// Some MySQL servers (this project has been hit by a WAMP install) are configured with
/// default_storage_engine=MyISAM instead of InnoDB. Pomelo has no per-table ENGINE override,
/// so we force InnoDB for the session the moment our app opens a connection — independent of
/// whatever the shared server's global default is.
/// </summary>
public sealed class ForceInnoDbInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        SetDefaultEngine(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await SetDefaultEngineAsync(connection, cancellationToken);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    private static void SetDefaultEngine(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SET SESSION default_storage_engine = 'InnoDB';";
        command.ExecuteNonQuery();
    }

    private static async Task SetDefaultEngineAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        await using (command.ConfigureAwait(false))
        {
            command.CommandText = "SET SESSION default_storage_engine = 'InnoDB';";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
