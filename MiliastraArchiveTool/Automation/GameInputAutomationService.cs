using MiliastraArchiveTool.Infrastructure;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MiliastraArchiveTool.Automation;

public enum GameVirtualKey : byte
{
    Enter = 0x0D,
    Escape = 0x1B,
    Control = 0x11,
    A = 0x41,
    V = 0x56
}

public sealed partial class GameInputAutomationService : IGameInputAutomationService
{
    private const uint LeftButtonDown = 0x0002;
    private const uint LeftButtonUp = 0x0004;
    private const uint MouseWheel = 0x0800;
    private const uint KeyUp = 0x0002;
    private const uint KeyEventScancode = 0x0008;
    private const uint KeyEventUnicode = 0x0004;
    private const uint SendInputMouse = 0;
    private const uint SendInputKeyboard = 1;
    private const uint MouseMove = 0x0001;
    private const uint MouseVirtualDesk = 0x4000;
    private const uint MouseAbsolute = 0x8000;
    private const int LeftButtonDownMessage = 0x0201;
    private const int LeftButtonUpMessage = 0x0202;
    private const int KeyDownMessage = 0x0100;
    private const int KeyUpMessage = 0x0101;
    private const nuint MouseLeftButtonParameter = 0x0001;
    private const int SystemMetricVirtualScreenLeft = 76;
    private const int SystemMetricVirtualScreenTop = 77;
    private const int SystemMetricVirtualScreenWidth = 78;
    private const int SystemMetricVirtualScreenHeight = 79;

    private readonly GameWindowActivationService _activation;

    public GameInputAutomationService(GameWindowActivationService? activation = null)
    {
        _activation = activation ?? new GameWindowActivationService();
    }

    public async Task ClickAsync(GameWindowInfo window, double normalizedX, double normalizedY, CancellationToken cancellationToken)
    {
        using var performanceTiming = WorkflowPerformanceTrace.Measure("Input.Click", new { normalizedX, normalizedY });
        ValidateNormalizedCoordinate(normalizedX, nameof(normalizedX));
        ValidateNormalizedCoordinate(normalizedY, nameof(normalizedY));

        _activation.RestoreAndActivate(window);
        var (screenX, screenY) = GetFreshClientPoint(window, normalizedX, normalizedY);
        Debug.WriteLine($"Game click normalized=({normalizedX:F3},{normalizedY:F3}), screen=({screenX},{screenY}), window=0x{window.Handle.ToInt64():X}");

        if (TryMoveCursor(window, screenX, screenY))
        {
            await Task.Delay(100, cancellationToken);
            mouse_event(LeftButtonDown, 0, 0, 0, UIntPtr.Zero);
            await Task.Delay(100, cancellationToken);
            mouse_event(LeftButtonUp, 0, 0, 0, UIntPtr.Zero);
        }
        else
        {
            var clientX = Math.Clamp((int)Math.Round(window.ClientWidth * normalizedX), 0, Math.Max(0, window.ClientWidth - 1));
            var clientY = Math.Clamp((int)Math.Round(window.ClientHeight * normalizedY), 0, Math.Max(0, window.ClientHeight - 1));
            PostClientClickOrThrow(window, clientX, clientY);
        }

        await Task.Delay(150, cancellationToken);
    }

    public async Task MoveAsync(GameWindowInfo window, double normalizedX, double normalizedY, CancellationToken cancellationToken)
    {
        using var performanceTiming = WorkflowPerformanceTrace.Measure("Input.Move", new { normalizedX, normalizedY });
        ValidateNormalizedCoordinate(normalizedX, nameof(normalizedX));
        ValidateNormalizedCoordinate(normalizedY, nameof(normalizedY));

        _activation.RestoreAndActivate(window);
        var (screenX, screenY) = GetFreshClientPoint(window, normalizedX, normalizedY);
        Debug.WriteLine($"Game move normalized=({normalizedX:F3},{normalizedY:F3}), screen=({screenX},{screenY}), window=0x{window.Handle.ToInt64():X}");
        if (!TryMoveCursor(window, screenX, screenY))
            Debug.WriteLine($"Physical cursor move failed. Target=({screenX},{screenY}), window=0x{window.Handle.ToInt64():X}");
        await Task.Delay(150, cancellationToken);
    }

