using System.Text.RegularExpressions;

namespace HsAuto.Open;

// Presentation only: never changes the engine's decisions, errors or game actions.
// A null result means a technical detail belongs in the diagnostic file, not the player feed.
public static class PlayerLog
{
    static Regex Pattern(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    static readonly Regex Tags = Pattern(@"^(?:\[(?:传统导航|传统执行|推荐|只读|卡组)\]\s*)+");
    static readonly Regex Technical = Pattern(@"\b(?:PID|CEF|RID|OpenProcess|ReadProcessMemory|Win32|callback|payload|optionId|entityId|cardId|OptionsPacket|MulliganManager|GameState|HsAuto|HSAng|BepInEx|hsmm|UnityBridge)\b|Bridge|0x[0-9a-f]+|[A-Za-z_][\w.]*\([^)]*\)");
    static readonly Regex CardNames = Pattern(@"要求替换起手\s+([^(/]+)\(");
    static readonly Regex Codes = Pattern(@"\([^)]*(?:实体|位置|[A-Z]+_\d)[^)]*\)");
    static readonly Regex Path = Pattern(@"[A-Z]:[\\/][^；。\r\n]*");

    public static string? ForDisplay(string? input)
    {
        var original = UiText.WithoutTimestamp(input);
        if (original.Length == 0) return null;
        bool error = original.StartsWith("[错误]") || original.StartsWith("[停止]");
        if (original.StartsWith("[卡组]")) return "卡组暂未就绪，请回到大厅后刷新。";
        var text = Tags.Replace(original, "");
        if (error) text = text[(text.IndexOf(']') + 1)..].Trim();
        var result = Simplify(text, error);
        if (result == null) return null;
        if (original.StartsWith("[只读]") && !result.Contains("观察模式")) result = "观察模式：" + result;
        return error ? (original.StartsWith("[停止]") ? "已停止：" : "操作失败：") + result : result;
    }

    static string? Simplify(string text, bool error)
    {
        // Failure checks precede routine scan suppression so important warnings are not hidden.
        if (text.StartsWith("[恢复] 运行异常")) return "运行暂时异常，3秒后自动重新连接。";
        if (text.StartsWith("[恢复]")) return text[4..].Trim();
        if (text.Contains("插件检查/安装未完成")) return "插件未能安装，3秒后自动重试；请检查安装路径和文件权限。";
        if (text.Contains("仍没有可用建议") && text.Contains("自动重新连接")) return "暂未收到盒子建议，正在自动重新连接。";
        if (text.Contains("游戏操作阶段连续") && text.Contains("自动重新连接")) return "游戏操作暂未就绪，正在自动重新连接。";
        if (text.Contains("动作已提交但未观察到完成") && text.Contains("自动重连")) return "正在自动重新连接并核实上一操作，不会重复出牌。";
        if (text.Contains("动作失败") && text.Contains("自动恢复")) return "游戏操作未完成，正在读取新局面并自动恢复。";
        if (text.Contains("另一个线程拥有该对象")) return "界面更新异常，请重启软件；详细原因已记录。";
        if (text.Contains("权限等级不一致") || text.Contains("OpenProcess=失败") || text.Contains("访问被拒绝") || text.Contains("Access is denied"))
            return "无法读取盒子，请用相同权限启动本软件和炉石盒子后重试。";
        if (text.Contains("[推荐未就绪]") || text.Contains("仍没有可用建议"))
            return text.Contains("只读观察继续") ? "暂未收到盒子建议，请检查盒子；观察模式继续。" : "长时间未收到盒子建议，脚本已停止。请检查盒子是否提供建议后重试。";
        if (text.Contains("动作已提交但未观察到完成")) return "未能确认操作完成，已停止以免重复出牌。请检查游戏画面后重试。";
        if (text.Contains("游戏操作阶段连续60秒未就绪")) return "已收到盒子建议，但游戏画面长时间未就绪，脚本已停止。请检查开场画面。";
        if (text.Contains("动作失败")) return "游戏操作未完成，已停止以免重复出牌。请检查游戏画面和插件。";
        if (text.Contains("自动恢复失败")) return "重启盒子失败，请手动重启盒子后重试。";
        if (text.Contains("取消盒子自动恢复") || text.Contains("取消盒子联动关闭")) return "无法确认盒子连接的是当前游戏，已取消自动恢复。请检查盒子连接。";
        if (text.Contains("未连接开源插件") || text.Contains("未连接") && text.Contains("Bridge")) return "游戏插件未连接，请安装/修复插件后重启炉石。";
        if (text.Contains("需要 0.1.1 插件")) return "游戏插件需要更新，请退出炉石后安装/修复插件。";
        if (text.Contains("路径无效")) return text.Contains("盒子") ? "炉石盒子路径无效，请重新定位盒子主程序。" : "炉石路径无效，请重新定位游戏主程序。";
        if (text.Contains("请先定位 Hearthstone.exe")) return "请先定位炉石主程序。";
        if (text.Contains("没有自动识别到炉石盒子")) return "未找到炉石盒子，请安装盒子或手动定位后重新检测。";
        if (error) return PlainFallback(text, true);
        if (text.Contains("请通过战网启动并登录炉石")) return "请通过战网启动并登录炉石，正在等待（最多 120 秒，可随时停止）。";
        if (text.Contains("正在连接炉石")) return "正在连接游戏和炉石盒子…";
        if (text.StartsWith("UnityBridge 协议") && text.Contains("已连接")) return "已连接游戏插件。";
        if (text.StartsWith("当前推荐源：")) return "推荐来源：盒子AI。";
        if (text.StartsWith("已确认盒子AI绑定")) return null;
        if (text.StartsWith("已连接盒子AI")) return "已连接炉石盒子，等待建议。";
        if (text.Contains("已识别并保存炉石盒子和炉石路径")) return "已找到炉石和盒子，安装路径已保存。";
        if (text.Contains("引擎设置已保存")) return "设置已保存，下次开始脚本时生效。";
        if (text.StartsWith("已定位盒子AI")) return text.Contains("胜率") ? "已找到盒子胜率信息。" : "已找到盒子AI建议。";
        if (text.Contains("盒子AI已启动")) return "盒子已启动，进入对局后等待建议。";
        if (text.Contains("传统对战策略已就绪")) return "准备就绪。";
        if (text.Contains("新一局起手阶段")) return "对局开始，等待起手建议。";
        if (text.Contains("开局建议尚未就绪，已重新定位")) return "开局建议暂未就绪，正在自动重新读取，无需停止再开始。";
        if (text.Contains("起手换牌状态尚未稳定")) return "起手建议已收到，等待开场动画和手牌就绪。";
        if (text.Contains("等待期间起手状态已更新")) return "起手画面正在更新，稍后继续换牌。";
        if (text.Contains("起手换牌已提交")) return "已确认起手，等待发牌和首回合。";
        if (text.Contains("编号已重置") || text.Contains("已唯一匹配客户端合法动作")) return null;
        if (text.Contains("盒子AI CEF 候选") || text.Contains("盒子AI诊断") || text.Contains("盒子AI胜率候选")) return null;
        if (text.Contains("CEF 扫描")) return null; // recommendation status messages already report waiting/success
        if (text.Contains("触发盒子自动恢复")) return "建议长时间未更新，正在重启盒子…";
        if (text.Contains("盒子已重启并重新绑定")) return "盒子已重启，等待当前对局建议。";
        if (text.StartsWith("等待盒子确认")) return "正在确认盒子与游戏的连接…";
        if (text.Contains("已复用当前盒子")) return "已恢复盒子连接，无需重新绑定账号。";
        if (text.Contains("主界面已就绪")) return "游戏大厅已打开，等待画面稳定…";
        if (text.Contains("主界面准备完成")) return "大厅准备完成。";
        if (text.Contains("已点击主菜单传统对战入口")) return "正在进入传统对战…";
        if (text.Contains("切换传统对战模式")) return "已选择模式：" + Tail(text) + "。";
        if (text.Contains("选择传统套牌")) return "已选择卡组：" + Tail(text) + "。";
        if (text.Contains("开始按钮，进入匹配") || text.Contains("当前处于匹配中")) return "正在匹配对手…";
        if (text.Contains("未找到盒子卡牌")) return "卡牌信息尚未同步，等待新的盒子建议。";
        if (text.Contains("等待进入起手、我方回合")) return "等待我方操作阶段。";
        if (text.Contains("等待盒子AI起手推荐")) return "等待盒子AI起手建议。";
        if (text.Contains("等待") && (text.Contains("OptionsPacket") || text.Contains("费用同步"))) return "等待游戏状态更新。";
        if (text.Contains("起手换掉"))
        {
            var cards = CardNames.Matches(text).Select(m => m.Groups[1].Value.Trim()).Distinct().ToArray();
            return cards.Length > 0 ? "盒子建议换掉：" + string.Join("、", cards) + "。" : "盒子建议更换起手牌。";
        }
        if (text.Contains("起手替换牌") && text.Contains("已通过")) return null;
        if (text.Contains("确认起手换牌") && text.Contains("已通过")) return "已确认起手换牌。";
        if (text.Contains("起手保留")) return "盒子建议保留起手牌。";
        if (text.StartsWith("确认起手")) return "盒子建议确认起手牌。";
        if (text.Contains("结束") && text.Contains("回合")) return text.Contains("已通过") ? "已结束回合。" : "盒子建议结束回合。";
        if (text.StartsWith("打出手牌") || text.StartsWith("指定目标出牌")) return text.Contains("已通过") ? "已出牌。" : "盒子建议出牌。";
        if (text.StartsWith("攻击")) return text.Contains("已通过") ? "已完成攻击。" : "盒子建议攻击。";
        if (text.Contains("英雄技能") && !error) return text.Contains("已通过") ? "已使用英雄技能。" : "盒子建议使用英雄技能。";
        if (text.Contains("激活地标") && !error) return text.Contains("已通过") ? "已使用地标。" : "盒子建议使用地标。";
        if (text.StartsWith("选择选项") && !error) return "正在选择卡牌或选项。";
        if (text.StartsWith("交易卡牌") && !error) return "正在交易卡牌。";
        if (text.StartsWith("等待：")) return Simplify(text[3..].Trim(), error);
        if (text.Contains("Bridge 安装完成")) return "插件安装完成，请通过战网重新打开炉石。";
        if (text.StartsWith("已删除 HS Auto")) return "插件已删除，其他插件和备份已保留。";
        if (text.Contains("本项目插件未安装")) return "插件未安装，无需删除。";
        if (text.StartsWith("已恢复该备份")) return "插件备份已恢复，请重新打开炉石。";
        return PlainFallback(text, error);
    }

    static string? PlainFallback(string text, bool error)
    {
        if (text.Contains("调用线程") || text.Contains("System.") || text.Contains("Exception")) return "发生程序异常，请重启软件；详细原因已记录。";
        if (Technical.IsMatch(text))
            return error || new[] { "失败", "无法", "超时", "错误", "未能", "缺少", "不支持" }.Any(text.Contains)
                ? "操作未完成，请检查游戏、盒子和插件；详细原因已记录。" : null;
        text = Codes.Replace(text, "");
        text = Path.Replace(text, "（详见诊断日志）");
        if (!Regex.IsMatch(text, @"[\u4e00-\u9fff]")) return error ? "操作未完成，详细原因已记录。" : null;
        // A full technical stack trace is never placed in the player feed.
        text = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
        return text.Length > 130 ? text[..130] + "…（详情已记录）" : text;
    }
    static string Tail(string text) => text[(text.LastIndexOf('：') + 1)..].Trim().TrimEnd('。');
}

public sealed class PlayerLogFeed
{
    readonly Dictionary<string, DateTimeOffset> recent = new(StringComparer.Ordinal);
    string last = "";
    public string? Accept(string? raw, DateTimeOffset now)
    {
        var text = PlayerLog.ForDisplay(raw);
        if (text == null) return null;
        bool warning = text.StartsWith("已停止：") || text.StartsWith("操作失败：");
        // Consecutive waiting notices need not keep repeating. Actions can repeat in a turn.
        bool waiting = text.StartsWith("等待") || text.Contains("等待建议") || text.Contains("尚未同步") || text.StartsWith("正在确认");
        if (waiting && text == last) return null;
        bool action = text.StartsWith("盒子建议") || text.StartsWith("观察模式：盒子建议") || text is "已出牌。" or "已完成攻击。" or "已结束回合。" or "已使用英雄技能。" or "已使用地标。" or "正在选择卡牌或选项。" or "正在交易卡牌。";
        if (!action && recent.TryGetValue(text, out var before) && now - before < TimeSpan.FromSeconds(warning ? 3 : 2)) return null;
        if (recent.Count >= 256)
            foreach (var key in recent.Where(p => now - p.Value > TimeSpan.FromMinutes(1)).Select(p => p.Key).ToArray()) recent.Remove(key);
        if (recent.Count >= 256) recent.Clear();
        recent[text] = now; last = text;
        return text;
    }
}
