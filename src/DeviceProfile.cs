using System;
using System.Globalization;

namespace MiniView.WebView2App
{
    internal static class DeviceProfile
    {
        internal static int BrowserMajor(string browserVersion)
        {
            int major;
            string first = (browserVersion ?? string.Empty).Split('.')[0];
            return int.TryParse(first, NumberStyles.None, CultureInfo.InvariantCulture, out major) && major > 0
                ? major : 130;
        }

        internal static string BuildMobileUserAgent(string browserVersion)
        {
            return "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/"
                + BrowserMajor(browserVersion).ToString(CultureInfo.InvariantCulture) + ".0.0.0 Mobile Safari/537.36";
        }
    }
}
