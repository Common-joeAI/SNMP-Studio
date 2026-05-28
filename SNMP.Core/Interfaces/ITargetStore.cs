using SNMP.Core.Models;

namespace SNMP.Core.Interfaces;

public interface ITargetStore
{
    Task<List<SavedTarget>> LoadAsync();
    Task SaveAsync(IEnumerable<SavedTarget> targets);
    Task AddOrUpdateAsync(SavedTarget target);
    Task DeleteAsync(Guid id);
}
