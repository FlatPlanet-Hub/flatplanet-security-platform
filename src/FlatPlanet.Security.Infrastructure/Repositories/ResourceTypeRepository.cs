using Dapper;
using FlatPlanet.Security.Application.Interfaces;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Infrastructure.Repositories;

public class ResourceTypeRepository : IResourceTypeRepository
{
    private readonly IDbConnectionFactory _db;

    public ResourceTypeRepository(IDbConnectionFactory db) => _db = db;

    public async Task<IEnumerable<ResourceType>> GetAllAsync()
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QueryAsync<ResourceType>("SELECT * FROM resource_types ORDER BY name");
    }

    public async Task<ResourceType?> GetByIdAsync(Guid id)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<ResourceType>(
            "SELECT * FROM resource_types WHERE id = @Id", new { Id = id });
    }

    public async Task<ResourceType> CreateAsync(ResourceType resourceType)
    {
        using var conn = await _db.CreateConnectionAsync();
        var id = await conn.QuerySingleAsync<Guid>(
            """
            INSERT INTO resource_types (name, description)
            VALUES (@Name, @Description)
            RETURNING id
            """, resourceType);
        resourceType.Id = id;
        return resourceType;
    }
}
