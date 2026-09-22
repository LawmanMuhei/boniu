using System;
using System.IO;
using System.Net.Http;

namespace MiniView.WebView2App
{
    internal static class FailureMessages
    {
        internal static string ForUpdate(Exception exception)
        {
            if (exception is OperationCanceledException) return "更新已取消或超时，请检查网络后重试；当前版本未被替换。";
            if (exception is HttpRequestException) return "无法连接更新服务，请检查网络、代理或稍后重试。";
            if (exception is UnauthorizedAccessException) return "没有更新目录的写入权限，请将程序放到当前用户可写的目录后重试。";
            if (exception is InvalidDataException) return "更新文件无效或校验失败，已取消安装；请重试或从官方项目发布页下载。";
            if (exception is IOException) return "更新文件读写失败，请检查磁盘空间、文件占用和目录权限。";
            return "更新未完成，请稍后重试；可在本地数据目录查看日志。";
        }

        internal static string ForWebView(Exception exception)
        {
            if (exception.GetType().Name.IndexOf("RuntimeNotFound", StringComparison.OrdinalIgnoreCase) >= 0)
                return "未找到 Microsoft Edge WebView2 Runtime。请从微软官网安装 Evergreen Runtime，完成后重新打开波妞摸鱼。仅支持 Windows 10/11。";
            if (exception is UnauthorizedAccessException)
                return "无法访问本地浏览器数据目录。请检查当前用户对本地数据目录的读写权限，然后重新打开程序。无需清除登录数据。";
            return "浏览器初始化失败。请检查 WebView2 Runtime 是否正常、磁盘空间是否充足，并重新打开程序。仍失败时可查看本地数据目录的 logs/run.log。";
        }
    }
}
