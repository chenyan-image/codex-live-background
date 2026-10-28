using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
internal static class DouyinPreviewLauncher {
    [STAThread]static void Main(){try{string root=AppDomain.CurrentDomain.BaseDirectory,state=Path.Combine(root,"DouyinPreviewData");Directory.CreateDirectory(state);Process.Start(new ProcessStartInfo(Path.Combine(root,"CodexLiveBackground.exe"),"--spark-preview \""+state+"\""){WorkingDirectory=root,UseShellExecute=false});}catch(Exception error){MessageBox.Show(error.Message,"CodexHub · 抖音功能预览");}}
}
