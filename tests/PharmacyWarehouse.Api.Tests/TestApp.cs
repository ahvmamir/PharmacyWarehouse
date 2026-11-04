using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Api.Operations;
using PharmacyWarehouse.Domain.Auth;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Api.Tests;

// One in-process Api with its own temporary SQLite file. All passwords here are throwaway test data.
public sealed class TestApp : IAsyncDisposable
{
    public const string AdminPassword = "Adm-test-pass-9X2k";
    public const string StaffPassword = "Tech-test-pass-4Qz";
    public const string NewPassword = "new-secret-pass-77";

    private const string ConnectionStringVariable = "ConnectionStrings__Default";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly string _dbPath;

    private TestApp(WebApplicationFactory<Program> factory, string dbPath)
    {
        _factory = factory;
        _dbPath = dbPath;
    }

    public static async Task<TestApp> CreateAsync()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"pharmacywarehouse-test-{Guid.NewGuid():N}.db");

        // Program.cs reads the connection string at startup, before the factory can inject settings.
        // Environment variables are read by WebApplication.CreateBuilder and override appsettings files.
        Environment.SetEnvironmentVariable(ConnectionStringVariable, $"Data Source={dbPath}");

        var app = new TestApp(new WebApplicationFactory<Program>(), dbPath);
        try
        {
            await app.InitializeAsync();
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }

        return app;
    }

    private async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();

        // Safety: never seed into the real development database.
        var actual = db.Database.GetDbConnection().DataSource;
        if (!string.Equals(Path.GetFullPath(actual), Path.GetFullPath(_dbPath), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The test host did not use the temporary database. It uses: {actual}");
        }

        await db.Database.MigrateAsync();
    }

    // Each client has its own cookie jar, so each client is one "device".
    public HttpClient NewClient(bool csrfHeader = true)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            // https, because the auth cookie is Secure and is only stored and sent over https.
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

        if (csrfHeader)
        {
            client.DefaultRequestHeaders.Add(ApiHeaders.RequestedWith, ApiHeaders.RequestedWithValue);
        }

        return client;
    }

    public Task<AppUser> AddAdminAsync(string username, bool mustChangePassword = false)
        => AddUserAsync(username, AdminPassword, Roles.Admin, mustChangePassword);

    public Task<AppUser> AddStaffAsync(string username, bool mustChangePassword = false)
        => AddUserAsync(username, StaffPassword, Roles.Staff, mustChangePassword);

    public Task<AppUser> AddKeeperAsync(string username, bool mustChangePassword = false)
        => AddUserAsync(username, StaffPassword, Roles.Keeper, mustChangePassword);

    public Task<AppUser> AddManagerAsync(string username, bool mustChangePassword = false)
        => AddUserAsync(username, StaffPassword, Roles.Manager, mustChangePassword);

    public Task<AppUser> AddUserInRoleAsync(string username, string role, bool mustChangePassword = false)
        => AddUserAsync(username, StaffPassword, role, mustChangePassword);

    // Test only: writes the last-backup marker next to the temporary database, `age` ago.
    public void WriteBackupMarker(TimeSpan age)
    {
        using var scope = _factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<DatabaseLocation>();
        var at = DateTime.UtcNow - age;
        File.WriteAllText(BackupMarker.PathFor(database.FilePath),
            at.ToString("O", System.Globalization.CultureInfo.InvariantCulture) + Environment.NewLine + "test");
    }

    // A user with no role at all, to prove the policies demand one.
    public Task<AppUser> AddUserWithoutRoleAsync(string username)
        => AddUserAsync(username, StaffPassword, role: null, mustChangePassword: false);

    private async Task<AppUser> AddUserAsync(string username, string password, string? role, bool mustChangePassword)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = username,
            DisplayName = username,
            MustChangePassword = mustChangePassword,
            IsActive = true
        };

        Ensure(await users.CreateAsync(user, password), "create user");
        if (role is not null)
        {
            Ensure(await users.AddToRoleAsync(user, role), $"assign role {role}");
        }

        return user;
    }

    // Test only: flips IsActive directly in the database WITHOUT touching the security stamp,
    // to prove the ActiveUser requirement works on its own (the stamp is the first line of defense).
    public async Task SetIsActiveRawAsync(Guid userId, bool isActive)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();

        var user = await db.Users.FirstAsync(u => u.Id == userId);
        user.IsActive = isActive;
        await db.SaveChangesAsync();
    }

    // Evaluates a policy for a user the way a request would. No endpoint uses the policies until
    // slice 1, so this checks them directly instead of adding a test-only route to the Api.
    public async Task<bool> PassesPolicyAsync(Guid userId, string policy)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var principals = scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<AppUser>>();
        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();

        var user = await users.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException("Test setup: user not found.");

        var result = await authorization.AuthorizeAsync(await principals.CreateAsync(user), policy);
        return result.Succeeded;
    }

    // Runs every password validator the Api registered, without creating anything.
    public async Task<IdentityResult> ValidatePasswordAsync(string password)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var errors = new List<IdentityError>();
        foreach (var validator in users.PasswordValidators)
        {
            var result = await validator.ValidateAsync(users, new AppUser { UserName = "test-user" }, password);
            errors.AddRange(result.Errors);
        }

        return errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed(errors.ToArray());
    }

    // Test only: raw SQL, to prove the triggers on AuditEntries hold.
    public async Task ExecuteRawAsync(string sql)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    // Test only: how many schema objects of this type and name exist (proves what the migration created).
    public async Task<int> CountSqliteMasterAsync(string type, string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = $type AND name = $name";
            var typeParameter = command.CreateParameter();
            typeParameter.ParameterName = "$type";
            typeParameter.Value = type;
            command.Parameters.Add(typeParameter);
            var nameParameter = command.CreateParameter();
            nameParameter.ParameterName = "$name";
            nameParameter.Value = name;
            command.Parameters.Add(nameParameter);
            return Convert.ToInt32(await command.ExecuteScalarAsync());
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    public async Task<int> CountAuditAsync(Guid entityId, string entityType)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();
        return await db.AuditEntries.CountAsync(a => a.EntityId == entityId && a.EntityType == entityType);
    }

    // Test only: the history rows of one record, oldest first.
    public async Task<List<AuditEntry>> AuditRowsAsync(Guid entityId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();
        var rows = await db.AuditEntries.AsNoTracking().Where(a => a.EntityId == entityId).ToListAsync();
        return rows.OrderBy(a => a.AtUtc).ToList();
    }

    public static Task<HttpResponseMessage> LoginAsync(HttpClient client, string username, string password)
        => client.PostAsJsonAsync(ApiRoutes.Login, new LoginRequest { Username = username, Password = password });

    public static Task<HttpResponseMessage> ChangePasswordAsync(HttpClient client, string current, string next)
        => client.PostAsJsonAsync(
            ApiRoutes.ChangePassword,
            new ChangePasswordRequest { CurrentPassword = current, NewPassword = next });

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();

        // SQLite keeps pooled connections (and file handles) open; release them before deleting.
        SqliteConnection.ClearAllPools();

        try
        {
            var directory = Path.GetDirectoryName(_dbPath)!;
            var name = Path.GetFileName(_dbPath);
            foreach (var file in Directory.EnumerateFiles(directory, name + "*"))
            {
                File.Delete(file);
            }
        }
        catch (IOException)
        {
            // Best effort: a leftover temp file must not fail a test.
        }
    }

    private static void Ensure(IdentityResult result, string action)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Test setup failed to {action}: " + string.Join(", ", result.Errors.Select(e => e.Code)));
        }
    }
}

public abstract class ApiTestBase : IAsyncLifetime
{
    protected TestApp App { get; private set; } = null!;

    public virtual async Task InitializeAsync() => App = await TestApp.CreateAsync();

    public async Task DisposeAsync() => await App.DisposeAsync();

    // A new device that has already logged in successfully.
    protected async Task<HttpClient> LoggedInAsync(string username, string password)
    {
        var client = App.NewClient();
        var response = await TestApp.LoginAsync(client, username, password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }

    protected static async Task<CurrentUserResponse> GetMeAsync(HttpClient client)
    {
        var response = await client.GetAsync(ApiRoutes.Me);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var me = await response.Content.ReadFromJsonAsync<CurrentUserResponse>();
        Assert.NotNull(me);
        return me;
    }
}
