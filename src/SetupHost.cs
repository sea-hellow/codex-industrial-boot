using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
namespace CodexIndustrialSetup
{
    static class Program
    {
        [STAThread] static int Main(string[] args)
        {
            string root = AppDomain.CurrentDomain.BaseDirectory;
            string script = Path.Combine(root, "scripts", "setup.ps1");
            string action = Array.IndexOf(args, "--restore") >= 0 ? "Restore" : "Install";
            bool quiet = Array.IndexOf(args, "--quiet") >= 0;
            try
            {
                if (!File.Exists(script)) throw new FileNotFoundException("请先完整解压安装包。", script);
                string shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
                var info = new ProcessStartInfo(shell, "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + script + "\" -Action " + action + " -Quiet")
                { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardError = true, RedirectStandardOutput = true };
                // Let Windows PowerShell build its own module path when called from PowerShell 7 or a desktop host.
                info.EnvironmentVariables.Remove("PSModulePath");
                string output = "", error = "";
                using (var p = new Process { StartInfo = info })
                {
                    p.OutputDataReceived += (s, e) => { if (e.Data != null) output += e.Data + "\n"; };
                    p.ErrorDataReceived += (s, e) => { if (e.Data != null) error += e.Data + "\n"; };
                    p.Start(); p.BeginOutputReadLine(); p.BeginErrorReadLine(); p.WaitForExit();
                    if (p.ExitCode != 0) throw new InvalidOperationException(error + "\n" + output);
                }
                if (!quiet) MessageBox.Show(action == "Install" ? "安装完成。\n\n本项目为免费、非官方社区启动动画，与 OpenAI 无隶属关系，未获其赞助、认证或背书。\n视频中的验证与欢迎语均为预制演出，不是真实认证或官方表态。\n\n现在点击原来的 Codex / ChatGPT 桌面图标，会先播放 32 秒启动动画。\n安装包可以移动或删除，已安装的动画不受影响。\n如任务栏旧图标未生效，请取消固定，再从桌面重新固定。\n\n第三方授权核验尚未完成，声明不保证免于争议或诉讼。\n完整说明与权利反馈方式保存在安装目录的 NOTICE.md 和 RIGHTS.md。" : "已恢复仍由本启动器管理的原始入口。\n你手动改过的图标会保留，详见恢复记录。",
                    "Codex Industrial · 非官方社区项目", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            catch (Exception e)
            {
                if (!quiet) MessageBox.Show(e.Message, "安装未完成", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}
