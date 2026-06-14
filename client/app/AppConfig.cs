// AppConfig.cs
// 앱 전역 설정 한 곳. 우선순위: appsettings.json(앱 exe 폴더) > 환경변수 > 빌트인 기본값.
//   - 설정파일을 안정 소스로 두어 "환경변수 상속 안 됨" 문제를 근본 제거.
//   - 경로형 설정(*Dir)은 상대경로면 앱 exe 폴더 기준으로 해석 → 대량 배포에 안전.
//   - 100명 배포 시 보통 DbMcpUrl/DbMcpToken 정도만 채우면 됨(나머지는 기본값).

using System.IO;
using System.Text.Json;

namespace NxAssistant;

public static class AppConfig
{
    private static readonly Dictionary<string, string> _file = LoadMerged();

    // appsettings.json(안정·커밋) 위에 appsettings.local.json(개발·비밀·미커밋)을 덮어쓴다.
    private static Dictionary<string, string> LoadMerged()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Merge(d, "appsettings.json");
        Merge(d, "appsettings.local.json");
        return d;
    }

    private static void Merge(Dictionary<string, string> d, string fileName)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, fileName);
            if (!File.Exists(path)) return;
            var doc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(path));
            if (doc == null) return;
            foreach (var kv in doc)
            {
                if (kv.Value.ValueKind == JsonValueKind.String) d[kv.Key] = kv.Value.GetString() ?? "";
                else if (kv.Value.ValueKind == JsonValueKind.Number) d[kv.Key] = kv.Value.ToString();
            }
        }
        catch { /* 파일 없거나 깨지면 환경변수/기본값으로 폴백 */ }
    }

    // 설정파일 키 > 환경변수 > 기본값
    private static string Get(string key, string env, string def)
    {
        if (_file.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)) return v;
        var e = Environment.GetEnvironmentVariable(env);
        return !string.IsNullOrWhiteSpace(e) ? e! : def;
    }

    // 상대경로면 앱 exe 폴더 기준으로 해석(절대경로는 그대로)
    private static string ResolvePath(string p)
    {
        if (string.IsNullOrWhiteSpace(p)) return "";
        return Path.IsPathRooted(p) ? p : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, p));
    }

    private static bool Truthy(string s) => s == "1" || string.Equals(s, "true", StringComparison.OrdinalIgnoreCase);

    // ── DB 서버 ──
    public static string DbMcpUrl   => Get("DbMcpUrl",   "NX_ASSISTANT_DB_MCP_URL", "http://127.0.0.1:8766");
    public static string DbMcpToken => Get("DbMcpToken", "DB_MCP_TOKEN", "");

    // ── NX 제어 브리지 ──
    public static string NxBridgeDir    => ResolvePath(Get("NxBridgeDir", "NX_BRIDGE_DIR", ""));
    public static string NxBridgePython => Get("NxBridgePython", "NX_BRIDGE_PYTHON", "python");
    public static int    NxMcpPort      => int.TryParse(Get("NxMcpPort", "NX_MCP_PORT", "8792"), out var n) ? n : 8792;

    // ── 자동화 ──
    public static string AutomationDir    => ResolvePath(Get("AutomationDir", "NX_AUTOMATION_DIR", ""));
    public static string AutomationPython => Get("AutomationPython", "NX_AUTOMATION_PYTHON", "python");
    public static string AutomationCdp    => Get("AutomationCdp", "NX_AUTOMATION_CDP", "");
    public static string AutomationVendor => Get("AutomationVendor", "NX_AUTOMATION_VENDOR", "수원/기타출발 - 예스로지스");

    // ── 개발 플래그 (배포본엔 보통 미설정) ──
    public static bool IsVdi        => Get("Mode", "NX_ASSISTANT_MODE", "").Trim().Equals("vdi", StringComparison.OrdinalIgnoreCase);
    public static bool ShowWorker   => Truthy(Get("ShowWorker", "NX_ASSISTANT_SHOW_WORKER", ""));
    public static bool FakeDbPrompt => Truthy(Get("FakeDbPrompt", "NX_ASSISTANT_FAKE_DBPROMPT", ""));
}
