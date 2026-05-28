using System.Text.Json;
using SNMP.Core.Interfaces;
using SNMP.Core.Models;

namespace SNMP.Engine.Services;

/// <summary>Persists saved device targets to %APPDATA%\SNMPStudio\targets.json.</summary>
public sealed class TargetStore : ITargetStore
{
    private static readonly string Path =
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SNMPStudio", "targets.json");

    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented        = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<List<SavedTarget>> LoadAsync()
    {
        if (!File.Exists(Path)) return new();
        try
        {
            await using var fs = File.OpenRead(Path);
            return await JsonSerializer.DeserializeAsync<List<SavedTarget>>(fs, Opts) ?? new();
        }
        catch { return new(); }
    }

    public async Task SaveAsync(IEnumerable<SavedTarget> targets)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var tmp = Path + ".tmp";
        await using (var fs = File.Create(tmp))
            await JsonSerializer.SerializeAsync(fs, targets.ToList(), Opts);
        File.Move(tmp, Path, overwrite: true);
    }

    public async Task AddOrUpdateAsync(SavedTarget target)
    {
        var list = await LoadAsync();
        var idx  = list.FindIndex(t => t.Id == target.Id);
        if (idx >= 0) list[idx] = target;
        else list.Insert(0, target);
        await SaveAsync(list);
    }

    public async Task DeleteAsync(Guid id)
    {
        var list = await LoadAsync();
        list.RemoveAll(t => t.Id == id);
        await SaveAsync(list);
    }
}
