// Migrated from user-provided HsAuto 0.8.21 source. See PROVENANCE.md.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace HsAuto.Core.Automation;
internal sealed class HearthstoneStartScreenController
{
    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);
    private readonly struct NativeRect
    {
        public readonly int Left;
        public readonly int Top;
        public readonly int Right;
        public readonly int Bottom;
    }

    private struct NativePoint(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    private struct Input
    {
        public uint Type;
        public MouseInput Mouse;
        public static Input CreateMouse(uint flags, int x, int y)
        {
            return new Input
            {
                Type = 0u,
                Mouse = new MouseInput
                {
                    Dx = x,
                    Dy = y,
                    DwFlags = flags,
                    DwExtraInfo = UIntPtr.Zero
                }
            };
        }
    }

    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint DwFlags;
        public uint Time;
        public nuint DwExtraInfo;
    }

    private static readonly nint HwndTopMost = new IntPtr(-1);
    private static readonly nint HwndNoTopMost = new IntPtr(-2);
    private const uint InputMouse = 0u;
    private const uint MouseEventMove = 1u;
    private const uint MouseEventLeftDown = 2u;
    private const uint MouseEventLeftUp = 4u;
    private const uint MouseEventAbsolute = 32768u;
    private const uint SwpNoSize = 1u;
    private const uint SwpNoMove = 2u;
    private const uint SwpShowWindow = 64u;
    private readonly int? _processId;
    public HearthstoneStartScreenController(int? processId)
    {
        _processId = processId;
    }

    public bool TryClick()
    {
        (double, double)[] array = new (double, double)[3]
        {
            (0.5, 0.82),
            (0.5, 0.74),
            (0.5, 0.5)
        };
        for (int i = 0; i < array.Length; i++)
        {
            var(xRatio, yRatio) = array[i];
            if (TryClickRelative(xRatio, yRatio, 350))
            {
                Thread.Sleep(250);
            }
        }

        return true;
    }

    public bool TryClickRelative(double xRatio, double yRatio, int settleMs = 500)
    {
        (int, nint, Rectangle)? tuple = FindWindow();
        if (!tuple.HasValue)
        {
            return false;
        }

        HearthstoneForegroundGuard.AllowForeground(tuple.Value.Item1, TimeSpan.FromSeconds(5.0));
        ShowWindow(tuple.Value.Item2, 9);
        SetWindowPos(tuple.Value.Item2, HwndTopMost, 0, 0, 0, 0, 67u);
        TryBringToForeground(tuple.Value.Item2);
        SetWindowPos(tuple.Value.Item2, HwndNoTopMost, 0, 0, 0, 0, 67u);
        Thread.Sleep(120);
        Point point = new Point(tuple.Value.Item3.Left + (int)Math.Round((double)tuple.Value.Item3.Width * Math.Clamp(xRatio, 0.0, 1.0)), tuple.Value.Item3.Top + (int)Math.Round((double)tuple.Value.Item3.Height * Math.Clamp(yRatio, 0.0, 1.0)));
        SetCursorPos(point.X, point.Y);
        Thread.Sleep(40);
        MoveMouse(point);
        Thread.Sleep(40);
        SendMouse(2u);
        Thread.Sleep(80);
        SendMouse(4u);
        Thread.Sleep(Math.Max(0, settleMs));
        return true;
    }

    private static bool TryBringToForeground(nint handle)
    {
        nint foregroundWindow = GetForegroundWindow();
        uint currentThreadId = GetCurrentThreadId();
        uint windowThreadProcessId = GetWindowThreadProcessId(foregroundWindow, out var lpdwProcessId);
        uint windowThreadProcessId2 = GetWindowThreadProcessId(handle, out lpdwProcessId);
        bool flag = windowThreadProcessId != 0 && windowThreadProcessId != currentThreadId && AttachThreadInput(currentThreadId, windowThreadProcessId, fAttach: true);
        bool flag2 = windowThreadProcessId2 != 0 && windowThreadProcessId2 != currentThreadId && windowThreadProcessId2 != windowThreadProcessId && AttachThreadInput(currentThreadId, windowThreadProcessId2, fAttach: true);
        try
        {
            BringWindowToTop(handle);
            SetActiveWindow(handle);
            SetForegroundWindow(handle);
            return GetForegroundWindow() == handle;
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

    private (int ProcessId, nint Handle, Rectangle ClientRectangle)? FindWindow()
    {
        foreach (Process item in
            from process in Process.GetProcessesByName("Hearthstone")orderby process.Id
            select process)
        {
            if (_processId.HasValue && item.Id != _processId.Value)
            {
                continue;
            }

            nint num = item.MainWindowHandle;
            if (num == IntPtr.Zero || !TryGetClientRectangle(num, out var rectangle))
            {
                num = FindTopLevelWindow(item.Id);
                if (num == IntPtr.Zero || !TryGetClientRectangle(num, out rectangle))
                {
                    continue;
                }
            }

            return (item.Id, num, rectangle);
        }

        return null;
    }

    private static bool TryGetClientRectangle(nint handle, out Rectangle rectangle)
    {
        rectangle = Rectangle.Empty;
        if (!GetClientRect(handle, out var lpRect))
        {
            return false;
        }

        NativePoint lpPoint = new NativePoint(lpRect.Left, lpRect.Top);
        if (!ClientToScreen(handle, ref lpPoint))
        {
            return false;
        }

        rectangle = new Rectangle(lpPoint.X, lpPoint.Y, lpRect.Right - lpRect.Left, lpRect.Bottom - lpRect.Top);
        if (rectangle.Width > 0)
        {
            return rectangle.Height > 0;
        }

        return false;
    }

    private static nint FindTopLevelWindow(int processId)
    {
        nint found = IntPtr.Zero;
        EnumWindows((nint handle, nint lParam) =>
        {
            GetWindowThreadProcessId(handle, out var lpdwProcessId);
            if (lpdwProcessId == processId && IsWindowVisible(handle) && TryGetClientRectangle(handle, out var _))
            {
                found = handle;
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static void MoveMouse(Point point)
    {
        int num = Math.Max(2, GetSystemMetrics(0));
        int num2 = Math.Max(2, GetSystemMetrics(1));
        int x = (int)Math.Round((double)point.X * 65535.0 / (double)(num - 1));
        int y = (int)Math.Round((double)point.Y * 65535.0 / (double)(num2 - 1));
        Input input = Input.CreateMouse(32769u, x, y);
        SendInput(1u, new Input[1] { input }, Marshal.SizeOf<Input>());
    }

    private static void SendMouse(uint flags)
    {
        Input input = Input.CreateMouse(flags, 0, 0);
        SendInput(1u, new Input[1] { input }, Marshal.SizeOf<Input>());
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern nint SetActiveWindow(nint hWnd);
    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(nint hWnd);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")]
    private static extern bool GetClientRect(nint hWnd, out NativeRect lpRect);
    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(nint hWnd, ref NativePoint lpPoint);
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out int lpdwProcessId);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hWnd);
}