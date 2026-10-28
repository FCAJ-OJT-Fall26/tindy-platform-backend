using Npgsql;
using Testcontainers.PostgreSql;

namespace Tindy.Tests.Integration;

// Docker by default. An explicitly configured, loopback-only test cluster can be
// used on machines without Docker. Always create our own randomly named database.
internal sealed class TestPostgres : IAsyncDisposable
{
    private readonly PostgreSqlContainer? _container;
    private readonly string? _adminConnection;
    private readonly string _database = "tindy_test_" + Guid.NewGuid().ToString("N");
    private string? _connection;
    private bool _created;

    public TestPostgres()
    {
        _adminConnection = Environment.GetEnvironmentVariable("TINDY_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(_adminConnection))
        {
            _adminConnection = null;
            _container = new PostgreSqlBuilder().WithImage("postgres:16-alpine")
                .WithDatabase(_database).WithUsername("postgres").WithPassword("postgres_test_only").Build();
        }
        else
        {
            var options = new NpgsqlConnectionStringBuilder(_adminConnection);
            if (options.Host is not ("127.0.0.1" or "localhost") || options.Username != "tindy_test_admin" || options.Database != "postgres")
                throw new InvalidOperationException("TINDY_TEST_POSTGRES must point to a loopback test cluster using tindy_test_admin and database postgres.");
            _connection = new NpgsqlConnectionStringBuilder(_adminConnection) { Database = _database }.ConnectionString;
        }
    }

    public string GetConnectionString() => _container?.GetConnectionString() ?? _connection!;

    public async Task StartAsync()
    {
        if (_container is not null) { await _container.StartAsync(); return; }
        await using var admin = new NpgsqlConnection(_adminConnection);
        await admin.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE {_database}", admin);
        await command.ExecuteNonQueryAsync();
        _created = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null) { await _container.DisposeAsync(); return; }
        if (!_created) return;
        await using var pooled = new NpgsqlConnection(_connection);
        NpgsqlConnection.ClearPool(pooled);
        await using var admin = new NpgsqlConnection(_adminConnection);
        await admin.OpenAsync();
        // The identifier is generated here, never taken from a developer configuration.
        await using var command = new NpgsqlCommand($"DROP DATABASE {_database} WITH (FORCE)", admin);
        await command.ExecuteNonQueryAsync();
    }
}
