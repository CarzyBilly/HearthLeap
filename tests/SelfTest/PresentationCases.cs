using System.Text.Json;
using HsAuto.Open;
using HsAuto.UnityBridge;

internal static class PresentationCases
{
    public static async Task RunAsync(Func<string, Func<Task>, Task> check, string temp)
    {
        static void Need(bool valid) { if (!valid) throw new Exception("Presentation assertion failed."); }
        static JsonElement Json(object data) => JsonSerializer.SerializeToElement(data);
        await check("盒子AI名称：旧诊断文字与版本后缀统一，正常文本不变", () =>
        {
            var old = "\u6700\u5f3a";
            Need(UiText.Normalize(old + "AI3.0 推荐 / " + old + " AI 2.0") == "盒子AI 推荐 / 盒子AI");
            Need(UiText.Normalize("盒子AI 已连接") == "盒子AI 已连接" && UiText.Normalize(null) == "");
            Need(UiText.Normalize("[错误] " + old + "ai 获取失败") == "[错误] 盒子AI 获取失败");
            return Task.CompletedTask;
        });
        await check("玩家昵称：名称未就绪、退出登录和坏类型不冒充已识别", () =>
        {
            Need(PlayerIdentityView.Parse(default).Name == "等待账号信息");
            Need(PlayerIdentityView.Parse(Json(new { loggedIn=false, playerName="旧昵称" })).Name == "等待账号信息");
            Need(PlayerIdentityView.Parse(Json(new { identityAvailable=false, playerName="旧昵称" })).Name == "等待账号信息");
            Need(PlayerIdentityView.Parse(Json(new { playerName=17 })).Name == "等待账号信息");
            return Task.CompletedTask;
        });
        await check("玩家昵称：只用账号专用字段，不把奖励、对手或邮箱当玩家", () =>
        {
            Need(PlayerIdentityView.Parse(Json(new { name="奖励路线", opponentName="对手" })).Name == "等待账号信息");
            Need(PlayerIdentityView.Parse(Json(new { playerName="a@example.invalid" })).Name == "等待账号信息");
            Need(PlayerIdentityView.Parse(Json(new { playerName="bad\nname" })).Name == "等待账号信息");
            Need(PlayerIdentityView.Parse(Json(new { playerName=new string('a',101) })).Name == "等待账号信息");
            return Task.CompletedTask;
        });
        await check("玩家昵称：中文空格正常且同进程账号变化可直接更新", () =>
        {
            Need(PlayerIdentityView.Parse(Json(new { loggedIn=true, playerName=" 玩家甲 " })).Name == "玩家甲");
            Need(PlayerIdentityView.Parse(Json(new { loggedIn=true, playerName="玩家乙" })).Name == "玩家乙");
            Need(PlayerIdentityView.Parse(Json(new { playerName="", accountName="账号昵称" })).Name == "账号昵称");
            return Task.CompletedTask;
        });
        await check("旧手填昵称兼容迁移：不复用旧名字，也不保存识别名称", async () =>
        {
            var dir=Path.Combine(temp,"old-alias"); Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(Path.Combine(dir,"settings.json"), "{\"AccountAlias\":\"旧假名\",\"DeckName\":\"原卡组\"}");
            var data=new LocalData(dir); await data.LoadAsync(); Need(data.Settings.AccountAlias=="" && data.Settings.DeckName=="原卡组");
            data.Settings.AccountAlias="本轮读取昵称"; await data.SaveAsync();
            Need(!(await File.ReadAllTextAsync(Path.Combine(dir,"settings.json"))).Contains("AccountAlias"));
        });
        await check("身份读取：优先已登录玩家的缓存昵称，不遍历好友不读取凭据", () =>
        {
            var fallback=0;
            Need(PlayerIdentityReader.ReadName(true,()=>new FakePlayer(),()=>{fallback++;return new FakeFriendly();})=="缓存玩家");
            Need(fallback==0); return Task.CompletedTask;
        });
        await check("身份读取：退出登录不调用任何玩家getter", () =>
        {
            var invoked=0;
            Need(PlayerIdentityReader.ReadName(false,()=>{invoked++;return new FakePlayer();},()=>{invoked++;return new FakeFriendly();})=="");
            Need(invoked==0); return Task.CompletedTask;
        });
        await check("身份读取：缓存缺失时只回退本地一侧玩家，不序列化SDK对象", () =>
        {
            Need(PlayerIdentityReader.ReadName(true,()=>null!,()=>new FakeFriendly())=="本地一侧玩家");
            Need(PlayerIdentityReader.ReadName(true,()=>new object(),()=>new object())=="");
            return Task.CompletedTask;
        });
        await check("身份读取：接口抛错或无效字符安全返回空名称", () =>
        {
            Need(PlayerIdentityReader.ReadName(true,()=>throw new IOException("fixture"),()=>throw new IOException("fixture"))=="");
            Need(PlayerIdentityReader.ReadName(true,()=>null!,()=>new InvalidFriendly())=="");
            return Task.CompletedTask;
        });
    }
    sealed class FakePlayer { public FakeBattleTag GetBattleTag()=>new(); public override string ToString()=>throw new Exception("SDK must not be serialized."); }
    sealed class FakeBattleTag { public string GetName()=>"缓存玩家"; public override string ToString()=>throw new Exception("SDK must not be serialized."); }
    sealed class FakeFriendly { public string GetName()=>"本地一侧玩家"; }
    sealed class InvalidFriendly { public string GetName()=>"bad\nname"; }
}
