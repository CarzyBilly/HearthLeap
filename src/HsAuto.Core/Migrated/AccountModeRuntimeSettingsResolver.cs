// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;
using System.Collections.Generic;

namespace HsAuto.Core.Configuration;
public static class AccountModeRuntimeSettingsResolver
{
    public static BotSettings Resolve(BotSettings settings, string? accountId)
    {
        ArgumentNullException.ThrowIfNull(settings, "settings");
        if (string.IsNullOrWhiteSpace(accountId) || settings.AccountModeSettings == null || !settings.AccountModeSettings.TryGetValue(accountId.Trim(), out AccountModeRuntimeSettings value) || (object)value == null)
        {
            return settings;
        }

        return value.ApplyTo(settings);
    }

    public static Dictionary<string, AccountModeRuntimeSettings> SetAccount(IReadOnlyDictionary<string, AccountModeRuntimeSettings>? existing, string accountId, AccountModeRuntimeSettings accountSettings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId, "accountId");
        ArgumentNullException.ThrowIfNull(accountSettings, "accountSettings");
        Dictionary<string, AccountModeRuntimeSettings> dictionary = Normalize(existing);
        dictionary[accountId.Trim()] = AccountModeRuntimeSettings.Normalize(accountSettings);
        return dictionary;
    }

    public static Dictionary<string, AccountModeRuntimeSettings> Normalize(IReadOnlyDictionary<string, AccountModeRuntimeSettings>? settings)
    {
        Dictionary<string, AccountModeRuntimeSettings> dictionary = new Dictionary<string, AccountModeRuntimeSettings>(StringComparer.Ordinal);
        if (settings == null)
        {
            return dictionary;
        }

        foreach (KeyValuePair<string, AccountModeRuntimeSettings> setting in settings)
        {
            if (!string.IsNullOrWhiteSpace(setting.Key) && (object)setting.Value != null)
            {
                dictionary[setting.Key.Trim()] = AccountModeRuntimeSettings.Normalize(setting.Value);
            }
        }

        return dictionary;
    }
}