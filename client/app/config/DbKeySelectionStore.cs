// config/DbKeySelectionStore.cs
// 도메인별로 사용자가 선택한 db_key 목록을 로컬에 저장/로드한다.
// 위치: %LOCALAPPDATA%\NX_Assistant\db_selection.json
// 형식: { "MECH_STANDARD": ["mobile","water_proof"], "MECHA_DFM": ["mold_design"] }
//
// 로컬 전용 데이터이므로 서버/VDI 와 무관. 분실 시 서버 default 로 다시 선택하면 됨.

using System.Text.Json;

namespace NxAssistant.Config;

internal static class DbKeySelectionStore
{
    private static readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NX_Assistant", "db_selection.json");

    private static Dictionary<string, string[]> LoadAll()
    {
        try
        {
            if (!File.Exists(_path)) return new();
            var text = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<Dictionary<string, string[]>>(text) ?? new();
        }
        catch
        {
            return new();
        }
    }

    /// <summary>도메인의 저장된 선택. 없으면 null.</summary>
    public static string[]? Load(string domain)
    {
        if (string.IsNullOrEmpty(domain)) return null;
        var all = LoadAll();
        return all.TryGetValue(domain, out var keys) ? keys : null;
    }

    /// <summary>도메인의 선택 저장 (덮어쓰기).</summary>
    public static void Save(string domain, string[] keys)
    {
        if (string.IsNullOrEmpty(domain)) return;
        try
        {
            var all = LoadAll();
            all[domain] = keys ?? Array.Empty<string>();
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var text = JsonSerializer.Serialize(all,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_path, text);
        }
        catch
        {
            // 저장 실패는 무시 (다음 진입 때 서버 default 사용)
        }
    }
}
