using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using System.Collections.Generic;
using Microsoft.Web.WebView2.WinForms;

// Reflect the exact built Orbit.exe; no production source is recompiled here.
class TerminalRecoveryProbe {
    static Type type;static Form window;
    [STAThread] static void Main(string[] args) {
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        type=Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Orbit.exe")).GetType("Orbit.MainWindow");
        window=(Form)Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,new object[]{new[]{"--remote-test",args[0]}},null);
        window.Opacity=0;window.ShowInTaskbar=false;window.Shown+=async delegate {await Run();};Application.Run(window);
    }
    static Task<string> Exec(string js) {return (Task<string>)type.GetMethod("ExecuteForTest",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,new object[]{js});}
    static async Task Run() {
        string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","results");Directory.CreateDirectory(root);
        try {
            bool ready=false;
            for(int i=0;i<180;i++){try{ready=await Exec("Boolean(window.orbitUiTest && window.orbitDiagnostics().sessions[0]?.output.includes('ORBIT_REMOTE_TEST_READY'))")=="true";}catch{}if(ready)break;await Task.Delay(100);}
            if(!ready)throw new Exception("isolated built Orbit did not initialize");
            await Exec("document.activeElement.dispatchEvent(new KeyboardEvent('keydown',{key:'Enter',code:'Enter',keyCode:13,which:13,bubbles:true,cancelable:true}));");await Task.Delay(300);
            var json=new JavaScriptSerializer();
            string rect=await Exec("JSON.stringify((()=>{const r=document.querySelector('.terminal-tab-name').getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2}})())");
            var point=json.Deserialize<Dictionary<string,object>>(json.Deserialize<string>(rect));
            var web=(WebView2)type.GetField("web",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);
            foreach(string action in new[]{"mousePressed","mouseReleased"})await web.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",json.Serialize(new{type=action,x=point["x"],y=point["y"],button="left",clickCount=1}));
            await Task.Delay(250);
            string script;using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("TerminalRecovery.js"))using(var reader=new StreamReader(stream))script=reader.ReadToEnd();
            string fixture=Path.GetFullPath(Path.Combine(root,"editor-fixture.txt"));File.WriteAllText(fixture,"focus fixture\n");
            script=script.Replace("__FIXTURE__",json.Serialize(fixture));
            await Exec(script);
            bool done=false;for(int i=0;i<450;i++){done=await Exec("window.__recoveryResult?.done===true")=="true";if(done)break;await Task.Delay(100);}
            if(!done)throw new Exception("built UI recovery regression timed out");
            string result=await Exec("window.__recoveryResult");File.WriteAllText(Path.Combine(root,"terminal-recovery-results.json"),result);
            if(!result.Contains("\"ok\":true"))Environment.ExitCode=1;
            var deliveries=type.GetField("terminalDeliveries",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);
            int count=(int)deliveries.GetType().GetProperty("Count").GetValue(deliveries,null);
            if(count!=0)throw new Exception("closed terminal retained pending output");
        }catch(Exception error){File.WriteAllText(Path.Combine(root,"error.txt"),error.ToString());Environment.ExitCode=1;}
        type.GetMethod("EndUiTest",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,new object[]{Environment.ExitCode!=0});
    }
}
