using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

namespace MiniView.WebView2App
{
    public sealed class BoundsData
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }

        internal static BoundsData FromRectangle(Rectangle value)
        {
            return new BoundsData { X = value.X, Y = value.Y, Width = value.Width, Height = value.Height };
        }
    }

    public sealed class AppSettings
    {
        public BoundsData NormalBounds { get; set; }
        public BoundsData LiveBounds { get; set; }
        public bool AlwaysOnTop { get; set; }
        public bool AutoLandscapeLive { get; set; }
        public bool ChromeHidden { get; set; }
        public string HideShortcut { get; set; }
        public string ChromeShortcut { get; set; }
        public bool Muted { get; set; }
        public bool AutoHideEnabled { get; set; }
        public int AutoHideDelayMilliseconds { get; set; }
        public bool HideWhenInactive { get; set; }
        public bool HideLeftNav { get; set; }
        public bool HideTopBar { get; set; }
        public bool HideRightBar { get; set; }
        public bool ImmersiveMode { get; set; }
        public string ImmersiveShortcut { get; set; }

        public AppSettings()
        {
            AlwaysOnTop = true;
            HideShortcut = "Control+Alt+D";
            ChromeShortcut = "Control+Alt+B";
            ImmersiveShortcut = "Control+Alt+F";
            AutoHideEnabled = true;
            AutoHideDelayMilliseconds = 100;
            HideWhenInactive = false;
            HideLeftNav = false;
            HideTopBar = false;
            HideRightBar = false;
            ImmersiveMode = false;
        }
    }

    internal sealed class SettingsStore
    {
        private readonly string settingsPath;
        private readonly JavaScriptSerializer serializer = new JavaScriptSerializer();

        internal SettingsStore(string settingsPath)
        {
            this.settingsPath = settingsPath;
        }

        internal AppSettings Load()
        {
            try
            {
                if (!File.Exists(settingsPath)) return new AppSettings();
                AppSettings value = serializer.Deserialize<AppSettings>(File.ReadAllText(settingsPath, Encoding.UTF8));
                if (value == null) return new AppSettings();
                value.AutoHideDelayMilliseconds = WindowRules.NormalizeAutoHideDelay(value.AutoHideDelayMilliseconds);
                return value;
            }
            catch
            {
                return new AppSettings();
            }
        }

        internal void Save(AppSettings settings)
        {
            string directory = Path.GetDirectoryName(settingsPath);
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
            string json = serializer.Serialize(settings);
            File.WriteAllText(settingsPath, json + Environment.NewLine, new UTF8Encoding(false));
        }

    }

    internal static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct NativeRectangle
        {
            internal int Left;
            internal int Top;
            internal int Right;
            internal int Bottom;
        }

        internal const int WM_HOTKEY = 0x0312;
        internal const int WM_ACTIVATEAPP = 0x001C;
        internal const int WM_NCHITTEST = 0x0084;
        internal const int WM_NCLBUTTONDOWN = 0x00A1;
        internal const int HTCAPTION = 2;
        internal const int HTLEFT = 10;
        internal const int HTRIGHT = 11;
        internal const int HTTOP = 12;
        internal const int HTTOPLEFT = 13;
        internal const int HTTOPRIGHT = 14;
        internal const int HTBOTTOM = 15;
        internal const int HTBOTTOMLEFT = 16;
        internal const int HTBOTTOMRIGHT = 17;
        internal const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        internal const int DWMWCP_ROUND = 2;

        [DllImport("dwmapi.dll")]
        internal static extern int DwmSetWindowAttribute(IntPtr windowHandle, int attribute, ref int attributeValue, int attributeSize);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterHotKey(IntPtr windowHandle, int id);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        internal static extern IntPtr SendMessage(IntPtr windowHandle, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern short GetAsyncKeyState(int virtualKey);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(IntPtr windowHandle, out NativeRectangle rectangle);
    }
}
