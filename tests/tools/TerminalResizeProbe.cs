using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Web.Script.Serialization;

// Reflect exact packaged Windows bits, with own test data root/processes only.
class TerminalResizeProbe {
    sealed class FailingWriteStream : FileStream {
        internal FailingWriteStream(string path):base(path,FileMode.Create,FileAccess.Write,FileShare.ReadWrite){}
        public override void Write(byte[] bytes,int offset,int count) {throw new IOException("Injected input transport fault",unchecked((int)0x800700E8));}
    }
    static Type type;static Form window;static string root;static System.Threading.Timer deadline;
    static readonly BindingFlags fields=BindingFlags.Instance|BindingFlags.NonPublic;
    static readonly JavaScriptSerializer json=new JavaScriptSerializer();
    [STAThread]static void Main(string[] args) {
        root=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","results"));Directory.CreateDirectory(root);
        deadline=new System.Threading.Timer(_=>{File.WriteAllText(Path.Combine(root,"resize-error.txt"),"Own probe exceeded 60s deadline");Environment.Exit(2);},null,60000,Timeout.Infinite);
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        type=Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Orbit.exe")).GetType("Orbit.MainWindow");
        window=(Form)Activator.CreateInstance(type,fields|BindingFlags.Public,null,new object[]{new[]{"--remote-test",args[0]}},null);
        window.Opacity=0;window.ShowInTaskbar=false;window.Shown+=async delegate{await Run();};Application.Run(window);
    }
    static async Task<string> Exec(string script) {
        var task=(Task<string>)type.GetMethod("ExecuteForTest",fields).Invoke(window,new object[]{script});
        if(await Task.WhenAny(task,Task.Delay(8000))!=task)throw new Exception("UI unresponsive");return await task;
    }
    static Task<object> Dispatch(string method,Dictionary<string,object> args) {return (Task<object>)type.GetMethod("Dispatch",fields).Invoke(window,new object[]{method,args,null});}
    static int Field(object terminal,string name) {return (int)terminal.GetType().GetField(name,fields).GetValue(terminal);}
    static async Task Run() {
        var result=new Dictionary<string,object>();
        try {
            bool ready=false;for(int i=0;i<180;i++){try{ready=await Exec("Boolean(window.orbitDiagnostics?.().sessions[0]?.output.includes('ORBIT_REMOTE_TEST_READY'))")=="true";}catch(TargetInvocationException){}if(ready)break;await Task.Delay(100);}
            if(!ready)throw new Exception("UI did not initialize");
            await Exec("chrome.webview.postMessage({id:0,method:'write',args:{session:window.orbitDiagnostics().sessions[0].id,data:'\\r'}})");await Task.Delay(100);
            string id=json.Deserialize<string>(await Exec("window.orbitDiagnostics().sessions[0].id"));
            var sessions=type.GetField("sessions",fields).GetValue(window);var terminal=sessions.GetType().GetProperty("Item").GetValue(sessions,new object[]{id});
            int pid=(int)terminal.GetType().GetField("ProcessId").GetValue(terminal);result["pid"]=pid;
            await Exec("const p=document.querySelector('.terminal-pane');p.style.flex='none';p.style.width='20px';p.style.height='20px';");await Task.Delay(250);
            if(await Exec("window.orbitDiagnostics().sessions[0].cols===10&&window.orbitDiagnostics().sessions[0].rows===3")!="true")throw new Exception("tiny xterm grid disagrees with Windows bounds");
            if(Field(terminal,"appliedCols")!=10||Field(terminal,"appliedRows")!=3)throw new Exception("native tiny grid disagrees");
            result["tinyGrid"]=new[]{10,3};
            await Exec("document.querySelector('.terminal-pane').removeAttribute('style');window.__resizePost=chrome.webview.postMessage.bind(chrome.webview);window.__heldAck=null;chrome.webview.postMessage=m=>{if(m.method==='ack'){window.__heldAck=m;return;}window.__resizePost(m)};window.__resizePost({id:0,method:'write',args:{session:window.orbitDiagnostics().sessions[0].id,data:'for /l %i in (1,1,1000) do @echo UI_RESIZE_BACKPRESSURE_%i\\r'}});");
            await Task.Delay(500);
            bool blocked=false;var timer=Stopwatch.StartNew();
            for(int i=0;i<1500;i++) {
                int cols=i%2==0?500:10,rows=i%2==0?300:3;
                await Dispatch("resize",new Dictionary<string,object>{{"session",id},{"cols",cols},{"rows",rows}});
                if(!blocked) {var wait=Stopwatch.StartNew();while(Field(terminal,"appliedCols")!=cols||Field(terminal,"appliedRows")!=rows){if(wait.ElapsedMilliseconds>100){blocked=true;break;}await Task.Delay(1);}}
            }
            await Dispatch("resize",new Dictionary<string,object>{{"session",id},{"cols",123},{"rows",37}});
            result["resizeRequests"]=1501;result["requestMs"]=timer.ElapsedMilliseconds;result["nativeBlockedWhileAckPaused"]=blocked;
            if(await Exec("Boolean(window.__heldAck&&window.orbitDiagnostics().sessions[0].pid==="+pid+")")!="true")throw new Exception("UI ACK handler unavailable under resize stress");
            result["uiResponsiveDuringPause"]=true;
            await Exec("chrome.webview.postMessage=window.__resizePost;window.__resizePost(window.__heldAck);");
            bool final=false;for(int i=0;i<200;i++){if(Field(terminal,"appliedCols")==123&&Field(terminal,"appliedRows")==37){final=true;break;}await Task.Delay(25);}
            if(!final)throw new Exception("latest resize was not applied after ACK");
            result["latestCoalescedGrid"]=new[]{123,37};
            // The stress sends native sizes directly. Restore the actual xterm grid
            // before judging displayed output/input, as the normal fit path does.
            var grid=json.Deserialize<Dictionary<string,int>>(await Exec("({cols:window.orbitDiagnostics().sessions[0].cols,rows:window.orbitDiagnostics().sessions[0].rows})"));
            await Dispatch("resize",new Dictionary<string,object>{{"session",id},{"cols",grid["cols"]},{"rows",grid["rows"]}});
            for(int i=0;i<200;i++){if(Field(terminal,"appliedCols")==grid["cols"]&&Field(terminal,"appliedRows")==grid["rows"])break;await Task.Delay(25);}
            bool flood=false;for(int i=0;i<400;i++){flood=await Exec("window.orbitDiagnostics().sessions[0].output.includes('UI_RESIZE_BACKPRESSURE_1000')")=="true";if(flood)break;await Task.Delay(25);}
            if(!flood)throw new Exception("flood did not finish after resize ACK recovery");
            await Exec("window.__resizePost({id:0,method:'write',args:{session:window.orbitDiagnostics().sessions[0].id,data:'echo RESIZE_INPUT_RECOVERED\\r'}})");
            bool input=false;for(int i=0;i<200;i++){input=await Exec("window.orbitDiagnostics().sessions[0].output.split('\\n').some(l=>l.trim()==='RESIZE_INPUT_RECOVERED')")=="true";if(input)break;await Task.Delay(25);}
            if(!input||Process.GetProcessById(pid).HasExited) {result["syntheticOutputTail"]=await Exec("window.orbitDiagnostics().sessions[0].output.split('\\n').slice(-12)");throw new Exception("same PID/input did not survive resize pause");}
            result["samePidInputRecovered"]=true;result["finalNativeGrid"]=new[]{grid["cols"],grid["rows"]};
            var originalWriter=(FileStream)terminal.GetType().GetField("writer",fields).GetValue(terminal);
            terminal.GetType().GetField("writer",fields).SetValue(terminal,new FailingWriteStream(Path.Combine(root,"input-fault-sink.bin")));
            await Dispatch("write",new Dictionary<string,object>{{"session",id},{"data","echo OWN_WRITER_FAULT\r"}});
            bool notice=false;for(int i=0;i<100;i++){notice=await Exec("Boolean(document.querySelector('.terminal-fault')?.textContent.includes('프로세스는 실행 중입니다.'))&&!window.orbitDiagnostics().sessions[0].exited")=="true";if(notice)break;await Task.Delay(25);}
            if(!notice||Process.GetProcessById(pid).HasExited)throw new Exception("writer fault killed shell or lacked accurate UI state");
            bool rejected=false;try{await Dispatch("write",new Dictionary<string,object>{{"session",id},{"data","rejected"}});}catch(InvalidOperationException){rejected=true;}
            if(!rejected||Field(terminal,"queuedBytes")!=0)throw new Exception("writer fault accepted/retained input");
            result["writerFaultPreservedPid"]=true;result["disconnectedUi"]=true;result["deadWriterRejectsInput"]=true;
            byte[] exitBytes=System.Text.Encoding.UTF8.GetBytes("exit 17\r");originalWriter.Write(exitBytes,0,exitBytes.Length);originalWriter.Flush();
            bool exited=false;for(int i=0;i<200;i++){exited=await Exec("window.orbitDiagnostics().sessions[0].exited&&!document.querySelector('.terminal-fault')")=="true";if(exited)break;await Task.Delay(25);}
            if(!exited)throw new Exception("genuine exit left a misleading running-process notice");
            result["genuineExitUpdatesUi"]=true;
            await Exec("window.orbitUiTest.closeAll()");await Task.Delay(250);
            try{originalWriter.Dispose();}catch(Exception){}
            result["ok"]=true;File.WriteAllText(Path.Combine(root,"resize-recovery-results.json"),json.Serialize(result));
        }catch(Exception error){result["ok"]=false;result["error"]=error.ToString();File.WriteAllText(Path.Combine(root,"resize-recovery-results.json"),json.Serialize(result));Environment.ExitCode=1;}
        deadline.Dispose();type.GetMethod("EndUiTest",fields).Invoke(window,new object[]{Environment.ExitCode!=0});
    }
}
