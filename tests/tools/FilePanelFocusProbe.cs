using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;
namespace Orbit {
    // Compiled separately by file-panel-focus.ps1; never included in production builds.
    internal static class FilePanelFocusProbe {
        [STAThread] static void Main(string[] args) {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var window=new MainWindow(new[]{"--remote-test",args[0]});
            window.Opacity=0;
            window.ShowInTaskbar=false;
            window.Shown+=async delegate {await Run(window);};
            Application.Run(window);
        }
        static async Task Run(MainWindow window) {
            string report=TestArtifacts.PathFor("file-panel-results.txt");
            string file=TestArtifacts.PathFor("file-panel-note.txt"),other=TestArtifacts.PathFor("file-panel-other.txt");
            File.WriteAllText(file,"fixture text\n");
            File.WriteAllText(other,"other fixture\n");
            try {
                bool ready=false;
                for(int i=0;i<200;i++) {
                    try {ready=await window.ExecuteForTest("Boolean(window.orbitUiTest && window.orbitDiagnostics().sessions[0]?.output.includes('ORBIT_REMOTE_TEST_READY'))")=="true";}
                    catch(NullReferenceException) {} // WebView2 initialization has not completed yet.
                    if(ready)break;
                    await Task.Delay(100);
                }
                if(!ready)throw new Exception("isolated terminal initialization timed out");
                await Task.Delay(500);
                string script;
                using(var stream=typeof(FilePanelFocusProbe).Assembly.GetManifestResourceStream("FilePanelFocus.js"))
                using(var reader=new StreamReader(stream))script=reader.ReadToEnd();
                var json=new JavaScriptSerializer();
                await window.ExecuteForTest(script.Replace("__FILE__",json.Serialize(file)).Replace("__OTHER__",json.Serialize(other)));
                bool done=false;
                for(int i=0;i<400;i++) {
                    done=await window.ExecuteForTest("window.__panelResult.done")=="true";
                    if(done)break;
                    await Task.Delay(100);
                }
                if(!done)throw new Exception("file panel regression timed out");
                string result=await window.ExecuteForTest("window.__panelResult");
                File.WriteAllText(report,result);
                window.EndUiTest(!result.Contains("\"ok\":true"));
            } catch(Exception ex) {
                File.WriteAllText(report,ex.ToString());
                window.EndUiTest(true);
            }
        }
    }
}
