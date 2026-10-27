using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;

namespace Orbit {
    internal static class TestArtifacts {
        internal static readonly string Root=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","..","artifacts","ui-test"));
        internal static string PathFor(string name) { Directory.CreateDirectory(Root); return Path.Combine(Root,name); }
    }

    // Exercises the startup terminal over the real WebView2 bridge and ConPTY.
    internal static class UiTest {
        internal static async Task Run(MainWindow window) {
            string report=TestArtifacts.PathFor("report.txt"),image=TestArtifacts.PathFor("orbit-desktop.png");
            Exception failure=null;
            try {
                bool ready=false;
                for(int i=0;i<160;i++) {
                    if((await window.ExecuteForTest("Boolean(window.orbitDiagnostics && window.orbitDiagnostics().ready)"))=="true") { ready=true; break; }
                    await Task.Delay(50);
                }
                if(!ready) throw new TimeoutException("PowerShell did not start on launch");
                if((await window.ExecuteForTest("window.orbitDiagnostics().profile"))!="\"powershell\"") throw new InvalidOperationException("startup shell was not PowerShell");
                if((await window.ExecuteForTest("window.orbitDiagnostics().windowControls"))!="3") throw new InvalidOperationException("window controls are missing");
                await SendAndWait(window,"Write-Output ([string]::Concat('ORBIT_TERMINAL_', 'SMOKE_PS'))","ORBIT_TERMINAL_SMOKE_PS");

                await window.ExecuteForTest("window.orbitUiTest.startCmd()");
                await WaitFor(window,"window.orbitUiTest.diagnostics().sessions[0]?.profile==='cmd' && window.orbitUiTest.diagnostics().sessions[0]?.pid");
                await SendAndWait(window,"echo ORBIT_TERMINAL_SMOKE_CMD","ORBIT_TERMINAL_SMOKE_CMD",2);
                await window.ExecuteForTest("window.orbitUiTest.startPowerShell()");
                await WaitFor(window,"window.orbitUiTest.diagnostics().sessions[0]?.profile==='powershell' && window.orbitUiTest.diagnostics().sessions[0]?.pid");
                await SendAndWait(window,"Write-Output ([string]::Concat('ORBIT_TERMINAL_SMOKE_', 'RESTART'))","ORBIT_TERMINAL_SMOKE_RESTART");

                var original=window.Size; bool resizeChecked=false;
                try {
                    int screenWidth=Screen.PrimaryScreen.WorkingArea.Width;
                    window.Size=new Size(Math.Min(720,screenWidth),Math.Min(500,Screen.PrimaryScreen.WorkingArea.Height));
                    await Task.Delay(300);
                    int narrow=await Number(window,"window.orbitDiagnostics().cols");int narrowWidth=window.ClientSize.Width;
                    window.Size=new Size(Math.Min(1180,screenWidth),Math.Min(800,Screen.PrimaryScreen.WorkingArea.Height));
                    await Task.Delay(300);
                    int wide=await Number(window,"window.orbitDiagnostics().cols");
                    if(window.ClientSize.Width>=narrowWidth+150) { resizeChecked=true;if(wide<=narrow) throw new InvalidOperationException("terminal column count did not grow with the window"); }
                    await window.CaptureForTest(image);
                } finally { window.Size=original; }

                await VerifyWindowChrome(window);
                File.WriteAllText(report,"PASS: startup PowerShell, PowerShell command output, CMD restart and command output, PowerShell restart, native window controls; resize "+(resizeChecked?"checked":"skipped (screen width did not permit a wider comparison)")+".\r\n");
                window.EndUiTest(false);
            } catch(Exception ex) { failure=ex; }
            if(failure!=null) {
                File.WriteAllText(report,"FAIL terminal UI smoke test\r\n"+failure);
                try { await window.CaptureForTest(image); } catch { }
                window.EndUiTest(true);
            }
        }
        private static async Task SendAndWait(MainWindow window,string command,string marker,int occurrences=1) {
            await window.ExecuteForTest("window.orbitUiTest.send("+new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(command+"\r\n")+")");
            await WaitFor(window,"(window.orbitUiTest.diagnostics().output.match(new RegExp("+new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(marker)+",'g'))||[]).length >= "+occurrences);
        }
        private static async Task WaitFor(MainWindow window,string expression) {
            for(int i=0;i<120;i++) { if((await window.ExecuteForTest("Boolean("+expression+")"))=="true") return; await Task.Delay(50); }
            throw new TimeoutException("terminal condition timed out: "+expression);
        }
        private static async Task<int> Number(MainWindow window,string expression) {
            string value=await window.ExecuteForTest(expression);
            return Int32.Parse(value,System.Globalization.CultureInfo.InvariantCulture);
        }
        private static async Task VerifyWindowChrome(MainWindow window) {
            if(window.FormBorderStyle!=FormBorderStyle.None || window.Icon==null) throw new InvalidOperationException("custom native window chrome was not initialized");
            await window.ExecuteForTest("document.querySelector('#window-maximize').click()");
            await Task.Delay(150);
            if(window.WindowState!=FormWindowState.Maximized) throw new InvalidOperationException("maximize button did not maximize the window");
            var work=Screen.FromHandle(window.Handle).WorkingArea; var bounds=window.Bounds;
            if(bounds.Left<work.Left || bounds.Top<work.Top || bounds.Right>work.Right || bounds.Bottom>work.Bottom) throw new InvalidOperationException("maximized window exceeds the taskbar work area");
            await window.CaptureForTest(TestArtifacts.PathFor("terminal-maximized.png"));
            await window.ExecuteForTest("document.querySelector('#window-maximize').click()");
            await Task.Delay(150);
            if(window.WindowState!=FormWindowState.Normal) throw new InvalidOperationException("maximize button did not restore the window");
        }
    }
}
