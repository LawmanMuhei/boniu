using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace MiniView.WebView2App
{
    internal sealed partial class MainForm
    {
        private async Task CaptureUiReviewAsync()
        {
            Rectangle original = Bounds;
            float originalScale = uiScale;
            bool immersive = settings.ImmersiveMode;
            bool open = settingsOpen;
            bool revealed = toolbarRevealed;
            bool monitoring = pointerTimer.Enabled;
            pointerTimer.Stop();
            try
            {
                foreach (float scale in new[] { 1F, 1.5F })
                {
                    string prefix = "ui-layout-" + (int)(scale * 100);
                    uiScale = scale;
                    settings.ImmersiveMode = false;
                    settingsOpen = true;
                    settingsPanel.Visible = true;
                    webView.Visible = false;
                    SetBoundsProgrammatically(new Rectangle(Left, Top, Scaled(420), Scaled(760)));
                    LayoutWindow();
                    LayoutSettingsPanel();
                    settingsPanel.SetScrollOffset(0);
                    await Task.Delay(140);
                    SaveControlShot(this, prefix + "-settings-top.png");
                    SaveControlShot(toolbar, prefix + "-toolbar-normal.png");
                    settingsPanel.SetScrollOffset(settingsPanel.ScrollContentHeight);
                    await Task.Delay(140);
                    SaveControlShot(this, prefix + "-settings-bottom.png");
                    SetBoundsProgrammatically(new Rectangle(Left, Top, Scaled(280), Scaled(460)));
                    LayoutWindow();
                    await Task.Delay(140);
                    SaveControlShot(toolbar, prefix + "-toolbar-narrow.png");
                    SetBoundsProgrammatically(new Rectangle(Left, Top, Scaled(420), Scaled(760)));
                    settingsOpen = false;
                    settingsPanel.Visible = false;
                    settings.ImmersiveMode = true;
                    toolbarRevealed = true;
                    LayoutWindow();
                    await Task.Delay(140);
                    SaveControlShot(toolbar, prefix + "-toolbar-overlay.png");
                }
                File.WriteAllText(Path.Combine(appFolder, "ui-review.json"), new JavaScriptSerializer().Serialize(new {
                    actualWindowDpi = NativeMethods.GetDpiForWindow(Handle), layoutScales = new[] { 1.0, 1.5 },
                    simulatedLayout = true,
                    scope = "WinForms DrawToBitmap at forced layout scales; not two physical monitor DPI measurements" }));
            }
            finally
            {
                uiScale = originalScale;
                settings.ImmersiveMode = immersive;
                settingsOpen = open;
                toolbarRevealed = revealed;
                settingsPanel.Visible = open;
                webView.Visible = !open;
                SetBoundsProgrammatically(original);
                settingsPanel.SetScrollOffset(0);
                LayoutWindow();
                if (monitoring) pointerTimer.Start();
            }
        }

        private void SaveControlShot(System.Windows.Forms.Control control, string name)
        {
            using (Bitmap image = new Bitmap(control.Width, control.Height))
            {
                control.DrawToBitmap(image, control.ClientRectangle);
                image.Save(Path.Combine(appFolder, name), ImageFormat.Png);
            }
        }
    }
}
