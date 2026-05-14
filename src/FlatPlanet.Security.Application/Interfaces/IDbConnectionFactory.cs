using System.Data;

namespace FlatPlanet.Security.Application.Interfaces;

public interface IDbConnectionFactory : IAsyncDisposable
{
    Task<IDbConnection> CreateConnectionAsync(CancellationToken cancellationToken = default);
}
