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
    private const int VkVolumeMute = 0xAD;
    private const int VkVolumeDown = 0xAE;
    private const int VkVolumeUp = 0xAF;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;

    private readonly Func<VolumeCommand, bool> onCommand;
    private readonly Func<bool> shouldBlockKeys;
    private readonly NativeMethods.LowLevelKeyboardProc callback;
    private IntPtr hookHandle;

    public VolumeKeyHook(Func<VolumeCommand, bool> onCommand, Func<bool> shouldBlockKeys)
    {
        this.onCommand = onCommand;
        this.shouldBlockKeys = shouldBlockKeys;
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
                if (key.VirtualKeyCode is VkVolumeMute or VkVolumeDown or VkVolumeUp)
                {
                    if (message is WmKeyDown or WmSysKeyDown)
                    {
                        var command = key.VirtualKeyCode switch
                        {
                            VkVolumeMute => VolumeCommand.Mute,
                            VkVolumeDown => VolumeCommand.Down,
                            _ => VolumeCommand.Up
                        };
                        var handled = onCommand(command);
                        return handled ? 1 : NativeMethods.CallNextHookEx(hookHandle, nCode, wParam, lParam);
                    }

                    return shouldBlockKeys()
                        ? 1
                        : NativeMethods.CallNextHookEx(hookHandle, nCode, wParam, lParam);
                }
            }
        }

        return NativeMethods.CallNextHookEx(hookHandle, nCode, wParam, lParam);
    }
}