    public async Task PressKeyAsync(
        GameWindowInfo window,
        GameVirtualKey key,
        CancellationToken cancellationToken,
        bool allowPostMessageFallback = true,
        bool forcePostMessageFallback = false)
    {
        using var performanceTiming = WorkflowPerformanceTrace.Measure("Input.PressKey", new { Key = key.ToString() });
        _activation.RestoreAndActivate(window);
        var before = _activation.GetForegroundWindowInfo(window);
        if (!before.IsTargetHandleForeground && !before.IsTargetProcessForeground)
            throw new InvalidOperationException($"Game window is not foreground before key input. key={key}, {before.ToDiagnosticString()}");

        Debug.WriteLine($"Game key input: {key}, before=({before.ToDiagnosticString()})");
        var physicalSent = SendKeyboardInput(key);
        if (!physicalSent)
        {
            Debug.WriteLine($"SendInput keyboard failed for {key}; falling back to keybd_event.");
            var scanCode = (byte)Math.Min(byte.MaxValue, GetScanCode(key));
            keybd_event((byte)key, scanCode, 0, UIntPtr.Zero);
            try
            {
                await Task.Delay(90, cancellationToken);
            }
            finally
            {
                keybd_event((byte)key, scanCode, KeyUp, UIntPtr.Zero);
            }
        }

        await Task.Delay(180, cancellationToken);
        var after = _activation.GetForegroundWindowInfo(window);
        if (forcePostMessageFallback)
        {
            if (!allowPostMessageFallback)
                throw new InvalidOperationException($"Forced PostMessage fallback was requested but fallback is disabled. key={key}, after=({after.ToDiagnosticString()})");

            Debug.WriteLine($"Forcing PostMessage key fallback for {key}. after=({after.ToDiagnosticString()})");
            PostKeyOrThrow(window, key, before, after);
            await Task.Delay(180, cancellationToken);
            var afterForcedFallback = _activation.GetForegroundWindowInfo(window);
            if (!afterForcedFallback.IsTargetHandleForeground && !afterForcedFallback.IsTargetProcessForeground)
                throw new InvalidOperationException(
                    $"Game window lost foreground after forced PostMessage key fallback. key={key}, " +
                    $"before=({before.ToDiagnosticString()}), after=({after.ToDiagnosticString()}), afterForcedFallback=({afterForcedFallback.ToDiagnosticString()})");
            return;
        }

        if (after.IsTargetHandleForeground || after.IsTargetProcessForeground)
        {
            Debug.WriteLine($"Game key input complete: {key}, after=({after.ToDiagnosticString()})");
            return;
        }

        if (allowPostMessageFallback)
        {
            Debug.WriteLine($"Foreground moved away after {key}; trying PostMessage fallback. after=({after.ToDiagnosticString()})");
            PostKeyOrThrow(window, key, before, after);
            await Task.Delay(180, cancellationToken);
            return;
        }

        throw new InvalidOperationException(
            $"Game window lost foreground after key input. key={key}, before=({before.ToDiagnosticString()}), after=({after.ToDiagnosticString()})");
    }

    public async Task ScrollAsync(GameWindowInfo window, double normalizedX, double normalizedY, int wheelDelta, CancellationToken cancellationToken)
    {
        using var performanceTiming = WorkflowPerformanceTrace.Measure("Input.Scroll", new { normalizedX, normalizedY, wheelDelta });
        Debug.WriteLine($"Game wheel normalized=({normalizedX:F3},{normalizedY:F3}), delta={wheelDelta}, window=0x{window.Handle.ToInt64():X}");
        await MoveAsync(window, normalizedX, normalizedY, cancellationToken);
        await SendWheelBurstAsync(wheelDelta,
            delta => mouse_event(MouseWheel, 0, 0, unchecked((uint)delta), UIntPtr.Zero),
            (delay, token) => Task.Delay(delay, token), cancellationToken);
        await Task.Delay(200, cancellationToken);
    }

    // Delta is expressed in Win32 units (120 per notch), not a count of wheel notches.
    // Move once, send the segment quickly, then let the caller capture the settled page.
    internal static async Task SendWheelBurstAsync(
        int wheelDelta, Action<int> send, Func<TimeSpan, CancellationToken, Task> delay,
        CancellationToken cancellationToken)
    {
        long remaining = wheelDelta;
        while (remaining != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var step = (int)Math.Clamp(remaining, -120L, 120L);
            send(step);
            remaining -= step;
            if (remaining != 0)
                await delay(TimeSpan.FromMilliseconds(40), cancellationToken);
        }
    }

