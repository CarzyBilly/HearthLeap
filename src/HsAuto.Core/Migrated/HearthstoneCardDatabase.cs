// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace HsAuto.Core.Cards;
public sealed class HearthstoneCardDatabase
{
    private static readonly Regex HtmlTagPattern = new Regex("<[^>]+>", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex WhitespacePattern = new Regex("\\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Lazy<HearthstoneCardDatabase> DefaultHolder = new Lazy<HearthstoneCardDatabase>(LoadDefault, LazyThreadSafetyMode.ExecutionAndPublication);
    private readonly IReadOnlyDictionary<string, HearthstoneCardMetadata> _byCardId;
    private readonly IReadOnlyDictionary<int, HearthstoneCardMetadata> _byDbfId;
    public static HearthstoneCardDatabase Default => DefaultHolder.Value;
    public int Count => _byCardId.Count;

    public bool IsLoaded
    {
        get
        {
            if (Count > 0)
            {
                return string.IsNullOrWhiteSpace(LoadError);
            }

            return false;
        }
    }

    public string SourcePath { get; }
    public string LoadError { get; }

    private HearthstoneCardDatabase(IReadOnlyDictionary<string, HearthstoneCardMetadata> byCardId, IReadOnlyDictionary<int, HearthstoneCardMetadata> byDbfId, string sourcePath, string loadError)
    {
        _byCardId = byCardId;
        _byDbfId = byDbfId;
        SourcePath = sourcePath;
        LoadError = loadError;
    }

    public static HearthstoneCardDatabase Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("HDT card database path cannot be empty.", "path");
        }

        string fullPath = Path.GetFullPath(path);
        using FileStream utf8Json = File.Open(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using JsonDocument jsonDocument = JsonDocument.Parse(utf8Json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip, MaxDepth = 64 });
        Dictionary<string, HearthstoneCardMetadata> byCardId = new Dictionary<string, HearthstoneCardMetadata>(StringComparer.OrdinalIgnoreCase);
        Dictionary<int, HearthstoneCardMetadata> byDbfId = new Dictionary<int, HearthstoneCardMetadata>();
        if (jsonDocument.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in jsonDocument.RootElement.EnumerateArray())
            {
                AddCard(item);
            }
        }
        else
        {
            if (jsonDocument.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("HDT card database root must be a JSON array or an id-keyed object.");
            }

            foreach (JsonProperty item2 in jsonDocument.RootElement.EnumerateObject())
            {
                AddCard(item2.Value, item2.Name);
            }
        }

        return new HearthstoneCardDatabase(byCardId, byDbfId, fullPath, "");
        void AddCard(JsonElement element, string fallbackId = "")
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                string text = ReadString(element, "id");
                if (string.IsNullOrWhiteSpace(text))
                {
                    text = fallbackId;
                }

                int num = ReadInt(element, "dbfId");
                if (!string.IsNullOrWhiteSpace(text) || num > 0)
                {
                    string text2 = ReadString(element, "text");
                    IReadOnlyList<string> readOnlyList = ReadStringArray(element, "races");
                    if (readOnlyList.Count == 0)
                    {
                        string text3 = ReadString(element, "race");
                        readOnlyList = (string.IsNullOrWhiteSpace(text3) ? Array.Empty<string>() : new string[1]
                        {
                            text3
                        }

                        );
                    }

                    HearthstoneCardMetadata value = new HearthstoneCardMetadata
                    {
                        Id = text,
                        DbfId = num,
                        Name = ReadString(element, "name"),
                        Text = text2,
                        PlainText = NormalizeRulesText(text2),
                        Type = ReadString(element, "type"),
                        CardClass = ReadString(element, "cardClass"),
                        Set = ReadString(element, "set"),
                        Rarity = ReadString(element, "rarity"),
                        SpellSchool = ReadString(element, "spellSchool"),
                        Cost = ReadInt(element, "cost"),
                        Attack = ReadInt(element, "attack"),
                        Health = ReadInt(element, "health"),
                        Durability = ReadInt(element, "durability"),
                        SpellDamage = ReadInt(element, "spellDamage"),
                        Collectible = ReadBool(element, "collectible"),
                        Mechanics = ReadStringArray(element, "mechanics"),
                        Races = readOnlyList,
                        ReferencedTags = ReadStringArray(element, "referencedTags")
                    };
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        byCardId[text] = value;
                    }

