using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CoreDesk_App;

public sealed class WindowsKeyHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

    private readonly LowLevelKeyboardProc _callback;
    private nint _hook;

    public WindowsKeyHook()
    {
        _callback = HookCallback;
    }

    public void Install()
    {
        if (_hook != 0)
        {
            return;
        }

        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _callback, GetModuleHandle(module?.ModuleName), 0);
        App.Services.Diagnostics.Info(_hook == 0
            ? "Windows key hook installation failed."
            : "Windows key hook installed.");
    }

    public void Dispose()
    {
        if (_hook == 0)
        {
            return;
        }

        _ = UnhookWindowsHookEx(_hook);
        _hook = 0;
    }

    private nint HookCallback(int code, nint wParam, nint lParam)
    {
        if (code < 0)
        {
            return CallNextHookEx(_hook, code, wParam, lParam);
        }

        var message = wParam.ToInt32();
        var vkCode = Marshal.ReadInt32(lParam);
        if (vkCode is VK_LWIN or VK_RWIN)
        {
            if (message is WM_KEYUP or WM_SYSKEYUP)
            {
                App.DispatcherQueue.TryEnqueue(() => App.ShowMainShell());
            }

            return 1;
        }

        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string? lpModuleName);
}
