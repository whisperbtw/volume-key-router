using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Win32;

namespace VolumeKeyRouter;

internal sealed class VolumeKeyHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int HcAction = 0;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int LlkhfInjected = 0x10;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;

    private readonly Func<VolumeCommand, bool> onCommand;
    private readonly Func<MediaKeyCommand, bool>? onMediaKey;
    private readonly Func<ShortcutSettings> getShortcuts;
    private readonly NativeMethods.LowLevelKeyboardProc callback;
    private readonly HashSet<int> blockedKeyUps = new();
    private IntPtr hookHandle;

    public VolumeKeyHook(
        Func<VolumeCommand, bool> onCommand,
        Func<bool> shouldBlockKeys,
        Func<MediaKeyCommand, bool>? onMediaKey = null,
        Func<ShortcutSettings>? getShortcuts = null)
    {
        this.onCommand = onCommand;
        this.onMediaKey = onMediaKey;
        this.getShortcuts = getShortcuts ?? (() => new ShortcutSettings());
        callback = HookCallback;
    }

    public void Install()
    {
        if (hookHandle != IntPtr.Zero)
        {
            return;
        }

        var moduleHandle = NativeMethods.GetModuleHandle(null);
        hookHandle = NativeMethods.SetWindowsHookEx(WhKeyboardLl, callback, moduleHandle, 0);
        if (hookHandle == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"Nao foi possivel instalar o hook de teclado. Win32: {error}");
        }
    }

    public void Dispose()
    {
        if (hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(hookHandle);
            hookHandle = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == HcAction)
        {
            var message = wParam.ToInt32();
            if (message is WmKeyDown or WmKeyUp or WmSysKeyDown or WmSysKeyUp)
            {
                var key = Marshal.PtrToStructure<NativeMethods.KeyboardHookStruct>(lParam);
                if ((key.Flags & LlkhfInjected) != 0 &&
                    key.ExtraInfo == NativeMethods.VolumeKeyRouterInjectedKeyExtraInfo)
                {
                    return NativeMethods.CallNextHookEx(hookHandle, nCode, wParam, lParam);
                }

                if (message is WmKeyUp or WmSysKeyUp)
                {
                    return ShouldBlockKeyUp(key.VirtualKeyCode)
                        ? 1
                        : NativeMethods.CallNextHookEx(hookHandle, nCode, wParam, lParam);
                }

                var shortcuts = getShortcuts();
                shortcuts.Normalize();
                var modifiers = GetCurrentModifiers();
                if (TryGetMediaKeyCommand(key.VirtualKeyCode, modifiers, shortcuts, out var mediaCommand) && onMediaKey is not null)
                {
                    // Holding the like shortcut must not keep sending requests.
                    if (mediaCommand == MediaKeyCommand.LikeTrack)
                    {
                        lock (blockedKeyUps)
                        {
                            if (blockedKeyUps.Contains(key.VirtualKeyCode)) return 1;
                        }
                    }
                    var handled = onMediaKey(mediaCommand);
                    if (handled)
                    {
                        TrackBlockedKeyUp(key.VirtualKeyCode);
                    }

                    return handled ? 1 : NativeMethods.CallNextHookEx(hookHandle, nCode, wParam, lParam);
                }

                if (TryGetVolumeCommand(key.VirtualKeyCode, modifiers, shortcuts, out var volumeCommand))
                {
                    var handled = onCommand(volumeCommand);
                    if (handled)
                    {
                        TrackBlockedKeyUp(key.VirtualKeyCode);
                    }

                    return handled ? 1 : NativeMethods.CallNextHookEx(hookHandle, nCode, wParam, lParam);
                }
            }
        }

        return NativeMethods.CallNextHookEx(hookHandle, nCode, wParam, lParam);
    }

    private static bool TryGetVolumeCommand(
        int virtualKeyCode,
        ShortcutModifiers modifiers,
        ShortcutSettings shortcuts,
        out VolumeCommand command)
    {
        if (Matches(virtualKeyCode, modifiers, shortcuts.Mute))
        {
            command = VolumeCommand.Mute;
            return true;
        }

        if (Matches(virtualKeyCode, modifiers, shortcuts.VolumeDown))
        {
            command = VolumeCommand.Down;
            return true;
        }

        if (Matches(virtualKeyCode, modifiers, shortcuts.VolumeUp))
        {
            command = VolumeCommand.Up;
            return true;
        }

        command = default;
        return false;
    }

    private static bool TryGetMediaKeyCommand(
        int virtualKeyCode,
        ShortcutModifiers modifiers,
        ShortcutSettings shortcuts,
        out MediaKeyCommand command)
    {
        if (Matches(virtualKeyCode, modifiers, shortcuts.LikeTrack))
        {
            command = MediaKeyCommand.LikeTrack;
            return true;
        }

        if (Matches(virtualKeyCode, modifiers, shortcuts.PeekMedia))
        {
            command = MediaKeyCommand.Peek;
            return true;
        }

        if (Matches(virtualKeyCode, modifiers, shortcuts.PreviousTrack))
        {
            command = MediaKeyCommand.PreviousTrack;
            return true;
        }

        if (Matches(virtualKeyCode, modifiers, shortcuts.NextTrack))
        {
            command = MediaKeyCommand.NextTrack;
            return true;
        }

        if (Matches(virtualKeyCode, modifiers, shortcuts.PlayPause))
        {
            command = MediaKeyCommand.PlayPause;
            return true;
        }

        if (Matches(virtualKeyCode, modifiers, shortcuts.Stop))
        {
            command = MediaKeyCommand.Stop;
            return true;
        }

        command = default;
        return false;
    }

    private static bool Matches(int virtualKeyCode, ShortcutModifiers modifiers, ShortcutBinding binding)
    {
        return binding.IsConfigured &&
            virtualKeyCode == binding.VirtualKeyCode &&
            (modifiers == binding.Modifiers ||
                binding.Modifiers == ShortcutModifiers.None && IsHardwareMediaOrVolumeKey(virtualKeyCode));
    }

    private static bool IsHardwareMediaOrVolumeKey(int virtualKeyCode)
    {
        return virtualKeyCode is
            KeyboardShortcutKeys.VolumeMute or
            KeyboardShortcutKeys.VolumeDown or
            KeyboardShortcutKeys.VolumeUp or
            KeyboardShortcutKeys.MediaNextTrack or
            KeyboardShortcutKeys.MediaPreviousTrack or
            KeyboardShortcutKeys.MediaStop or
            KeyboardShortcutKeys.MediaPlayPause or
            KeyboardShortcutKeys.LaunchMediaSelect;
    }

    private static ShortcutModifiers GetCurrentModifiers()
    {
        var modifiers = ShortcutModifiers.None;
        if (IsKeyDown(VkControl))
        {
            modifiers |= ShortcutModifiers.Control;
        }

        if (IsKeyDown(VkShift))
        {
            modifiers |= ShortcutModifiers.Shift;
        }

        if (IsKeyDown(VkMenu))
        {
            modifiers |= ShortcutModifiers.Alt;
        }

        if (IsKeyDown(VkLWin) || IsKeyDown(VkRWin))
        {
            modifiers |= ShortcutModifiers.Win;
        }

        return modifiers;
    }

    private static bool IsKeyDown(int virtualKeyCode)
    {
        return (NativeMethods.GetAsyncKeyState(virtualKeyCode) & unchecked((short)0x8000)) != 0;
    }

    private void TrackBlockedKeyUp(int virtualKeyCode)
    {
        lock (blockedKeyUps)
        {
            blockedKeyUps.Add(virtualKeyCode);
        }
    }

    private bool ShouldBlockKeyUp(int virtualKeyCode)
    {
        lock (blockedKeyUps)
        {
            return blockedKeyUps.Remove(virtualKeyCode);
        }
    }
}
