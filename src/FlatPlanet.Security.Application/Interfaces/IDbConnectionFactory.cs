using System.Data;

namespace FlatPlanet.Security.Application.Interfaces;

public interface IDbConnectionFactory
{
    Task<IDbConnection> CreateConnectionAsync();
}
