using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SNMP.Core.Interfaces;
using SNMP.Core.Models;

namespace SNMP.Engine.Services;

/// <summary>
/// Persists V3Profile list to %APPDATA%\SNMPStudio\v3profiles.json.
/// Passwords are encrypted with Windows DPAPI (CurrentUser scope) — never stored plaintext.
/// </summary>
public sealed class V3ProfileStore : IV3ProfileStore
{
    private static readonly string FilePath =
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SNMPStudio", "v3profiles.json");

    private static readonly JsonSerializerOptions Opts = new()
        { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<List<V3Profile>> LoadAsync()
    {
        if (!File.Exists(FilePath)) return new();
        try
        {
            await using var fs = File.OpenRead(FilePath);
            return await JsonSerializer.DeserializeAsync<List<V3Profile>>(fs, Opts) ?? new();
        }
        catch { return new(); }
    }

    public async Task SaveAsync(IEnumerable<V3Profile> profiles)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath)!);
        var tmp = FilePath + ".tmp";
        await using (var fs = File.Create(tmp))
            await JsonSerializer.SerializeAsync(fs, profiles.ToList(), Opts);
        File.Move(tmp, FilePath, overwrite: true);
    }

    public async Task AddOrUpdateAsync(V3Profile profile)
    {
        var list = await LoadAsync();
        var idx  = list.FindIndex(p => p.Id == profile.Id);
        if (idx >= 0) list[idx] = profile; else list.Insert(0, profile);
        await SaveAsync(list);
    }

    public async Task DeleteAsync(Guid id)
    {
        var list = await LoadAsync();
        list.RemoveAll(p => p.Id == id);
        await SaveAsync(list);
    }

    // ── DPAPI helpers ─────────────────────────────────────────────────────────

    public static string Encrypt(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return string.Empty;
        var bytes     = Encoding.UTF8.GetBytes(plaintext);
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    public static string Decrypt(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext)) return string.Empty;
        try
        {
            var bytes     = Convert.FromBase64String(ciphertext);
            var decrypted = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch { return string.Empty; }
    }
}
