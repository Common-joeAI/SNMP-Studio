using SNMP.Core.Models;

namespace SNMP.Core.Interfaces;

public interface IV3ProfileStore
{
    Task<List<V3Profile>> LoadAsync();
    Task SaveAsync(IEnumerable<V3Profile> profiles);
    Task AddOrUpdateAsync(V3Profile profile);
    Task DeleteAsync(Guid id);
}
