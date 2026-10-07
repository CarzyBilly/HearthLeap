using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace HsAuto.UnityBridge;

internal sealed class CurrencyBalanceReader
{
	private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

	private readonly Dictionary<int, DateTime> _lastRefresh = new Dictionary<int, DateTime>();

	private object? _manager;

	public bool TryRead(object? manager, int typeValue, string label, DateTime now, bool allowRefresh, out long balance, out bool available, out string reason)
	{
		balance = 0L;
		available = false;
		reason = "CurrencyManager is not available yet.";
		if (manager == null)
		{
			return false;
		}
		if (_manager != manager)
		{
			_manager = manager;
			_lastRefresh.Clear();
		}
		try
		{
			MethodInfo methodInfo = manager.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((MethodInfo methodInfo2) => methodInfo2.Name == "GetBalance" && methodInfo2.GetParameters().Length == 1 && methodInfo2.GetParameters()[0].ParameterType.IsEnum);
			if (methodInfo == null)
			{
				reason = "CurrencyManager.GetBalance(CurrencyType) was not found.";
				return false;
			}
			Type parameterType = methodInfo.GetParameters()[0].ParameterType;
			object[] parameters = new object[1] { Enum.ToObject(parameterType, typeValue) };
			MethodInfo method = manager.GetType().GetMethod("IsBalanceAvailable", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[1] { parameterType }, null);
			if (method == null || method.ReturnType != typeof(bool))
			{
				reason = "CurrencyManager.IsBalanceAvailable(CurrencyType) was not found.";
				return false;
			}
			object obj = method.Invoke(manager, parameters);
			bool flag = default;
			int num;
			if (obj is bool)
			{
				flag = (bool)obj;
				num = 1;
			}
			else
			{
				num = 0;
			}
			available = (byte)((uint)num & (flag ? 1u : 0u)) != 0;
			if (available)
			{
				object obj2 = methodInfo.Invoke(manager, parameters);
				if (obj2 == null)
				{
					throw new InvalidOperationException("CurrencyManager returned no balance.");
				}
				balance = Convert.ToInt64(obj2, CultureInfo.InvariantCulture);
				if (balance < 0)
				{
					throw new InvalidOperationException("CurrencyManager returned a negative balance.");
				}
				_lastRefresh.Remove(typeValue);
				reason = "";
				return true;
			}
			reason = label + " balance is not ready";
			if (!allowRefresh)
			{
				reason += "; refresh deferred during active gameplay.";
				return true;
			}
			object obj3 = manager.GetType().GetMethod("GetCurrencyCache", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[1] { parameterType }, null)?.Invoke(manager, parameters);
			if (obj3 == null)
			{
				reason += "; currency refresh API is unavailable.";
				return true;
			}
			obj = obj3.GetType().GetMethod("IsRefreshing", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null)?.Invoke(obj3, null);
			if (obj is bool && (bool)obj)
			{
				reason += "; the previous balance request is still pending (refreshing=true).";
				return true;
			}
			if (_lastRefresh.TryGetValue(typeValue, out var value) && now - value < TimeSpan.FromSeconds(15.0))
			{
				reason += "; refresh retry is throttled.";
				return true;
			}
			MethodInfo method2 = obj3.GetType().GetMethod("TryRefresh", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
			MethodInfo method3 = manager.GetType().GetMethod("MarkCurrencyDirty", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[1] { parameterType }, null);
			if (method2 == null || method3 == null)
			{
				reason += "; currency refresh API is unavailable.";
				return true;
			}
			if (_lastRefresh.Count >= 8)
			{
				_lastRefresh.Clear();
			}
			_lastRefresh[typeValue] = now;
			method3.Invoke(manager, parameters);
			method2.Invoke(obj3, null);
			reason += "; a refresh was requested.";
			return true;
		}
		catch (Exception ex)
		{
			available = false;
			balance = 0L;
			reason = ((ex is TargetInvocationException && ex.InnerException != null) ? ex.InnerException.Message : ex.Message);
			return false;
		}
	}
}
