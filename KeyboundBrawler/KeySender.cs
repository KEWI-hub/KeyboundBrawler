using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KeyboundBrawler;

// Posts keyboard messages to the game window, so it works even when the window is not focused.
public static class KeySender
{
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmChar = 0x0102;
    private const int VkReturn = 0x0D;
    private const int VkBack = 0x08;

    [DllImport("user32.dll")]
    private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private static IntPtr window;

    private static IntPtr Window
    {
        get
        {
            if (window == IntPtr.Zero)
                window = Process.GetCurrentProcess().MainWindowHandle;
            return window;
        }
    }

    public static void Char(char c)
    {
        GameInput.MarkActive();
        PostMessageW(Window, WmChar, c, 1);
    }

    public static void Enter() => Key(VkReturn, 0x1C);

    // The minigame box ignores a bare Backspace key press; it deletes on the backspace character.
    // Sending both may delete twice, which is harmless because we only clear whole words.
    public static void Backspace()
    {
        Key(VkBack, 0x0E);
        PostMessageW(Window, WmChar, VkBack, 1 | 0x0E << 16);
    }

    private static void Key(int vk, int scanCode)
    {
        GameInput.MarkActive();
        PostMessageW(Window, WmKeyDown, vk, 1 | scanCode << 16);
        PostMessageW(Window, WmKeyUp, vk, unchecked((int)0xC0000001) | scanCode << 16);
    }
}
