using HsAuto.Open;

internal static class PlayerLogCases
{
    public static async Task RunAsync(Func<string, Func<Task>, Task> check)
    {
        static void Need(bool ok) { if (!ok) throw new Exception("Player log assertion failed."); }
        await check("玩家日志：打开盒子提示和引擎设置保存提示准确展示", () =>
        {
            Need(PlayerLog.ForDisplay("已打开炉石盒子。请正常启动炉石。") == "已打开炉石盒子。请正常启动炉石。");
            Need(PlayerLog.ForDisplay("引擎设置已保存，下次启动脚本生效。") == "设置已保存，下次开始脚本时生效。");
            return Task.CompletedTask;
        });
        await check("玩家日志：重复时间戳与旧AI名称归一且诊断内容保留", () =>
        {
            Need(UiText.WithoutTimestamp("[23:14:22] [23:14:22] UnityBridge 协议 1 已连接，PID=35776") == "UnityBridge 协议 1 已连接，PID=35776");
            Need(PlayerLog.ForDisplay("[23:14:22] [23:14:22] UnityBridge 协议 1 已连接，PID=35776") == "已连接游戏插件。");
            Need(PlayerLog.ForDisplay("[23:14:22] 已连接" + "\u6700\u5f3aAI3.0：C:\\private\\HSAng.exe") == "已连接炉石盒子，等待建议。");
            return Task.CompletedTask;
        });
        await check("玩家日志：扫描细节不刷屏但权限失败不能隐藏", () =>
        {
            Need(PlayerLog.ForDisplay("盒子AI CEF 候选：PID=36920，CEF 版本=108.4，权限=高(RID=0x3000)")==null);
            Need(PlayerLog.ForDisplay("盒子AI CEF 扫描[完整扫描]：OpenProcess=成功，ReadProcessMemory 成功=418，payload 数=0。")==null);
            Need(PlayerLog.ForDisplay("盒子AI CEF 扫描[完整扫描]：OpenProcess=失败，payload 数=0。")!.Contains("相同权限"));
            Need(PlayerLog.ForDisplay("[停止] Bridge 请求超时")!.StartsWith("已停止：操作未完成"));
            return Task.CompletedTask;
        });
        await check("玩家日志：准备匹配模式与卡组中文展示", () =>
        {
            Need(PlayerLog.ForDisplay("[传统导航] 已通过内部方法切换传统对战模式：狂野模式")=="已选择模式：狂野模式。");
            Need(PlayerLog.ForDisplay("[传统导航] 已通过内部方法选择传统套牌：污手战")=="已选择卡组：污手战。");
            Need(PlayerLog.ForDisplay("[传统导航] 已确认当前处于匹配中：MatchingPopup3D 实例 -501478，累计 00:02。")=="正在匹配对手…");
            return Task.CompletedTask;
        });
        await check("玩家日志：建议和执行结果区分且不暴露实体编号", () =>
        {
            Need(PlayerLog.ForDisplay("[推荐] 打出手牌 13：客户端内部 OptionsPacket 确认该动作合法")=="盒子建议出牌。");
            Need(PlayerLog.ForDisplay("[传统执行] 打出手牌 13 已通过内部 OptionsPacket 提交")=="已出牌。");
            Need(PlayerLog.ForDisplay("[只读] 攻击 13，目标 66：客户端内部 OptionsPacket 确认源和目标合法")=="观察模式：盒子建议攻击。");
            Need(PlayerLog.ForDisplay("[传统执行] 结束传统对战回合已通过 GameState.SendOption 执行")=="已结束回合。");
            Need(PlayerLog.ForDisplay("[错误] 打出手牌 13：OptionsPacket 操作失败")!.StartsWith("操作失败："));
            return Task.CompletedTask;
        });
        await check("玩家日志：换牌显示真实卡名而不是cardId和位置", () =>
        {
            var line="[推荐] 起手换掉 16：盒子AI要求替换起手 眺望陆地(CAP_102,实体16,位置1) / 起手换掉 17：盒子AI要求替换起手 海盗藏品(BT_124,实体17,位置2) / 确认起手：完成后确认";
            Need(PlayerLog.ForDisplay(line)=="盒子建议换掉：眺望陆地、海盗藏品。");
            Need(PlayerLog.ForDisplay("[传统执行] 已通过 MulliganManager 内部选中起手替换牌 16")==null);
            Need(PlayerLog.ForDisplay("[传统执行] 已通过 MulliganManager 内部确认起手换牌")=="已确认起手换牌。");
            return Task.CompletedTask;
        });
        await check("玩家日志：缺推荐与动作失败保留处理建议", () =>
        {
            Need(PlayerLog.ForDisplay("[推荐未就绪] Bridge 已连接，但等待 30 秒仍没有可用建议。已停止脚本。")!.Contains("脚本已停止"));
            Need(PlayerLog.ForDisplay("[推荐未就绪] Bridge 已连接，仍没有可用建议。只读观察继续，不提交游戏操作。")!.Contains("观察模式继续"));
            Need(PlayerLog.ForDisplay("[停止] 动作已提交但未观察到完成：PlayCard")!.Contains("以免重复出牌"));
            Need(PlayerLog.ForDisplay("[错误] 调用线程无法访问此对象，因为另一个线程拥有该对象。")!.Contains("界面更新异常"));
            return Task.CompletedTask;
        });
        await check("玩家日志：连续等待去重但快连续两次出牌结果保留", () =>
        {
            var feed=new PlayerLogFeed();var now=DateTimeOffset.UtcNow;
            var waiting="等待客户端生成本手 OptionsPacket，暂不读取或执行旧推荐";
            Need(feed.Accept(waiting,now)=="等待游戏状态更新。");
            Need(feed.Accept("[推荐] 等待："+waiting,now.AddSeconds(8))==null);
            var play="[传统执行] 打出手牌 13 已通过内部 OptionsPacket 提交";
            Need(feed.Accept(play,now.AddSeconds(9))=="已出牌。");
            Need(feed.Accept(play.Replace("13","30"),now.AddMilliseconds(9300))=="已出牌。");
            Need(feed.Accept(waiting,now.AddSeconds(10))=="等待游戏状态更新。");
            return Task.CompletedTask;
        });
        await check("玩家日志：错误前缀、数值限制和纯中文原因保留", () =>
        {
            Need(PlayerLog.ForDisplay("[错误] 赢后投降场数需要输入 1～999 的整数。")=="操作失败：赢后投降场数需要输入 1～999 的整数。");
            Need(PlayerLog.ForDisplay("[停止] 无法确认当前排位段位，已停止匹配；请等游戏账号数据就绪后重试。")!.Contains("当前排位段位"));
            Need(PlayerLog.ForDisplay("请等待 120 秒后重试。")=="请等待 120 秒后重试。");
            Need(PlayerLog.ForDisplay("[卡组] unknown_command")!.Contains("回到大厅"));
            Need(PlayerLog.ForDisplay(null)==null && PlayerLog.ForDisplay("")==null);
            return Task.CompletedTask;
        });
    }
}