                    if (num > 0)
                    {
                        byDbfId[num] = value;
                    }
                }
            }
        }
    }

    public bool TryGetByCardId(string? cardId, out HearthstoneCardMetadata metadata)
    {
        if (!string.IsNullOrWhiteSpace(cardId) && _byCardId.TryGetValue(cardId.Trim(), out metadata))
        {
            return true;
        }

        metadata = null;
        return false;
    }

    public bool TryGetByDbfId(int dbfId, out HearthstoneCardMetadata metadata)
    {
        if (dbfId > 0 && _byDbfId.TryGetValue(dbfId, out metadata))
        {
            return true;
        }

        metadata = null;
        return false;
    }

    private static HearthstoneCardDatabase LoadDefault()
    {
        string[] array = BuildDefaultPathCandidates().ToArray();
        string text = array.FirstOrDefault(File.Exists);
        if (string.IsNullOrWhiteSpace(text))
        {
            return Unavailable("hdt.json was not found. Checked: " + string.Join("; ", array));
        }

        try
        {
            return Load(text);
        }
        catch (Exception ex)
        {
            return Unavailable("Failed to load hdt.json from " + text + ": " + ex.Message, text);
        }
    }

    private static HearthstoneCardDatabase Unavailable(string error, string sourcePath = "")
    {
        return new HearthstoneCardDatabase(new Dictionary<string, HearthstoneCardMetadata>(StringComparer.OrdinalIgnoreCase), new Dictionary<int, HearthstoneCardMetadata>(), sourcePath, error);
    }

    private static IEnumerable<string> BuildDefaultPathCandidates()
    {
        List<string> list = new List<string>();
        string environmentVariable = Environment.GetEnvironmentVariable("HSAUTO_HDT_PATH");
        if (!string.IsNullOrWhiteSpace(environmentVariable))
        {
            list.Add(environmentVariable);
        }

        list.Add(Path.Combine(AppContext.BaseDirectory, "Data", "hdt-by-id.json"));
        list.Add(Path.Combine(AppContext.BaseDirectory, "Data", "hdt.json"));
        list.Add(Path.Combine(AppContext.BaseDirectory, "hdt-by-id.json"));
        list.Add(Path.Combine(AppContext.BaseDirectory, "hdt.json"));
        list.Add(Path.Combine(Directory.GetCurrentDirectory(), "HsAuto", "docs", "hdt-by-id.json"));
        list.Add(Path.Combine(Directory.GetCurrentDirectory(), "HsAuto", "docs", "hdt.json"));
        list.Add(Path.Combine(Directory.GetCurrentDirectory(), "docs", "hdt-by-id.json"));
        list.Add(Path.Combine(Directory.GetCurrentDirectory(), "docs", "hdt.json"));
        DirectoryInfo directoryInfo = new DirectoryInfo(AppContext.BaseDirectory);
        int num = 0;
        while (num < 8 && directoryInfo != null)
        {
            list.Add(Path.Combine(directoryInfo.FullName, "HsAuto", "docs", "hdt-by-id.json"));
            list.Add(Path.Combine(directoryInfo.FullName, "HsAuto", "docs", "hdt.json"));
            list.Add(Path.Combine(directoryInfo.FullName, "docs", "hdt-by-id.json"));
            list.Add(Path.Combine(directoryInfo.FullName, "docs", "hdt.json"));
            num++;
            directoryInfo = directoryInfo.Parent;
        }

        return list.Where((string path) => !string.IsNullOrWhiteSpace(path)).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeRulesText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        string input = WebUtility.HtmlDecode(text).Replace("[x]", "", StringComparison.OrdinalIgnoreCase).Replace("$", "", StringComparison.Ordinal).Replace("#", "", StringComparison.Ordinal);
        return WhitespacePattern.Replace(HtmlTagPattern.Replace(input, ""), " ").Trim();
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return "";
        }

        return value.GetString() ?? "";
    }

    private static int ReadInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return 0;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var value2))
        {
            return value2;
        }

        if (value.ValueKind != JsonValueKind.String || !int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
        {
            return 0;
        }

        return result;
    }

    private static bool ReadBool(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value))
        {
            return value.ValueKind == JsonValueKind.True;
        }

        return false;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return (
            from item in value.EnumerateArray()
            where item.ValueKind == JsonValueKind.String
            select item.GetString() ?? "" into item
                where !string.IsNullOrWhiteSpace(item)select item).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}