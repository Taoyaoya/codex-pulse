using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CodexPulse
{
    internal static class WindowBackdrop
    {
        private const int DwmUseImmersiveDarkMode = 20;
        private const int DwmWindowCornerPreference = 33;
        private const int DwmSystemBackdropType = 38;
        private const int AccentEnableAcrylicBlurBehind = 4;
        private const int WindowCompositionAttributeAccentPolicy = 19;

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        public static void Apply(Window window)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            try
            {
                int enabled = 1;
                DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref enabled, sizeof(int));
                int rounded = 2;
                DwmSetWindowAttribute(handle, DwmWindowCornerPreference, ref rounded, sizeof(int));
                int acrylic = 3;
                DwmSetWindowAttribute(handle, DwmSystemBackdropType, ref acrylic, sizeof(int));
            }
            catch { }

            try
            {
                AccentPolicy policy = new AccentPolicy
                {
                    AccentState = AccentEnableAcrylicBlurBehind,
                    AccentFlags = 2,
                    GradientColor = unchecked((int)0xCC2B1716),
                    AnimationId = 0
                };
                int size = Marshal.SizeOf(typeof(AccentPolicy));
                IntPtr pointer = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.StructureToPtr(policy, pointer, false);
                    WindowCompositionAttributeData data = new WindowCompositionAttributeData
                    {
                        Attribute = WindowCompositionAttributeAccentPolicy,
                        Data = pointer,
                        SizeOfData = size
                    };
                    SetWindowCompositionAttribute(handle, ref data);
                }
                finally
                {
                    Marshal.FreeHGlobal(pointer);
                }
            }
            catch { }
        }
    }
}
