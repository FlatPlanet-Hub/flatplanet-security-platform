using Dapper;
using FlatPlanet.Security.Application.Interfaces;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Domain.Entities;
using Npgsql;

namespace FlatPlanet.Security.Infrastructure.Repositories;

public class AppRepository : IAppRepository
{
    private readonly IDbConnectionFactory _db;

    public AppRepository(IDbConnectionFactory db) => _db = db;

    public async Task<App?> GetByIdAsync(Guid id)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<App>(
            "SELECT * FROM apps WHERE id = @Id", new { Id = id });
    }

    public async Task<App?> GetBySlugAsync(string slug)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<App>(
            "SELECT * FROM apps WHERE slug = @Slug", new { Slug = slug });
    }

    public async Task<IEnumerable<App>> GetAllAsync()
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QueryAsync<App>("SELECT * FROM apps ORDER BY name");
    }

    public async Task<IEnumerable<App>> GetByIdsAsync(IEnumerable<Guid> ids)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QueryAsync<App>(
            "SELECT * FROM apps WHERE id = ANY(@Ids)",
            new { Ids = ids.ToArray() });
    }

    public async Task<App> CreateAsync(App app)
    {
        using var conn = await _db.CreateConnectionAsync();
        var id = await conn.QuerySingleAsync<Guid>(
            """
            INSERT INTO apps (company_id, name, slug, base_url, registered_by)
            VALUES (@CompanyId, @Name, @Slug, @BaseUrl, @RegisteredBy)
            RETURNING id
            """, app);
        app.Id = id;
        return app;
    }

    public async Task UpdateAsync(App app)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE apps SET name = @Name, base_url = @BaseUrl, status = @Status WHERE id = @Id", app);
    }

    public async Task DeleteAsync(Guid id)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync("DELETE FROM apps WHERE id = @Id", new { Id = id });
    }

    public async Task UpdateSlugAsync(Guid id, string newSlug)
    {
        using var conn = await _db.CreateConnectionAsync();
        try
        {
            await conn.ExecuteAsync(
                "UPDATE apps SET slug = @Slug WHERE id = @Id::uuid",
                new { Slug = newSlug, Id = id });
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            throw new InvalidOperationException(
                $"An app with slug '{newSlug}' already exists.", ex);
        }
    }
}
