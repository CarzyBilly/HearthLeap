// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace HsAuto.Core.Automation;
public sealed class HearthstoneForegroundGuard : IDisposable
{
    private readonly record struct UserFocusRequest(DateTimeOffset PendingUntil, bool Observed);
    private static readonly ConcurrentDictionary<int, DateTimeOffset> AllowedForegroundUntil = new ConcurrentDictionary<int, DateTimeOffset>();
    private static readonly ConcurrentDictionary<int, UserFocusRequest> UserFocusedProcesses = new ConcurrentDictionary<int, UserFocusRequest>();
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250.0);
    private static readonly TimeSpan RestoreRetryInterval = TimeSpan.FromMilliseconds(500.0);
    private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
    private readonly int _processId;
    private CancellationTokenSource? _linkedCancellation;
    private Task? _monitorTask;
    public event Action<string>? Log;
    public HearthstoneForegroundGuard(int processId)
    {
        _processId = processId;
    }

    public static void AllowForeground(int processId, TimeSpan duration)
    {
        if (processId > 0)
        {
            AllowedForegroundUntil[processId] = DateTimeOffset.UtcNow.Add(duration);
        }
    }

    public void Start(CancellationToken cancellationToken)
    {
        if (_monitorTask == null)
        {
            _linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(_cancellation.Token, cancellationToken);
            _monitorTask = Task.Run(() => MonitorAsync(_linkedCancellation.Token), CancellationToken.None);
        }
    }

    public static void AllowUserFocus(int processId)
    {
        UserFocusedProcesses[processId] = new UserFocusRequest(DateTimeOffset.UtcNow.AddSeconds(3.0), Observed: false);
    }

    public static void CancelUserFocus(int processId)
    {
        UserFocusedProcesses.TryRemove(processId, out var _);
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        try
        {
            _monitorTask?.Wait(TimeSpan.FromSeconds(1.0));
        }
        catch
        {
        }

        _linkedCancellation?.Dispose();
        _cancellation.Dispose();
        AllowedForegroundUntil.TryRemove(_processId, out var _);
        CancelUserFocus(_processId);
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        nint previousUserWindow = IntPtr.Zero;
        DateTimeOffset lastRestoreAttempt = DateTimeOffset.MinValue;
        while (!cancellationToken.IsCancellationRequested)
        {
            nint foregroundWindow = GetForegroundWindow();
            bool flag = GetProcessId(foregroundWindow) == _processId;
            if (UserFocusedProcesses.TryGetValue(_processId, out var value))
            {
                if (!value.Observed && value.PendingUntil < DateTimeOffset.UtcNow)
                {
                    CancelUserFocus(_processId);
                }
                else if (flag)
                {
                    UserFocusedProcesses.TryUpdate(_processId, value with { Observed = true }, value);
                }
                else if (value.Observed)
                {
                    CancelUserFocus(_processId);
                }
            }

            if (!flag)
            {
                if (foregroundWindow != IntPtr.Zero && IsWindow(foregroundWindow) && !IsIconic(foregroundWindow))
                {
                    previousUserWindow = foregroundWindow;
                }
            }
            else if (previousUserWindow != IntPtr.Zero && IsWindow(previousUserWindow) && !IsIconic(previousUserWindow) && !IsForegroundAllowed() && DateTimeOffset.UtcNow - lastRestoreAttempt >= RestoreRetryInterval)
            {
                lastRestoreAttempt = DateTimeOffset.UtcNow;
                if (TryRestoreForeground(previousUserWindow))
                {
                    Log?.Invoke("检测到炉石自行抢占前台，已恢复用户原窗口");
                }
                else
                {
                    Log?.Invoke("检测到炉石自行抢占前台，但恢复用户原窗口失败，将继续重试");
                }
            }

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
        }
    }

    private bool IsForegroundAllowed()
    {
        if (UserFocusedProcesses.ContainsKey(_processId))
        {
            return true;
        }

        if (!AllowedForegroundUntil.TryGetValue(_processId, out var value))
        {
            return false;
        }

        if (value > DateTimeOffset.UtcNow)
        {
            return true;
        }

        AllowedForegroundUntil.TryRemove(_processId, out var _);
        return false;
    }

    private static bool TryRestoreForeground(nint target)
    {
        nint foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == target)
        {
            return true;
        }

        uint currentThreadId = GetCurrentThreadId();
        uint windowThreadProcessId = GetWindowThreadProcessId(foregroundWindow, out var lpdwProcessId);
        uint windowThreadProcessId2 = GetWindowThreadProcessId(target, out lpdwProcessId);
        bool flag = windowThreadProcessId != 0 && windowThreadProcessId != currentThreadId && AttachThreadInput(currentThreadId, windowThreadProcessId, fAttach: true);
        bool flag2 = windowThreadProcessId2 != 0 && windowThreadProcessId2 != currentThreadId && windowThreadProcessId2 != windowThreadProcessId && AttachThreadInput(currentThreadId, windowThreadProcessId2, fAttach: true);
        try
        {
            BringWindowToTop(target);
            SetActiveWindow(target);
            SetForegroundWindow(target);
            return GetForegroundWindow() == target;
        }
        finally
        {
            if (flag2)
            {
                AttachThreadInput(currentThreadId, windowThreadProcessId2, fAttach: false);
            }

            if (flag)
            {
                AttachThreadInput(currentThreadId, windowThreadProcessId, fAttach: false);
            }
        }
    }

    private static int GetProcessId(nint window)
    {
        if (window == IntPtr.Zero)
        {
            return 0;
        }

        GetWindowThreadProcessId(window, out var lpdwProcessId);
        return (int)lpdwProcessId;
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hWnd);
    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hWnd);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);
    [DllImport("user32.dll")]
    private static extern nint SetActiveWindow(nint hWnd);
    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(nint hWnd);
    [DllImport("user32.dll")]
    private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
}