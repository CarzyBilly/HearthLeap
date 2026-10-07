// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using HsAuto.Core.Strategy;

namespace HsAuto.Core.Automation;
public static class NeteaseBoxMemoryPayloadExtractor
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    };
    private const int MaxJsonSearchDistance = 524288;
    public static IReadOnlyList<NeteaseBoxRecommendationObservation> Extract(byte[] bytes, int rendererProcessId, ulong baseAddress)
    {
        List<NeteaseBoxRecommendationObservation> list = new List<NeteaseBoxRecommendationObservation>();
        ExtractUtf8(bytes, rendererProcessId, baseAddress, list);
        if (bytes.AsSpan().IndexOf(Encoding.Unicode.GetBytes("actionDom")) >= 0 || bytes.AsSpan().IndexOf(Encoding.Unicode.GetBytes("\"optionId\"")) >= 0)
        {
            ExtractUtf16(bytes, rendererProcessId, baseAddress, list);
        }

        return (
            from item in list
            group item by $"{item.RendererProcessId}|{item.Address}|{item.Payload.OptionId}|{item.CapturedAt:O}" into @group
                select @group.First()).ToArray();
    }

    public static IReadOnlyList<NeteaseBoxWinRateObservation> ExtractWinRates(byte[] bytes, int rendererProcessId, ulong baseAddress)
    {
        List<NeteaseBoxWinRateObservation> list = new List<NeteaseBoxWinRateObservation>();
        ExtractUtf8WinRateCallbacks(bytes, rendererProcessId, baseAddress, list);
        if (bytes.AsSpan().IndexOf(Encoding.Unicode.GetBytes("onUpdateWinrateData")) >= 0)
        {
            ExtractUtf16WinRateCallbacks(bytes, rendererProcessId, baseAddress, list);
        }

        return (
            from @group in list.GroupBy((NeteaseBoxWinRateObservation item) =>
            {
                IFormatProvider invariantCulture = CultureInfo.InvariantCulture;
                DefaultInterpolatedStringHandler handler = new DefaultInterpolatedStringHandler(3, 4, invariantCulture);
                handler.AppendFormatted(item.RendererProcessId);
                handler.AppendLiteral("|");
                handler.AppendFormatted(item.Address, "X");
                handler.AppendLiteral("|");
                handler.AppendFormatted(item.Payload.MyWinRate, "R");
                handler.AppendLiteral("|");
                handler.AppendFormatted(item.Payload.Turns.LastOrDefault()?.TurnId ?? 0);
                return string.Create(invariantCulture, ref handler);
            })select @group.First()).ToArray();
    }

    public static bool TryParseWinRate(string json, out NeteaseBoxWinRatePayload payload)
    {
        payload = new NeteaseBoxWinRatePayload();
        try
        {
            NeteaseBoxWinRatePayload neteaseBoxWinRatePayload = JsonSerializer.Deserialize<NeteaseBoxWinRatePayload>(json, JsonOptions);
            bool flag = (object)neteaseBoxWinRatePayload == null || !double.IsFinite(neteaseBoxWinRatePayload.MyWinRate);
            if (!flag)
            {
                double myWinRate = neteaseBoxWinRatePayload.MyWinRate;
                bool flag2 = ((myWinRate < 0.0 || myWinRate > 100.0) ? true : false);
                flag = flag2;
            }

            if (flag)
            {
                return false;
            }

            payload = neteaseBoxWinRatePayload;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static bool TryParseRecommendation(string json, out NeteaseBoxRecommendation recommendation)
    {
        recommendation = new NeteaseBoxRecommendation();
        try
        {
            NeteaseBoxRecommendation neteaseBoxRecommendation = JsonSerializer.Deserialize<NeteaseBoxRecommendation>(json, JsonOptions);
            if ((object)neteaseBoxRecommendation == null || neteaseBoxRecommendation.Data == null)
            {
                return false;
            }

            recommendation = neteaseBoxRecommendation;
            return neteaseBoxRecommendation.OptionId > 0 || neteaseBoxRecommendation.ChoiceId > 0 || neteaseBoxRecommendation.Data.Count > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void ExtractUtf8WinRateCallbacks(byte[] bytes, int rendererProcessId, ulong baseAddress, ICollection<NeteaseBoxWinRateObservation> output)
    {
        byte[] bytes2 = Encoding.ASCII.GetBytes("onUpdateWinrateData");
        int start = 0;
        while ((start = IndexOf(bytes, bytes2, start)) >= 0)
        {
            int num = SkipAsciiWhitespace(bytes, start + bytes2.Length);
            if (num < bytes.Length && bytes[num] == 40)
            {
                num = SkipAsciiWhitespace(bytes, num + 1);
                if (TryReadCallbackJson(bytes, num, out string json) && TryParseWinRate(json, out NeteaseBoxWinRatePayload payload))
                {
                    output.Add(new NeteaseBoxWinRateObservation(payload, DateTimeOffset.MinValue, rendererProcessId, baseAddress + (ulong)start));
                }
            }

            start += bytes2.Length;
        }
    }

    private static void ExtractUtf16WinRateCallbacks(byte[] bytes, int rendererProcessId, ulong baseAddress, ICollection<NeteaseBoxWinRateObservation> output)
    {
        string text = Encoding.Unicode.GetString(bytes);
        int startIndex = 0;
        while ((startIndex = text.IndexOf("onUpdateWinrateData", startIndex, StringComparison.Ordinal)) >= 0)
        {
            int num = SkipWhitespace(text, startIndex + "onUpdateWinrateData".Length);
            if (num < text.Length && text[num] == '(')
            {
                num = SkipWhitespace(text, num + 1);
                if (TryReadCallbackJson(text, num, out string json) && TryParseWinRate(json, out NeteaseBoxWinRatePayload payload))
                {
                    output.Add(new NeteaseBoxWinRateObservation(payload, DateTimeOffset.MinValue, rendererProcessId, baseAddress + (ulong)(startIndex * 2)));
                }
            }

            startIndex += "onUpdateWinrateData".Length;
        }
    }

    private static void ExtractUtf8(byte[] bytes, int rendererProcessId, ulong baseAddress, ICollection<NeteaseBoxRecommendationObservation> output)
    {
        ExtractUtf8CallbackInvocations(bytes, rendererProcessId, baseAddress, output);
        byte[] bytes2 = Encoding.ASCII.GetBytes("actionDom");
        byte[] bytes3 = Encoding.ASCII.GetBytes("{\"type\":5,\"timestamp\"");
        byte[] bytes4 = Encoding.ASCII.GetBytes("{\"timestamp\"");
        int start = 0;
        while ((start = IndexOf(bytes, bytes2, start)) >= 0)
        {
            int num = Math.Max(LastIndexOf(bytes, bytes3, start, 524288), LastIndexOf(bytes, bytes4, start, 524288));
            if (num >= 0 && TryReadBalancedJson(bytes, num, out byte[] json) && TryReadObservation(Encoding.UTF8.GetString(json), rendererProcessId, baseAddress + (ulong)num, out NeteaseBoxRecommendationObservation observation))
            {
                output.Add(observation);
            }

            start += bytes2.Length;
        }

        byte[] bytes5 = Encoding.ASCII.GetBytes("\"choiceId\"");
        start = 0;
        while ((start = IndexOf(bytes, bytes5, start)) >= 0)
        {
            if (TryReadStandaloneJson(bytes, start, out int start2, out byte[] json2) && TryParseRecommendation(Encoding.UTF8.GetString(json2), out NeteaseBoxRecommendation recommendation))
            {
                output.Add(new NeteaseBoxRecommendationObservation(recommendation, DateTimeOffset.MinValue, rendererProcessId, baseAddress + (ulong)start2));
            }

            start += bytes5.Length;
        }
    }

    private static void ExtractUtf16(byte[] bytes, int rendererProcessId, ulong baseAddress, ICollection<NeteaseBoxRecommendationObservation> output)
    {
        string text = Encoding.Unicode.GetString(bytes);
        ExtractUtf16CallbackInvocations(text, rendererProcessId, baseAddress, output);
        int startIndex = 0;
        while ((startIndex = text.IndexOf("actionDom", startIndex, StringComparison.Ordinal)) >= 0)
        {
            int num = Math.Max(0, startIndex - 262144);
            int val = text.LastIndexOf("{\"type\":5,\"timestamp\"", startIndex, startIndex - num + 1, StringComparison.Ordinal);
            int val2 = text.LastIndexOf("{\"timestamp\"", startIndex, startIndex - num + 1, StringComparison.Ordinal);
            int num2 = Math.Max(val, val2);
            if (num2 >= 0 && TryReadBalancedJson(text, num2, out string json) && TryReadObservation(json, rendererProcessId, baseAddress + (ulong)(num2 * 2), out NeteaseBoxRecommendationObservation observation))
            {
                output.Add(observation);
            }

            startIndex += "actionDom".Length;
        }

        startIndex = 0;
        while ((startIndex = text.IndexOf("\"choiceId\"", startIndex, StringComparison.Ordinal)) >= 0)
        {
            if (TryReadStandaloneJson(text, startIndex, out int start, out string json2) && TryParseRecommendation(json2, out NeteaseBoxRecommendation recommendation))
            {
                output.Add(new NeteaseBoxRecommendationObservation(recommendation, DateTimeOffset.MinValue, rendererProcessId, baseAddress + (ulong)(start * 2)));
            }

            startIndex += "\"choiceId\"".Length;
        }
    }

    private static bool TryReadStandaloneJson(byte[] bytes, int markerOffset, out int start, out byte[] json)
    {
        start = -1;
        json = Array.Empty<byte>();
        int num = Math.Max(0, markerOffset - 4096);
        for (int num2 = markerOffset - 1; num2 >= num; num2--)
        {
            if (bytes[num2] == 123 && TryReadBalancedJson(bytes, num2, out byte[] json2) && num2 + json2.Length > markerOffset)
            {
                start = num2;
                json = json2;
                return true;
            }
        }

        return false;
    }

    private static bool TryReadStandaloneJson(string text, int markerOffset, out int start, out string json)
    {
        start = -1;
        json = "";
        int num = Math.Max(0, markerOffset - 2048);
        for (int num2 = markerOffset - 1; num2 >= num; num2--)
        {
            if (text[num2] == '{' && TryReadBalancedJson(text, num2, out string json2) && num2 + json2.Length > markerOffset)
            {
                start = num2;
                json = json2;
                return true;
            }
        }

        return false;
    }

    private static void ExtractUtf8CallbackInvocations(byte[] bytes, int rendererProcessId, ulong baseAddress, ICollection<NeteaseBoxRecommendationObservation> output)
    {
        byte[] bytes2 = Encoding.ASCII.GetBytes("onUpdateLadderActionRecommend");
        int start = 0;
        while ((start = IndexOf(bytes, bytes2, start)) >= 0)
        {
            int num = SkipAsciiWhitespace(bytes, start + bytes2.Length);
            if (num >= bytes.Length || bytes[num] != 40)
            {
                start += bytes2.Length;
                continue;
            }

            num = SkipAsciiWhitespace(bytes, num + 1);
            if (!TryReadCallbackJson(bytes, num, out string json) || !TryParseRecommendation(json, out NeteaseBoxRecommendation recommendation))
            {
                start += bytes2.Length;
                continue;
            }

            output.Add(new NeteaseBoxRecommendationObservation(recommendation, ReadNearbyTimestamp(bytes, start), rendererProcessId, baseAddress + (ulong)start));
            start += bytes2.Length;
        }
    }

    private static void ExtractUtf16CallbackInvocations(string text, int rendererProcessId, ulong baseAddress, ICollection<NeteaseBoxRecommendationObservation> output)
    {
        int startIndex = 0;
        while ((startIndex = text.IndexOf("onUpdateLadderActionRecommend", startIndex, StringComparison.Ordinal)) >= 0)
        {
            int num = SkipWhitespace(text, startIndex + "onUpdateLadderActionRecommend".Length);
            if (num >= text.Length || text[num] != '(')
            {
                startIndex += "onUpdateLadderActionRecommend".Length;
                continue;
            }

            num = SkipWhitespace(text, num + 1);
            if (!TryReadCallbackJson(text, num, out string json) || !TryParseRecommendation(json, out NeteaseBoxRecommendation recommendation))
            {
                startIndex += "onUpdateLadderActionRecommend".Length;
                continue;
            }

            output.Add(new NeteaseBoxRecommendationObservation(recommendation, ReadNearbyTimestamp(text, startIndex), rendererProcessId, baseAddress + (ulong)(startIndex * 2)));
            startIndex += "onUpdateLadderActionRecommend".Length;
        }
    }

    private static bool TryReadCallbackJson(byte[] bytes, int start, out string json)
    {
        json = "";
        if (start >= bytes.Length)
        {
            return false;
        }

        byte b = bytes[start];
        if ((b == 34 || b == 39 || b == 96) ? true : false)
        {
            if (!TryReadDelimited(bytes, start, b, out byte[] payload))
            {
                return false;
            }

            json = DecodeCallbackPayload(Encoding.UTF8.GetString(payload), (char)b);
            return true;
        }

        if (bytes[start] != 123 || !TryReadBalancedJson(bytes, start, out byte[] json2))
        {
            return false;
        }

        json = Encoding.UTF8.GetString(json2);
        return true;
    }

    private static bool TryReadCallbackJson(string text, int start, out string json)
    {
        json = "";
        if (start >= text.Length)
        {
            return false;
        }

        char c = text[start];
        if ((c == '"' || c == '\'' || c == '`') ? true : false)
        {
            if (!TryReadDelimited(text, start, c, out string payload))
            {
                return false;
            }

            json = DecodeCallbackPayload(payload, c);
            return true;
        }

        if (text[start] == '{')
        {
            return TryReadBalancedJson(text, start, out json);
        }

        return false;
    }

    private static string DecodeCallbackPayload(string payload, char delimiter)
    {
        if (delimiter != '"')
        {
            return payload;
        }

        try
        {
            return JsonSerializer.Deserialize<string>("\"" + payload + "\"") ?? payload;
        }
        catch (JsonException)
        {
            return payload;
        }
    }

    private static bool TryReadDelimited(byte[] bytes, int start, byte delimiter, out byte[] payload)
    {
        payload = Array.Empty<byte>();
        bool flag = false;
        int num = Math.Min(bytes.Length, start + 524288);
        for (int i = start + 1; i < num; i++)
        {
            byte b = bytes[i];
            if (flag)
            {
                flag = false;
            }
            else if (b == 92)
            {
                flag = true;
            }
            else if (b == delimiter)
            {
                payload = bytes[(start + 1)..i];
                return true;
            }
        }

        return false;
    }

    private static bool TryReadDelimited(string text, int start, char delimiter, out string payload)
    {
        payload = "";
        bool flag = false;
        int num = Math.Min(text.Length, start + 262144);
        for (int i = start + 1; i < num; i++)
        {
            char c = text[i];
            if (flag)
            {
                flag = false;
            }
            else if (c == '\\')
            {
                flag = true;
            }
            else if (c == delimiter)
            {
                int num2 = start + 1;
                payload = text.Substring(num2, i - num2);
                return true;
            }
        }

        return false;
    }

    private static int SkipAsciiWhitespace(byte[] bytes, int offset)
    {
        while (true)
        {
            bool flag = offset < bytes.Length;
            if (flag)
            {
                byte b = bytes[offset];
                bool flag2 = (((uint)(b - 9) <= 1u || b == 13 || b == 32) ? true : false);
                flag = flag2;
            }

            if (!flag)
            {
                break;
            }

            offset++;
        }

        return offset;
    }

    private static int SkipWhitespace(string text, int offset)
    {
        while (offset < text.Length && char.IsWhiteSpace(text[offset]))
        {
            offset++;
        }

        return offset;
    }

    private static DateTimeOffset ReadNearbyTimestamp(byte[] bytes, int offset)
    {
        byte[] bytes2 = Encoding.ASCII.GetBytes("\"timestamp\":");
        int num = IndexOf(bytes, bytes2, offset);
        if (num >= 0 && num - offset <= 32768)
        {
            return ReadAsciiTimestamp(bytes, num + bytes2.Length);
        }

        int num2 = LastIndexOf(bytes, bytes2, offset, 8192);
        if (num2 < 0)
        {
            return DateTimeOffset.MinValue;
        }

        return ReadAsciiTimestamp(bytes, num2 + bytes2.Length);
    }

    private static DateTimeOffset ReadNearbyTimestamp(string text, int offset)
    {
        int num = text.IndexOf("\"timestamp\":", offset, StringComparison.Ordinal);
        if (num >= 0 && num - offset <= 16384)
        {
            return ReadTextTimestamp(text, num + "\"timestamp\":".Length);
        }

        int num2 = Math.Max(0, offset - 4096);
        int num3 = text.LastIndexOf("\"timestamp\":", offset, offset - num2 + 1, StringComparison.Ordinal);
        if (num3 < 0)
        {
            return DateTimeOffset.MinValue;
        }

        return ReadTextTimestamp(text, num3 + "\"timestamp\":".Length);
    }

    private static DateTimeOffset ReadAsciiTimestamp(byte[] bytes, int offset)
    {
        int i;
        for (i = offset; i < bytes.Length; i++)
        {
            byte b = bytes[i];
            if ((b < 48 || b > 57) && bytes[i] != 46)
            {
                break;
            }
        }

        return TryConvertUnixTimestamp(Encoding.ASCII.GetString(bytes, offset, i - offset));
    }

    private static DateTimeOffset ReadTextTimestamp(string text, int offset)
    {
        int i;
        for (i = offset; i < text.Length && (char.IsDigit(text[i]) || text[i] == '.'); i++)
        {
        }

        return TryConvertUnixTimestamp(text.Substring(offset, i - offset));
    }

    private static DateTimeOffset TryConvertUnixTimestamp(string value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
        {
            return DateTimeOffset.MinValue;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(checked((long)((result >= 100000000000.0) ? result : (result * 1000.0))));
        }
        catch (Exception ex)when ((ex is ArgumentOutOfRangeException || ex is OverflowException) ? true : false)
        {
            return DateTimeOffset.MinValue;
        }
    }

    private static bool TryReadObservation(string json, int rendererProcessId, ulong address, out NeteaseBoxRecommendationObservation observation)
    {
        observation = new NeteaseBoxRecommendationObservation(new NeteaseBoxRecommendation(), DateTimeOffset.MinValue, rendererProcessId, address);
        try
        {
            using JsonDocument jsonDocument = JsonDocument.Parse(json);
            JsonElement rootElement = jsonDocument.RootElement;
            DateTimeOffset capturedAt = ReadTimestamp(rootElement);
            if (!TryFindRecommendation(rootElement, out NeteaseBoxRecommendation payload))
            {
                return false;
            }

            observation = new NeteaseBoxRecommendationObservation(payload, capturedAt, rendererProcessId, address);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryFindRecommendation(JsonElement element, out NeteaseBoxRecommendation payload)
    {
        payload = new NeteaseBoxRecommendation();
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("optionId", out var value) && element.TryGetProperty("data", out value) && TryParseRecommendation(element.GetRawText(), out payload))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("data", out var value2) && value2.ValueKind == JsonValueKind.Object && value2.TryGetProperty("arguments", out var value3) && value3.ValueKind == JsonValueKind.Array)
        {
            bool flag = false;
            foreach (JsonElement item in value3.EnumerateArray())
            {
                if (!flag && item.ValueKind == JsonValueKind.String && string.Equals(item.GetString(), "actionDom", StringComparison.Ordinal))
                {
                    flag = true;
                }
                else if (flag)
                {
                    if (item.ValueKind == JsonValueKind.Object && TryFindRecommendation(item, out payload))
                    {
                        return true;
                    }

                    if (item.ValueKind == JsonValueKind.String && TryParseRecommendation(item.GetString() ?? "", out payload))
                    {
                        return true;
                    }
                }
            }
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty item2 in element.EnumerateObject())
            {
                JsonValueKind valueKind = item2.Value.ValueKind;
                bool flag2 = valueKind - 1 <= JsonValueKind.Object;
                if (flag2 && TryFindRecommendation(item2.Value, out payload))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item3 in element.EnumerateArray())
            {
                if (TryFindRecommendation(item3, out payload))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static DateTimeOffset ReadTimestamp(JsonElement root)
    {
        if (!root.TryGetProperty("timestamp", out var value))
        {
            return DateTimeOffset.MinValue;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var value2))
        {
            try
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(checked((long)((value2 >= 100000000000.0) ? value2 : (value2 * 1000.0))));
            }
            catch (Exception ex)when ((ex is ArgumentOutOfRangeException || ex is OverflowException) ? true : false)
            {
                return DateTimeOffset.MinValue;
            }
        }

        if (value.ValueKind != JsonValueKind.String || !DateTimeOffset.TryParse(value.GetString(), out var result))
        {
            return DateTimeOffset.MinValue;
        }

        return result;
    }

    private static bool TryReadBalancedJson(byte[] bytes, int start, out byte[] json)
    {
        json = Array.Empty<byte>();
        int num = 0;
        bool flag = false;
        bool flag2 = false;
        int num2 = Math.Min(bytes.Length, start + 524288);
        for (int i = start; i < num2; i++)
        {
            byte b = bytes[i];
            if (flag)
            {
                if (flag2)
                {
                    flag2 = false;
                    continue;
                }

                switch (b)
                {
                    case 92:
                        flag2 = true;
                        break;
                    case 34:
                        flag = false;
                        break;
                }

                continue;
            }

            bool flag3;
            switch (b)
            {
                case 34:
                    flag = true;
                    continue;
                case 91:
                case 123:
                    flag3 = true;
                    break;
                default:
                    flag3 = false;
                    break;
            }

            if (flag3)
            {
                num++;
            }
            else if ((b == 93 || b == 125) ? true : false)
            {
                num--;
                if (num == 0)
                {
                    json = bytes[start..(i + 1)];
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryReadBalancedJson(string text, int start, out string json)
    {
        json = "";
        int num = 0;
        bool flag = false;
        bool flag2 = false;
        int num2 = Math.Min(text.Length, start + 262144);
        for (int i = start; i < num2; i++)
        {
            char c = text[i];
            if (flag)
            {
                if (flag2)
                {
                    flag2 = false;
                    continue;
                }

                switch (c)
                {
                    case '\\':
                        flag2 = true;
                        break;
                    case '"':
                        flag = false;
                        break;
                }

                continue;
            }

            bool flag3;
            switch (c)
            {
                case '"':
                    flag = true;
                    continue;
                case '[':
                case '{':
                    flag3 = true;
                    break;
                default:
                    flag3 = false;
                    break;
            }

            if (flag3)
            {
                num++;
            }
            else if ((c == ']' || c == '}') ? true : false)
            {
                num--;
                if (num == 0)
                {
                    json = text.Substring(start, i + 1 - start);
                    return true;
                }
            }
        }

        return false;
    }

    private static int IndexOf(byte[] source, byte[] pattern, int start)
    {
        int num = source.AsSpan(start).IndexOf(pattern);
        if (num >= 0)
        {
            return start + num;
        }

        return -1;
    }

    private static int LastIndexOf(byte[] source, byte[] pattern, int before, int maxDistance)
    {
        int num = Math.Max(0, before - maxDistance);
        for (int num2 = before; num2 >= num; num2--)
        {
            if (num2 + pattern.Length <= source.Length && source.AsSpan(num2, pattern.Length).SequenceEqual(pattern))
            {
                return num2;
            }
        }

        return -1;
    }
}