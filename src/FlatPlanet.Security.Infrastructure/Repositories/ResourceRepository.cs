using Dapper;
using FlatPlanet.Security.Application.Interfaces;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Infrastructure.Repositories;

public class ResourceRepository : IResourceRepository
{
    private readonly IDbConnectionFactory _db;

    public ResourceRepository(IDbConnectionFactory db) => _db = db;

    public async Task<IEnumerable<Resource>> GetByAppIdAsync(Guid appId)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QueryAsync<Resource>(
            "SELECT * FROM resources WHERE app_id = @AppId ORDER BY name",
            new { AppId = appId });
    }

    public async Task<Resource?> GetByIdAsync(Guid id)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<Resource>(
            "SELECT * FROM resources WHERE id = @Id", new { Id = id });
    }

    public async Task<Resource> CreateAsync(Resource resource)
    {
        using var conn = await _db.CreateConnectionAsync();
        var id = await conn.QuerySingleAsync<Guid>(
            """
            INSERT INTO resources (app_id, resource_type_id, name, identifier, status)
            VALUES (@AppId, @ResourceTypeId, @Name, @Identifier, @Status)
            RETURNING id
            """, resource);
        resource.Id = id;
        return resource;
    }

    public async Task UpdateAsync(Resource resource)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE resources SET name = @Name, identifier = @Identifier, status = @Status WHERE id = @Id",
            resource);
    }
}
