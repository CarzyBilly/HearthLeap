using System.Text.Json;
using System.Text.RegularExpressions;

namespace HsAuto.Open;

public static class UiText
{
    // Normalize diagnostics from external payloads too, not just our own literals.
    static readonly Regex OldLabel = new("\u6700\u5f3a\\s*AI(?:\\s*[23]\\.0)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    public static string Normalize(string? text) => OldLabel.Replace(text ?? "", "盒子AI");
    static readonly Regex Timestamps = new(@"^(?:\s*\[\d{2}:\d{2}:\d{2}\]\s*)+", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    public static string WithoutTimestamp(string? text) => Timestamps.Replace(Normalize(text), "").Trim();
}

public sealed record PlayerIdentityView(string Name, string Hint)
{
    public static PlayerIdentityView Waiting(string hint = "登录炉石并连接插件后自动识别，无需手动填写。") => new("等待账号信息", hint);
    public static PlayerIdentityView Parse(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object) return Waiting();
        if (payload.TryGetProperty("loggedIn", out var login) && login.ValueKind == JsonValueKind.False)
            return Waiting("炉石尚未登录，玩家名称会在登录后自动刷新。");
        if (payload.TryGetProperty("identityAvailable", out var available) && available.ValueKind == JsonValueKind.False)
            return Waiting("玩家名称尚未就绪，稍后自动重试；旧插件请安装/修复后重启炉石。");
        // No generic `name` field: it can be a reward-track name or opponent name.
        foreach (var key in new[] { "playerName", "accountName" })
        {
            if (payload.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString()?.Trim() ?? "";
                if (text.Length > 0 && text.Length <= 100 && !text.Any(char.IsControl) && !text.Contains('@'))
                    return new(UiText.Normalize(text), "已从当前炉石客户端自动读取玩家昵称；不保存账号密码或登录凭据。");
            }
        }
        return Waiting("当前插件尚未返回玩家名称；请安装/修复插件并重启炉石，程序会自动重试。");
    }
}
