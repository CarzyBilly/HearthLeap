using System;
using System.Reflection;

namespace HsAuto.UnityBridge
{
    // Cached display identity only. Never requests presence data, credentials, or a
    // friends list, and never falls back to an arbitrary player/opponent.
    public static class PlayerIdentityReader
    {
        public static string ReadName(bool loggedIn, Func<object> getMyPlayer, Func<object> getFriendlyPlayer)
        {
            if (!loggedIn) return "";
            var player = SafeGet(getMyPlayer);
            var name = NameFrom(Invoke(player, "GetBattleTag"), "GetName");
            if (name.Length > 0) return name;
            return NameFrom(SafeGet(getFriendlyPlayer), "GetName");
        }
        static object SafeGet(Func<object> getter) { try { return getter(); } catch { return null; } }
        static object Invoke(object obj, string method)
        {
            try { return obj?.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null)?.Invoke(obj, null); }
            catch { return null; }
        }
        static string NameFrom(object obj, string method)
        {
            // Do not serialize/ToString the SDK object: it may contain private data.
            var name = (Invoke(obj, method) as string ?? "").Trim();
            if (name.Length > 100 || name.IndexOf('@') >= 0) return "";
            foreach (var c in name) if (char.IsControl(c)) return "";
            return name;
        }
    }
}
