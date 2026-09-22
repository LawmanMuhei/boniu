using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace MiniView.Bootstrap
{
    internal static class Program
    {
        private const string Version = MiniView.WebView2App.AppVersion.Current;
        private const string AppExecutableName = "波妞摸鱼.exe";

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                string architecture = IntPtr.Size == 4 ? "x86" : "x64";
                string appRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MiniViewWebView2", "App");
                string installRoot = Path.Combine(appRoot, Version + "-" + architecture);
                Directory.CreateDirectory(installRoot);

                Dictionary<string, string> files = new Dictionary<string, string>();
                files.Add(AppExecutableName, "payload.BoNiuMoYu.exe");
                files.Add(AppExecutableName + ".config", "payload.BoNiuMoYu.exe.config");
                files.Add("Microsoft.Web.WebView2.Core.dll", "payload.Microsoft.Web.WebView2.Core.dll");
                files.Add("Microsoft.Web.WebView2.WinForms.dll", "payload.Microsoft.Web.WebView2.WinForms.dll");
                files.Add("WebView2Loader.dll", "payload.WebView2Loader.dll");

                foreach (KeyValuePair<string, string> file in files)
                    ExtractResource(file.Value, Path.Combine(installRoot, file.Key));
                CleanupOldPayloads(appRoot, installRoot, architecture);

                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = Path.Combine(installRoot, AppExecutableName);
                startInfo.WorkingDirectory = installRoot;
                startInfo.UseShellExecute = false;
                startInfo.Arguments = MiniView.CommandLineArguments.Join(args);
                startInfo.EnvironmentVariables["BONIU_LAUNCHER_PATH"] = Assembly.GetExecutingAssembly().Location;
                Process process = Process.Start(startInfo);
                if (HasArgument(args, "--self-test") || HasArgument(args, "--smoke-test")
                    || HasArgument(args, "--live-smoke-test") || HasArgument(args, "--settings-smoke-test")
                    || HasArgument(args, "--dom-probe") || HasArgument(args, "--update-check-test")
                    )
                {
                    process.WaitForExit();
                    return process.ExitCode;
                }
                return 0;
            }
            catch (Exception exception)
            {
                MessageBox.Show("波妞摸鱼启动失败。\r\n\r\n" + exception.Message,
                    "波妞摸鱼", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 2;
            }
        }

        private static void ExtractResource(string resourceName, string targetPath)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream input = assembly.GetManifestResourceStream(resourceName))
            {
                if (input == null) throw new InvalidOperationException("缺少内置组件：" + resourceName);
                byte[] bytes = ReadAllBytes(input);
                if (File.Exists(targetPath) && FilesEqual(bytes, targetPath)) return;

                string temporaryPath = targetPath + ".new";
                File.WriteAllBytes(temporaryPath, bytes);
                File.Copy(temporaryPath, targetPath, true);
                File.Delete(temporaryPath);
            }
        }

        private static byte[] ReadAllBytes(Stream input)
        {
            using (MemoryStream output = new MemoryStream())
            {
                input.CopyTo(output);
                return output.ToArray();
            }
        }

        private static bool FilesEqual(byte[] embedded, string path)
        {
            FileInfo info = new FileInfo(path);
            if (info.Length != embedded.LongLength) return false;
            using (SHA256 hash = SHA256.Create())
            {
                byte[] embeddedHash = hash.ComputeHash(embedded);
                using (FileStream stream = File.OpenRead(path))
                {
                    byte[] fileHash = hash.ComputeHash(stream);
                    for (int i = 0; i < embeddedHash.Length; i++)
                        if (embeddedHash[i] != fileHash[i]) return false;
                }
            }
            return true;
        }

        private static void CleanupOldPayloads(string appRoot, string currentPath, string architecture)
        {
            try
            {
                DirectoryInfo root = new DirectoryInfo(appRoot);
                if (!root.Exists) return;
                DirectoryInfo[] candidates = Array.FindAll(root.GetDirectories(),
                    delegate(DirectoryInfo directory)
                    {
                        return directory.Name.EndsWith("-" + architecture, StringComparison.OrdinalIgnoreCase);
                    });
                Array.Sort(candidates, delegate(DirectoryInfo left, DirectoryInfo right)
                {
                    return right.LastWriteTimeUtc.CompareTo(left.LastWriteTimeUtc);
                });
                bool keptPrevious = false;
                foreach (DirectoryInfo candidate in candidates)
                {
                    if (string.Equals(candidate.FullName, currentPath, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!keptPrevious) { keptPrevious = true; continue; }
                    try { candidate.Delete(true); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static bool HasArgument(string[] args, string expected)
        {
            foreach (string argument in args)
                if (string.Equals(argument, expected, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
