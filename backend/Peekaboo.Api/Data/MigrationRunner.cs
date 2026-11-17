using System.Globalization;
using System.Reflection;
using Microsoft.Data.Sqlite;

namespace Peekaboo.Api.Data;

public static class MigrationRunner
{
    public static int Apply(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var migrations = LoadMigrations();
        var currentVersion = GetUserVersion(connection);

        var newestKnownVersion = migrations.Count > 0 ? migrations[^1].Version : 0;
        if (currentVersion > newestKnownVersion)
        {
            throw new InvalidOperationException(
                $"Database schema version {currentVersion} is newer than the newest migration known " +
                $"to this build ({newestKnownVersion}). Run a newer Peekaboo version.");
        }

        var applied = 0;

        foreach (var migration in migrations)
        {
            if (migration.Version <= currentVersion)
            {
                continue;
            }

            using var transaction = connection.BeginTransaction();

            try
            {
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = migration.Sql;
                    command.ExecuteNonQuery();
                }

                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = string.Create(
                        CultureInfo.InvariantCulture,
                        $"PRAGMA user_version = {migration.Version}");
                    command.ExecuteNonQuery();
                }

                transaction.Commit();
            }
            catch (SqliteException exception)
            {
                throw new InvalidOperationException(
                    $"Migration '{migration.Name}' (version {migration.Version}) failed.",
                    exception);
            }

            applied++;
        }

        return applied;
    }

    /// <summary>
    /// Loads embedded migrations. Migrations are append-only: new scripts must use a
    /// strictly increasing version prefix, because only the latest applied version is tracked.
    /// </summary>
    public static IReadOnlyList<Migration> LoadMigrations()
    {
        var assembly = typeof(MigrationRunner).Assembly;
        var migrations = new List<Migration>();

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var separator = resourceName.IndexOf('_', StringComparison.Ordinal);
            if (separator <= 0 ||
                !long.TryParse(
                    resourceName.AsSpan(0, separator),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var version))
            {
                throw new InvalidOperationException(
                    $"Migration resource '{resourceName}' must be named '<version>_<name>.sql'.");
            }

            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Cannot read migration resource '{resourceName}'.");
            using var reader = new StreamReader(stream);

            migrations.Add(new Migration(version, resourceName, reader.ReadToEnd()));
        }

        migrations.Sort(static (left, right) => left.Version.CompareTo(right.Version));

        for (var i = 1; i < migrations.Count; i++)
        {
            if (migrations[i - 1].Version == migrations[i].Version)
            {
                throw new InvalidOperationException(
                    $"Duplicate migration version {migrations[i].Version}: " +
                    $"'{migrations[i - 1].Name}' and '{migrations[i].Name}'.");
            }
        }

        return migrations;
    }

    private static long GetUserVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version";
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public sealed record Migration(long Version, string Name, string Sql);
}
