using Dapper;
using FlatPlanet.Security.Application.Interfaces;
using FlatPlanet.Security.Application.Interfaces.Repositories;
using FlatPlanet.Security.Domain.Entities;

namespace FlatPlanet.Security.Infrastructure.Repositories;

public class CompanyRepository : ICompanyRepository
{
    private readonly IDbConnectionFactory _db;

    public CompanyRepository(IDbConnectionFactory db) => _db = db;

    public async Task<Company?> GetByIdAsync(Guid id)
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QuerySingleOrDefaultAsync<Company>(
            "SELECT * FROM companies WHERE id = @Id", new { Id = id });
    }

    public async Task<IEnumerable<Company>> GetAllAsync()
    {
        using var conn = await _db.CreateConnectionAsync();
        return await conn.QueryAsync<Company>("SELECT * FROM companies ORDER BY name");
    }

    public async Task<Company> CreateAsync(Company company)
    {
        using var conn = await _db.CreateConnectionAsync();
        var id = await conn.QuerySingleAsync<Guid>(
            """
            INSERT INTO companies (name, country_code, status, code)
            VALUES (@Name, @CountryCode, @Status, @Code)
            RETURNING id
            """, company);
        company.Id = id;
        return company;
    }

    public async Task UpdateAsync(Company company)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE companies SET name = @Name, country_code = @CountryCode, code = @Code WHERE id = @Id", company);
    }

    public async Task UpdateStatusAsync(Guid id, string status)
    {
        using var conn = await _db.CreateConnectionAsync();
        await conn.ExecuteAsync(
            "UPDATE companies SET status = @Status WHERE id = @Id",
            new { Status = status, Id = id });
    }
}
