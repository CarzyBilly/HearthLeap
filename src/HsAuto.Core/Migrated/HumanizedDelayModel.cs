// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;
using System.Security.Cryptography;

namespace HsAuto.Core.Automation;
internal sealed class HumanizedDelayModel
{
    private readonly Func<double> _nextUnit;
    private readonly double _sessionSpeedFactor;
    private string _turnKey = "";
    private double _turnSpeedFactor = 1.0;
    internal double SessionSpeedFactor => _sessionSpeedFactor;

    public HumanizedDelayModel(Func<double>? nextUnit = null)
    {
        _nextUnit = nextUnit ?? new Func<double>(NextCryptographicUnit);
        _sessionSpeedFactor = Lerp(0.86, 1.18, NextUnit());
    }

    public int NextDelayMs(HumanizedActionCategory category, string turnKey, int minimumDelayMs, int maximumDelayMs, bool allowHesitation = true)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minimumDelayMs, "minimumDelayMs");
        if (maximumDelayMs < minimumDelayMs)
        {
            throw new ArgumentOutOfRangeException("maximumDelayMs");
        }

        EnsureTurn(turnKey);
        if (maximumDelayMs == minimumDelayMs)
        {
            return minimumDelayMs;
        }

        double num = (Math.Log(Math.Max(1, minimumDelayMs)) + Math.Log(maximumDelayMs)) / 2.0;
        double num2 = NextStandardNormal();
        double num3 = Math.Exp(num + 0.43 * num2);
        num3 *= _sessionSpeedFactor * _turnSpeedFactor;
        double num4;
        switch (category)
        {
            case HumanizedActionCategory.Choice:
                num4 = 0.1;
                break;
            case HumanizedActionCategory.MulliganConfirmation:
                num4 = 0.08;
                break;
            case HumanizedActionCategory.MatchStart:
            case HumanizedActionCategory.PostGameQueue:
                num4 = 0.07;
                break;
            case HumanizedActionCategory.FollowUpAction:
            case HumanizedActionCategory.Accelerated:
                num4 = 0.0;
                break;
            default:
                num4 = 0.04;
                break;
        }

        double num5 = num4;
        if (allowHesitation && NextUnit() < num5)
        {
            num3 += Lerp(450.0, 1650.0, NextUnit());
        }

        return Math.Clamp((int)Math.Round(num3), minimumDelayMs, maximumDelayMs);
    }

    private void EnsureTurn(string turnKey)
    {
        if (!string.Equals(_turnKey, turnKey, StringComparison.Ordinal))
        {
            _turnKey = turnKey;
            double num = Lerp(0.86, 1.16, NextUnit());
            _turnSpeedFactor = Math.Clamp(_turnSpeedFactor * 0.68 + num * 0.32, 0.86, 1.16);
        }
    }

    private double NextStandardNormal()
    {
        double d = Math.Max(double.Epsilon, NextUnit());
        double num = NextUnit();
        return Math.Sqrt(-2.0 * Math.Log(d)) * Math.Cos(Math.PI * 2.0 * num);
    }

    private double NextUnit()
    {
        return Math.Clamp(_nextUnit(), 0.0, 0.9999999999999999);
    }

    private static double NextCryptographicUnit()
    {
        return (double)RandomNumberGenerator.GetInt32(0, int.MaxValue) / 2147483647.0;
    }

    private static double Lerp(double minimum, double maximum, double amount)
    {
        return minimum + (maximum - minimum) * amount;
    }
}