using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using HsAuto.Core.Models;
using HsAuto.Core.Automation;
using HsAuto.Core.Configuration;
using HsAuto.Core.Strategy;

namespace HsAuto.Open;

public sealed class Settings
{
    public string BoxPath { get; set; } = "";
    public string GamePath { get; set; } = "";
    public string DeckName { get; set; } = "";
    public string Format { get; set; } = "Standard";
    public bool AutoOpenBox { get; set; } = true;
    public bool AutoQueue { get; set; } = false;
    public bool ReadOnly { get; set; } = true;
    public bool ReduceMotion { get; set; } = false;
    public TraditionalSettings Traditional { get; set; } = new();
    public int PollMs { get; set; } = 900;
    public RunStopSettings Time { get; set; } = new();
    // Compatibility with old callers only; stale manual names are not loaded/saved.
    [System.Text.Json.Serialization.JsonIgnore]
    public string AccountAlias { get; set; } = "";
}
public sealed record FoundApp(string Path, string Source, int? Pid = null)
{
    public bool Found => !string.IsNullOrWhiteSpace(Path);
}
public sealed record MatchRow(string Id, DateTimeOffset StartedAt, DateTimeOffset FinishedAt, string Format, string Result);
public sealed class LocalData
{
    public string DirectoryPath { get; }
    public Settings Settings { get; private set; } = new();
    readonly object recordsLock = new();
    List<MatchRow> records = [];
    public IReadOnlyList<MatchRow> Matches { get { lock (recordsLock) return records.ToArray(); } }
    readonly SemaphoreSlim gate = new(1, 1);
    public string RecoveryMessage { get; private set; } = "";
    public LocalData(string directory = null) => DirectoryPath = directory ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HsAuto.OpenSource.Glass");
    public async Task LoadAsync()
    {
        System.IO.Directory.CreateDirectory(DirectoryPath);
        Settings = await LoadFile<Settings>("settings.json");
        var loadedRecords = await LoadFile<List<MatchRow>>("matches.json");
        lock (recordsLock) records = loadedRecords;
        Settings.PollMs = Math.Clamp(Settings.PollMs, 500, 5000);
        if (!new[] { "Standard", "Wild", "Casual" }.Contains(Settings.Format)) Settings.Format = "Standard";
        Settings.Traditional ??= new TraditionalSettings();
        Settings.Traditional.Normalize();
        Settings.Time ??= new RunStopSettings(); Settings.Time.Normalize();
    }
    async Task<T> LoadFile<T>(string name) where T : new()
    {
        var path = System.IO.Path.Combine(DirectoryPath, name);
        if (!File.Exists(path)) return new();
        try { return JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path)) ?? new T(); }
        catch (JsonException)
        {
            File.Copy(path, path + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"), false);
            RecoveryMessage = "损坏的配置已备份，使用默认设置；原文件未丢弃。";
            return new();
        }
    }
    public async Task SaveAsync()
    {
        await gate.WaitAsync();
        try { await WriteAtomic("settings.json", Settings); await WriteAtomic("matches.json", Matches); }
        finally { gate.Release(); }
    }
    async Task WriteAtomic<T>(string name, T value)
    {
        var path = System.IO.Path.Combine(DirectoryPath, name); var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, true);
    }
    public async Task<bool> AddMatchAsync(MatchRow record)
    {
        lock (recordsLock)
        {
            if (record.Result is not ("Win" or "Loss" or "Draw") || records.Any(m => m.Id == record.Id)) return false;
            records.Add(record);
        }
        await SaveAsync(); return true;
    }
}

public static class ActionConfirmation
{
    public static bool Changed(ConstructedAction action, ConstructedGameState before, ConstructedGameState after)
    {
        if (!StateSafety.IsReadable(after)) return false;
        if (action.Type == ConstructedActionType.Concede) return after.Phase == ConstructedPhase.GameOver;
        if (after.MatchId != before.MatchId || after.Phase == ConstructedPhase.GameOver) return true;
        if (action.Type == ConstructedActionType.EndTurn) return after.Turn != before.Turn || after.Phase == ConstructedPhase.OpponentTurn;
        if (action.Type is ConstructedActionType.ConfirmMulligan or ConstructedActionType.MulliganReplace) return after.Phase != ConstructedPhase.Mulligan || !before.Hand.Select(e => e.EntityId).SequenceEqual(after.Hand.Select(e => e.EntityId));
        if (action.Type == ConstructedActionType.ChooseOption) return before.Phase != after.Phase || !before.DiscoverOptions.Select(e => e.EntityId).SequenceEqual(after.DiscoverOptions.Select(e => e.EntityId)) || !before.ChoiceOptions.Select(e => e.EntityId).SequenceEqual(after.ChoiceOptions.Select(e => e.EntityId));
        if (action.Type is ConstructedActionType.PlayCard or ConstructedActionType.PlayCardWithTarget or ConstructedActionType.TradeCard)
            return before.Hand.Any(e => e.EntityId == action.SourceEntityId) && !after.Hand.Any(e => e.EntityId == action.SourceEntityId);
        if (before.Tags.TryGetValue("unity.optionsPacketId", out var old) && after.Tags.TryGetValue("unity.optionsPacketId", out var next) && old != next && next != "0") return true;
        return false;
    }
}
