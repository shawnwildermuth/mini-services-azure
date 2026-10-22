#:package Microsoft.Data.SqlClient@6.1.1
#:package System.Security.Cryptography.Pkcs@10.0.13

// Gives managed identities access to the Parts Store database (run once after migrations; safe to re-run).
//
//   dotnet run infra/scripts/grant-sql-access.cs -- \
//       --server  sql-partsstore-prod-abc123.database.windows.net \
//       --database partsstore \
//       --app app-partsstore-prod-api-abc123=<identity client id> \
//       --app func-partsstore-prod-abc123=<identity client id>
//
// Azure SQL here is Entra-only, so there are no SQL passwords: each app signs in as its managed identity and needs a
// database user. The user is created WITH SID taken from the identity's client (application) id, which avoids the
// directory lookup that `CREATE USER ... FROM EXTERNAL PROVIDER` needs extra Graph permissions for.
// The caller must be the server's Entra administrator; authentication uses DefaultAzureCredential (az login, CI login, ...).
//
// Options: --dry-run prints the SQL and connects to nothing.  --connection-string overrides the generated connection.

using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

string? server = null, database = null, connectionString = null;
var apps = new List<(string Name, Guid ClientId)>();
var dryRun = false;

try
{
  for (var i = 0; i < args.Length; i++)
  {
    switch (args[i])
    {
      case "--server": server = args[++i]; break;
      case "--database": database = args[++i]; break;
      case "--connection-string": connectionString = args[++i]; break;
      case "--dry-run": dryRun = true; break;
      case "--app":
        var parts = args[++i].Split('=', 2);
        if (parts.Length != 2) throw new ArgumentException("--app must look like <name>=<client id>");
        apps.Add((parts[0], Guid.Parse(parts[1])));
        break;
      default: throw new ArgumentException($"Unknown argument {args[i]}");
    }
  }

  if (apps.Count == 0) throw new ArgumentException("Pass at least one --app <name>=<client id>");

  // Names go into dynamic SQL (QUOTENAME-d as well); refuse anything that is not a plausible app name.
  foreach (var (name, _) in apps)
    if (!Regex.IsMatch(name, "^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$")) throw new ArgumentException($"'{name}' is not a valid app name");

  if (!dryRun && connectionString is null && (server is null || database is null))
    throw new ArgumentException("Pass --server and --database (or --connection-string)");
}
catch (Exception ex) when (ex is ArgumentException or FormatException or IndexOutOfRangeException)
{
  Console.Error.WriteLine($"error: {ex.Message}");
  return 2;
}

const string GrantSql = """
    DECLARE @sid VARBINARY(16) = CAST(CAST(@clientId AS UNIQUEIDENTIFIER) AS VARBINARY(16));
    DECLARE @sql NVARCHAR(MAX);

    IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @name)
    BEGIN
      SET @sql = N'CREATE USER ' + QUOTENAME(@name) + N' WITH SID = ' + CONVERT(NVARCHAR(34), @sid, 1) + N', TYPE = E;';
      EXEC (@sql);
      PRINT 'created user ' + @name;
    END
    ELSE PRINT 'user ' + @name + ' already exists';

    SET @sql = N'ALTER ROLE db_datareader ADD MEMBER ' + QUOTENAME(@name) + N'; ALTER ROLE db_datawriter ADD MEMBER ' + QUOTENAME(@name) + N';';
    EXEC (@sql);
    """;

if (dryRun)
{
  foreach (var (name, clientId) in apps)
  {
    Console.WriteLine($"-- {name} (client id {clientId})");
    Console.WriteLine(GrantSql.Replace("@name", $"N'{name}'").Replace("@clientId", $"'{clientId}'"));
    Console.WriteLine();
  }
  return 0;
}

connectionString ??= new SqlConnectionStringBuilder
{
  DataSource = $"tcp:{server},1433",
  InitialCatalog = database,
  Authentication = SqlAuthenticationMethod.ActiveDirectoryDefault,
  Encrypt = SqlConnectionEncryptOption.Mandatory,
  ConnectTimeout = 60,
}.ConnectionString;

await using var connection = new SqlConnection(connectionString);
connection.InfoMessage += (_, e) => Console.WriteLine($"  {e.Message}");
await connection.OpenAsync();

foreach (var (name, clientId) in apps)
{
  Console.WriteLine($"granting {name}");
  await using var command = new SqlCommand(GrantSql, connection);
  command.Parameters.AddWithValue("@name", name);
  command.Parameters.AddWithValue("@clientId", clientId.ToString());
  await command.ExecuteNonQueryAsync();
}

Console.WriteLine("done");
return 0;
