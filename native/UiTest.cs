using System;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Web.WebView2.Core;

namespace Orbit {
    internal static class TestArtifacts {
        internal static readonly string Root=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","..","artifacts","ui-test"));
        internal static string PathFor(string name) {
            Directory.CreateDirectory(Root);
            return Path.Combine(Root,name);
        }
    }

    // This is deliberately an in-process smoke test: it exercises WebView2 message
    // transport, the native file bridge and a real ConPTY child, rather than mocks.
    internal static class UiTest {
        internal static async Task Run(MainWindow window) {
            string report=TestArtifacts.PathFor("report.txt");
            string image=TestArtifacts.PathFor("orbit-desktop.png");
            string source=TestArtifacts.PathFor("ui-test-note.txt");
            var json=new JavaScriptSerializer();
            Exception failure=null;
            string metrics="Idle app metrics unavailable.";
            try {
                File.WriteAllText(source,"before\r\n",new System.Text.UTF8Encoding(false));
                bool ready=false;
                for(int i=0;i<100;i++) {
                    if((await window.ExecuteForTest("Boolean(window.orbitDiagnostics && window.orbitDiagnostics().ready)"))=="true") { ready=true; break; }
                    await Task.Delay(50);
                }
                if(!ready) throw new TimeoutException("frontend did not become ready for the idle measurement");
                await SessionHistoryUiTest.Run(window);
                await VerifyWindowChrome(window);
                await Task.Delay(4000);
                int[] webViewProcessIds=window.GetTestWebViewProcessIds();
                metrics=await Task.Run(()=>MeasureApp(webViewProcessIds));
                string quoted=json.Serialize(source);
                string jsFile=TestArtifacts.PathFor("race-check.js"),pythonFile=TestArtifacts.PathFor("race-check.py");
                File.WriteAllText(jsFile,"const value = 1;\n");File.WriteAllText(pythonFile,"value = 2\n");
                string paths=json.Serialize(new [] { jsFile,pythonFile,source });
                string script="window.__orbitUiTest={done:false};(async()=>{try{"
                    +"const pause=ms=>new Promise(r=>setTimeout(r,ms));"
                    +"for(let i=0;i<100&&!window.orbitUiTest;i++)await pause(50);"
                    +"if(!window.orbitUiTest)throw new Error('UI test API was not initialized');"
                    +"if(window.orbitDiagnostics().windowControls!==3)throw new Error('custom window controls are missing');"
                    +"const savedTab=document.querySelector('#sidebar-saved-tab'),explorerTab=document.querySelector('#sidebar-explorer-tab'),savedPanel=document.querySelector('#saved-conversations-panel'),explorerPanel=document.querySelector('#explorer-panel'),tree=document.querySelector('#file-tree'),filter=document.querySelector('#file-filter');"
                    +"if(!savedTab||!explorerTab||!savedPanel||!explorerPanel)throw new Error('sidebar mode tabs are missing');"
                    +"explorerTab.click();await pause(80);const sidebarBottom=document.querySelector('.sidebar-bottom').getBoundingClientRect(),explorerBox=explorerPanel.getBoundingClientRect();if(explorerPanel.hidden||explorerBox.height<120||explorerBox.bottom>sidebarBottom.top+1)throw new Error('explorer did not use the available sidebar height');"
                    +"document.querySelector('#tree-actions button').click();await pause(120);const folder=[...tree.querySelectorAll('.tree-directory')][0];if(!folder)throw new Error('explorer fixture has no folder');folder.click();await pause(120);if(!folder.classList.contains('expanded'))throw new Error('explorer folder did not expand');filter.value='sidebar state';filter.dispatchEvent(new Event('input',{bubbles:true}));tree.scrollTop=37;const scroll=tree.scrollTop;savedTab.click();await pause(40);explorerTab.click();await pause(40);if(filter.value!=='sidebar state'||!folder.classList.contains('expanded')||tree.scrollTop!==scroll)throw new Error('explorer state was lost while switching sidebar modes');"
                    +"await window.orbitUiTest.exerciseEditorConcurrency("+paths+");"
                    +"const file=await window.orbitUiTest.editAndSave('saved from WebView2\\r\\n한국어 UTF-8');"
                    +"const setShell=async value=>{document.querySelector('#preferences').click();await pause(30);const select=document.querySelector('#default-shell');if(!select)throw new Error('default shell setting is missing');select.value=value;select.dispatchEvent(new Event('change',{bubbles:true}));document.querySelector('#modal').close();await pause(30);};"
                    +"const launchDefault=()=>document.dispatchEvent(new KeyboardEvent('keydown',{bubbles:true,cancelable:true,ctrlKey:true,shiftKey:true,code:'KeyT'}));"
                    +"await setShell('cmd');"
                    +"document.querySelector('#preferences').click();await pause(30);if(document.querySelector('#default-shell').value!=='cmd')throw new Error('default shell selection was lost');document.querySelector('#modal').close();"
                    +"launchDefault();"
                    +"for(let i=0;i<100;i++){const d=window.orbitUiTest.diagnostics();if(d.sessions[0]&&d.sessions[0].pid)break;await pause(50);}"
                    +"if(window.orbitUiTest.diagnostics().sessions[0]?.profile!=='cmd')throw new Error('default terminal shortcut did not create CMD');"
                    +"await window.orbitUiTest.send('set /a 12345+67890\\r\\n');"
                    +"await setShell('powershell');launchDefault();"
                    +"for(let i=0;i<100;i++){const d=window.orbitUiTest.diagnostics();if(d.sessions.length===2&&d.sessions[1].pid)break;await pause(50);}"
                    +"if(window.orbitUiTest.diagnostics().sessions[1]?.profile!=='powershell')throw new Error('default terminal shortcut did not create PowerShell');await window.orbitUiTest.split();"
                    +"await window.orbitUiTest.send(\"Write-Output ('결과 ' + (12345 + 67890))\\r\\n\");"
                    +"for(let i=0;i<160;i++){const d=window.orbitUiTest.diagnostics();if(d.sessions.length===2&&JSON.stringify(d).indexOf('결과 80235')>=0){window.__orbitUiTest={done:true,result:{file:file,diagnostics:d}};return;}await pause(50);}"
                    +"throw new Error('interactive terminal output was not rendered');"
                    +"}catch(e){window.__orbitUiTest={done:true,error:String(e&&e.stack||e)};}})();'started';";
                await window.ExecuteForTest(script);
                string result=null;
                for(int i=0;i<140;i++) {
                    result=await window.ExecuteForTest("JSON.stringify(window.__orbitUiTest || null)");
                    if(result.IndexOf("\\\"done\\\":true",StringComparison.Ordinal)>=0) break;
                    await Task.Delay(100);
                }
                if(result==null || result.IndexOf("\\\"done\\\":true",StringComparison.Ordinal)<0) throw new TimeoutException("UI test timed out");
                if(result.IndexOf("\\\"error\\\"",StringComparison.Ordinal)>=0) throw new InvalidOperationException(result);
                string saved=File.ReadAllText(source,new System.Text.UTF8Encoding(false));
                if(saved!="saved from WebView2\r\n한국어 UTF-8") throw new InvalidOperationException("editor save did not reach the native file bridge");
                var preferences=json.Deserialize<System.Collections.Generic.Dictionary<string,object>>(File.ReadAllText(TestArtifacts.PathFor("webview-profile/settings.json")));
                if(!preferences.ContainsKey("defaultShell") || (string)preferences["defaultShell"]!="powershell") throw new InvalidOperationException("default shell preference was not persisted by the native bridge");
                await LayoutUiTest.Run(window);
                await window.CaptureForTest(image);
                await MarkdownUiTest.Run(window);
                await PickerUiTest.Run(window);
                await ThemeUiTest.Run(window);
                await window.ExecuteForTest("document.querySelector('#sidebar-explorer-tab').click();document.documentElement.dataset.theme='light';'light'");
                await window.CaptureForTest(TestArtifacts.PathFor("sidebar-explorer-light.png"));
                await window.ExecuteForTest("document.documentElement.dataset.theme='dark';'dark'");
                await AccountUiTest.Run(window);
                await Task.Run(()=>RemoteUiTest.Run());
                await MobileUiTest.Run(window);
                var normalSize=window.Size;
                window.Size=new System.Drawing.Size(900,620);
                await Task.Delay(200);
                await window.CaptureForTest(TestArtifacts.PathFor("compact-minimum.png"));
                window.Size=normalSize;
                File.WriteAllText(report,"PASS WebView2 UI, default-terminal shortcut, file save, CMD/PowerShell, drag/drop splits, Markdown, themed file/folder picker, exclusive new-file creation, and live dark/light themes with editor/session preservation\r\n"+metrics+"\r\n"+result);
                window.EndUiTest(false);
            } catch(Exception ex) {
                failure=ex;
            }
            if(failure!=null) {
                File.WriteAllText(report,"FAIL UI smoke test\r\n"+metrics+"\r\n"+failure);
                try { await window.CaptureForTest(image); } catch { }
                window.EndUiTest(true);
            }
        }
        private static string MeasureApp(int[] webViewProcessIds) {
            int[] ids=webViewProcessIds.Concat(new [] { Process.GetCurrentProcess().Id }).Distinct().ToArray();
            var processes=ids.Select(id=> { try { return Process.GetProcessById(id); } catch { return null; } }).Where(p=>p!=null).ToArray();
            try {
                TimeSpan start=processes.Aggregate(TimeSpan.Zero,(sum,p)=>sum+p.TotalProcessorTime); var clock=Stopwatch.StartNew();
                System.Threading.Thread.Sleep(2000); foreach(var p in processes) p.Refresh();
                double cpu=(processes.Aggregate(TimeSpan.Zero,(sum,p)=>sum+p.TotalProcessorTime)-start).TotalMilliseconds/clock.Elapsed.TotalMilliseconds/Environment.ProcessorCount*100;
                double working=processes.Sum(p=>p.WorkingSet64)/1048576.0, privateBytes=processes.Sum(p=>p.PrivateMemorySize64)/1048576.0;
                return "Settled 2-second sample (host + "+(processes.Length-1)+" WebView2 processes): working-set sum "+Math.Round(working,1)+" MB (shared pages can be counted twice) · private "+Math.Round(privateBytes,1)+" MB · CPU "+Math.Round(cpu,2)+"%";
            } finally { foreach(var p in processes) p.Dispose(); }
        }
        private static async Task VerifyWindowChrome(MainWindow window) {
            if(window.FormBorderStyle!=System.Windows.Forms.FormBorderStyle.None) throw new InvalidOperationException("native title bar is still enabled");
            if(window.Icon==null) throw new InvalidOperationException("application icon was not loaded");
            await window.ExecuteForTest("document.querySelector('#window-maximize').click();'clicked'");
            await Task.Delay(150);
            if(window.WindowState!=System.Windows.Forms.FormWindowState.Maximized) throw new InvalidOperationException("maximize control did not maximize the window");
            var work=System.Windows.Forms.Screen.FromHandle(window.Handle).WorkingArea; var bounds=window.Bounds;
            if(bounds.Left<work.Left || bounds.Top<work.Top || bounds.Right>work.Right || bounds.Bottom>work.Bottom) throw new InvalidOperationException("maximized window exceeds the taskbar work area");
            await window.CaptureForTest(TestArtifacts.PathFor("compact-welcome-maximized.png"));
            await window.ExecuteForTest("document.querySelector('#window-maximize').click();'clicked'");
            await Task.Delay(150);
            if(window.WindowState!=System.Windows.Forms.FormWindowState.Normal) throw new InvalidOperationException("maximize control did not restore the window");
            window.WindowState=System.Windows.Forms.FormWindowState.Minimized;
            window.WindowState=System.Windows.Forms.FormWindowState.Normal;
        }
    }
}