    public async Task ReplaceTextAsync(GameWindowInfo window, string text, CancellationToken cancellationToken)
    {
        using var performanceTiming = WorkflowPerformanceTrace.Measure("Input.ReplaceText", new { Characters = text.Length });
        ArgumentNullException.ThrowIfNull(text);

        _activation.RestoreAndActivate(window);
        var before = _activation.GetForegroundWindowInfo(window);
        if (!before.IsTargetHandleForeground && !before.IsTargetProcessForeground)
            throw new InvalidOperationException($"Game window is not foreground before text input. {before.ToDiagnosticString()}");

        SendCtrlAOrThrow();
        await Task.Delay(120, cancellationToken);
        SendUnicodeTextOrThrow(text);
        await Task.Delay(180, cancellationToken);

        var after = _activation.GetForegroundWindowInfo(window);
        if (!after.IsTargetHandleForeground && !after.IsTargetProcessForeground)
            throw new InvalidOperationException(
                $"Game window lost foreground after text input. before=({before.ToDiagnosticString()}), after=({after.ToDiagnosticString()})");
    }

    private static void ValidateNormalizedCoordinate(double value, string parameterName)
    {
        if (double.IsNaN(value) || value < 0 || value > 1)
            throw new ArgumentOutOfRangeException(parameterName, "Game coordinates must be between 0 and 1.");
    }

    private static (int X, int Y) GetFreshClientPoint(GameWindowInfo window, double normalizedX, double normalizedY)
    {
        if (!IsWindow(window.Handle))
            throw new InvalidOperationException("The game window has closed.");
        if (!GetClientRect(window.Handle, out var clientRect))
            throw new InvalidOperationException("Unable to read the game client area size.");

        var width = clientRect.Right - clientRect.Left;
        var height = clientRect.Bottom - clientRect.Top;
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException("The game client area size is invalid.");

        var origin = new NativePoint();
        if (!ClientToScreen(window.Handle, ref origin))
            throw new InvalidOperationException("Unable to read the game client area screen position.");

        return (
            origin.X + (int)Math.Round(width * normalizedX),
            origin.Y + (int)Math.Round(height * normalizedY));
    }

