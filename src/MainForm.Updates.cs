using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MiniView.WebView2App
{
    internal sealed partial class MainForm
    {
        private async Task CheckForUpdatesAsync(bool manual)
        {
            if (updateCheckRunning || isQuitting || IsDisposed) return;
            updateCheckRunning = true;
            try
            {
                // 两步式确认（老板键安全，无模态对话框）：第一步检查并亮出「下载并安装」按钮，再次点击按钮才开始下载安装。
                if (pendingUpdate != null && manual)
                {
                    await DownloadAndInstallAsync(pendingUpdate);
                    return;
                }
                updateStatus.Text = "正在检查更新…";
                LayoutSettingsPanel();
                UpdateInfo info = await updateService.CheckAsync();
                if (isQuitting || IsDisposed) return;
                if (info == null)
                {
                    pendingUpdate = null;
                    checkUpdateButton.Text = "检查更新";
                    toolTip.SetToolTip(checkUpdateButton, "检查 GitHub Releases 上的新版本");
                    updateStatus.Text = "v" + UpdateService.CurrentVersion.ToString(3) + " · 已是最新版";
                    if (manual) ShowTransientNotice("当前已是最新版本。");
                    return;
                }
                pendingUpdate = info;
                string summary = "发现 v" + info.Version.ToString(3);
                if (!string.IsNullOrWhiteSpace(info.Notes))
                {
                    string first = info.Notes.Trim().Split('\n')[0].Trim();
                    if (first.Length > 36) first = first.Substring(0, 36) + "…";
                    summary += " · " + first;
                }
                updateStatus.Text = summary;
                checkUpdateButton.Text = "下载并安装 v" + info.Version.ToString(3);
                toolTip.SetToolTip(checkUpdateButton, "下载 v" + info.Version.ToString(3) + " 并通过 SHA-256 校验后安装，失败自动回滚。再次点击即开始。");
                LayoutSettingsPanel();
            }
            catch (Exception exception)
            {
                Diagnostics.LogException("CheckForUpdates", exception);
                if (isQuitting || IsDisposed) return;
                updateStatus.Text = FailureMessages.ForUpdate(exception);
                if (pendingUpdate != null)
                    checkUpdateButton.Text = "重试安装 v" + pendingUpdate.Version.ToString(3);
                if (manual) ShowTransientNotice(FailureMessages.ForUpdate(exception) + " " + exception.Message);
            }
            finally
            {
                updateCheckRunning = false;
                if (!IsDisposed && !isQuitting) LayoutSettingsPanel();
            }
        }

        private async Task DownloadAndInstallAsync(UpdateInfo info)
        {
            updateStatus.Text = "正在下载并校验…";
            checkUpdateButton.Text = "正在下载…";
            LayoutSettingsPanel();
            PreparedUpdate prepared = await updateService.DownloadAsync(info);
            if (isQuitting || IsDisposed) return;
            updateStatus.Text = "正在安全安装…";
            LayoutSettingsPanel();
            SaveSettingsNow();
            updateService.BeginInstall(prepared, Process.GetCurrentProcess().Id);
            QuitApplication();
        }

    }
}