    private static bool TryMoveCursor(GameWindowInfo window, int screenX, int screenY)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (SetCursorPos(screenX, screenY))
                return true;
            if (IsCursorNear(screenX, screenY))
                return true;
            Thread.Sleep(50);
        }

        if (MoveCursorBySendInput(screenX, screenY))
            return true;

        var error = new Win32Exception(Marshal.GetLastWin32Error());
        Debug.WriteLine($"Unable to move cursor. Target=({screenX},{screenY}), window={window.Title}, handle=0x{window.Handle.ToInt64():X}, error={error.Message}");
        return false;
    }

    private static void PostClientClickOrThrow(GameWindowInfo window, int clientX, int clientY)
    {
        var lParam = (nint)((clientY << 16) | (clientX & 0xFFFF));
        var downPosted = PostMessage(window.Handle, LeftButtonDownMessage, MouseLeftButtonParameter, lParam);
        Thread.Sleep(100);
        var upPosted = PostMessage(window.Handle, LeftButtonUpMessage, UIntPtr.Zero, lParam);
        if (downPosted && upPosted)
            return;

        var error = new Win32Exception(Marshal.GetLastWin32Error());
        throw new InvalidOperationException(
            $"Unable to post mouse click to game window. client=({clientX},{clientY}), window={window.Title}, handle=0x{window.Handle.ToInt64():X}, error={error.Message}");
    }

    private static bool IsCursorNear(int screenX, int screenY)
    {
        if (!GetCursorPos(out var cursor))
            return false;

        return Math.Abs(cursor.X - screenX) <= 2 && Math.Abs(cursor.Y - screenY) <= 2;
    }

    private static bool MoveCursorBySendInput(int screenX, int screenY)
    {
        var virtualLeft = GetSystemMetrics(SystemMetricVirtualScreenLeft);
        var virtualTop = GetSystemMetrics(SystemMetricVirtualScreenTop);
        var virtualWidth = GetSystemMetrics(SystemMetricVirtualScreenWidth);
        var virtualHeight = GetSystemMetrics(SystemMetricVirtualScreenHeight);
        if (virtualWidth <= 1 || virtualHeight <= 1)
            return false;

        var absoluteX = (int)Math.Round((screenX - virtualLeft) * 65535.0 / (virtualWidth - 1));
        var absoluteY = (int)Math.Round((screenY - virtualTop) * 65535.0 / (virtualHeight - 1));
        var input = new Input
        {
            Type = SendInputMouse,
            Data = new InputUnion
            {
                Mouse = new MouseInput
                {
                    Dx = absoluteX,
                    Dy = absoluteY,
                    MouseData = 0,
                    Flags = MouseMove | MouseAbsolute | MouseVirtualDesk,
                    Time = 0,
                    ExtraInfo = UIntPtr.Zero
                }
            }
        };

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var sent = SendInput(1, new[] { input }, Marshal.SizeOf<Input>());
            Thread.Sleep(50);
            if (sent == 1 && IsCursorNear(screenX, screenY))
                return true;
        }

        return false;
    }

    private static bool SendKeyboardInput(GameVirtualKey key)
    {
        var scanCode = GetScanCode(key);
        var inputs = new[]
        {
            new Input
            {
                Type = SendInputKeyboard,
                Data = new InputUnion
                {
                    Keyboard = new KeyboardInput
                    {
                        VirtualKey = 0,
                        ScanCode = scanCode,
                        Flags = KeyEventScancode,
                        Time = 0,
                        ExtraInfo = UIntPtr.Zero
                    }
                }
            },
            new Input
            {
                Type = SendInputKeyboard,
                Data = new InputUnion
                {
                    Keyboard = new KeyboardInput
                    {
                        VirtualKey = 0,
                        ScanCode = scanCode,
                        Flags = KeyEventScancode | KeyUp,
                        Time = 0,
                        ExtraInfo = UIntPtr.Zero
                    }
                }
            }
        };

        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) == inputs.Length;
    }

    private static void SendCtrlAOrThrow()
    {
        var inputs = new[]
        {
            CreateVirtualKeyInput(GameVirtualKey.Control, keyUp: false),
            CreateVirtualKeyInput(GameVirtualKey.A, keyUp: false),
            CreateVirtualKeyInput(GameVirtualKey.A, keyUp: true),
            CreateVirtualKeyInput(GameVirtualKey.Control, keyUp: true)
        };

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            var error = new Win32Exception(Marshal.GetLastWin32Error());
            throw new InvalidOperationException($"Unable to send Ctrl+A before text input. sent={sent}/{inputs.Length}, error={error.Message}");
        }
    }

    private static void SendUnicodeTextOrThrow(string text)
    {
        if (text.Length == 0)
            return;

        var inputs = new List<Input>(text.Length * 2);
        foreach (var character in text)
        {
            inputs.Add(CreateUnicodeInput(character, keyUp: false));
            inputs.Add(CreateUnicodeInput(character, keyUp: true));
        }

        var sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Input>());
        if (sent != inputs.Count)
        {
            var error = new Win32Exception(Marshal.GetLastWin32Error());
            throw new InvalidOperationException($"Unable to send Unicode text input. sent={sent}/{inputs.Count}, error={error.Message}");
        }
    }

    private static Input CreateVirtualKeyInput(GameVirtualKey key, bool keyUp) =>
        new()
        {
            Type = SendInputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = (ushort)key,
                    ScanCode = 0,
                    Flags = keyUp ? KeyUp : 0,
                    Time = 0,
                    ExtraInfo = UIntPtr.Zero
                }
            }
        };

    private static Input CreateUnicodeInput(char character, bool keyUp) =>
        new()
        {
            Type = SendInputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = 0,
                    ScanCode = character,
                    Flags = KeyEventUnicode | (keyUp ? KeyUp : 0),
                    Time = 0,
                    ExtraInfo = UIntPtr.Zero
                }
            }
        };

    private static ushort GetScanCode(GameVirtualKey key)
    {
        var scanCode = MapVirtualKey((uint)key, 0);
        return scanCode == 0 ? (ushort)0 : (ushort)scanCode;
    }

    private static void PostKeyOrThrow(
        GameWindowInfo window,
        GameVirtualKey key,
        ForegroundWindowInfo before,
        ForegroundWindowInfo after)
    {
        var downPosted = PostMessage(window.Handle, KeyDownMessage, (nuint)key, 0);
        Thread.Sleep(90);
        var upPosted = PostMessage(window.Handle, KeyUpMessage, (nuint)key, 0);
        if (downPosted && upPosted)
            return;

        var error = new Win32Exception(Marshal.GetLastWin32Error());
        throw new InvalidOperationException(
            $"Unable to send key input to game window. key={key}, " +
            $"before=({before.ToDiagnosticString()}), after=({after.ToDiagnosticString()}), error={error.Message}");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput Mouse;

        [FieldOffset(0)]
        public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(nint windowHandle, int message, nuint wordParameter, nint longParameter);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint windowHandle);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(nint handle, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(nint handle, ref NativePoint point);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);
}
