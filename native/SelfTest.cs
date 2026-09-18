using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
namespace Orbit {
    internal static class SelfTest {
        public static int Run() {
            string dir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"selftest-"+Guid.NewGuid().ToString("N"));
            string report=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"self-test-results.txt");
            var log=new StringBuilder();int failed=0;
            Action<string,Action> test=(name,action)=>{try{action();log.AppendLine("PASS "+name);}catch(Exception ex){failed++;log.AppendLine("FAIL "+name+": "+ex);}};
            Directory.CreateDirectory(dir);
            test("saved session metadata fixtures",()=>SessionHistoryTest.Run(Path.Combine(dir,"session-history")));
            test("installed Codex permanently deletes an isolated fixture",()=>CodexDeleteIntegrationTest.Run(Path.Combine(dir,"codex-delete")));
            test("UTF-8/UTF-16/CP949 round trip and conflict protection",()=> {
                var json=new JavaScriptSerializer();
                foreach(var enc in new Encoding[]{new UTF8Encoding(false),new UTF8Encoding(true),Encoding.Unicode,Encoding.BigEndianUnicode,Encoding.GetEncoding(949)}) {
                    string path=Path.Combine(dir,"한글-"+enc.CodePage+"-"+enc.GetPreamble().Length+".txt");
                    File.WriteAllText(path,"첫 줄\r\nsecond line",enc);
                    var doc=json.Deserialize<System.Collections.Generic.Dictionary<string,object>>(json.Serialize(Files.Read(path)));
                    Files.Save(path,"수정\r\nline",(string)doc["encoding"],(string)doc["revision"]);
                    if(File.ReadAllText(path,enc)!="수정\r\nline")throw new Exception("Encoding mismatch");
                    bool rejected=false;try{Files.Save(path,"stale",(string)doc["encoding"],(string)doc["revision"]);}catch(InvalidOperationException){rejected=true;}
                    if(!rejected)throw new Exception("Conflict was not rejected");
                }
            });
            test("binary and large-file limits",()=> {
                string path=Path.Combine(dir,"binary.dat");File.WriteAllBytes(path,new byte[]{1,0,2,0});
                bool rejected=false;try{Files.Read(path);}catch(InvalidOperationException){rejected=true;}if(!rejected)throw new Exception("Binary accepted");
                File.WriteAllBytes(path,new byte[Files.MaxBytes+1]);rejected=false;try{Files.Read(path);}catch(InvalidOperationException){rejected=true;}if(!rejected)throw new Exception("Large file accepted");
            });
            test("Tailscale Funnel status parsing refuses unsafe setup",()=> {
                var ready=RemoteTailscale.Inspect("{\"BackendState\":\"Running\",\"Self\":{\"DNSName\":\"desktop.tail123.ts.net.\"}}");if(!ready.Running||ready.Dns!="desktop.tail123.ts.net.")throw new Exception("running status was not read");
                if(!ready.Ready)throw new Exception("valid ts.net DNS name was rejected");
                var loggedOut=RemoteTailscale.Inspect("{\"BackendState\":\"NeedsLogin\",\"Self\":{\"DNSName\":\"stale.ts.net.\"}}");if(loggedOut.Running)throw new Exception("logged out state accepted");
                if(!loggedOut.NeedsLogin)throw new Exception("NeedsLogin state was not read");
                var stopped=RemoteTailscale.Inspect("{\"BackendState\":\"Stopped\"}");if(!stopped.HasBackendState||stopped.Running||stopped.NeedsLogin)throw new Exception("Stopped state was not distinguished");
                var missingDns=RemoteTailscale.Inspect("{\"BackendState\":\"Running\"}");if(!missingDns.Running||missingDns.Ready)throw new Exception("Running state without DNS was not distinguished");
                if(RemoteTailscale.Inspect("{\"Self\":{}}").HasBackendState)throw new Exception("malformed status was accepted");
                if(RemoteTailscale.LoginUrl("open \"https://login.tailscale.com/aBc_12?next=x\" now")==null)throw new Exception("quoted login URL was not read");
                if(RemoteTailscale.Inspect("{\"BackendState\":\"Running\",\"Self\":{\"DNSName\":\"not-tailscale.example\"}}").Ready)throw new Exception("invalid DNS name accepted");
                if(!RemoteTailscale.Occupied("{\"Web\":{\"example.ts.net:443\":{}}}"))throw new Exception("existing Web mapping accepted");
                if(RemoteTailscale.Occupied("{\"Web\":{},\"TCP\":{}}"))throw new Exception("empty config rejected");
                if(!RemoteTailscale.Occupied("{\"Foreground\":\"example.ts.net\"}"))throw new Exception("foreground config accepted");
                if(!RemoteTailscale.Occupied("not JSON"))throw new Exception("malformed status accepted");
                if(!RemoteTailscale.FunnelReady("Available on the internet:\nhttps://orbit.example.ts.net/","https://orbit.example.ts.net"))throw new Exception("ready output missed");
                if(RemoteTailscale.FunnelReady("Available on the internet:\nhttps://other.ts.net","https://orbit.example.ts.net"))throw new Exception("wrong public URL accepted");
            });
            test("Tailscale loopback transport preserves auth and POST routing",()=> {
                int port=FreePort();var terminals=new TestTerminals();using(var server=new RemoteServer(terminals,Path.Combine(dir,"remote-devices.json")))using(var proxy=new LoopbackHostProxy(port)){
                    server.Start(port);
                    string browser=Raw(proxy.Port,"GET /mobile.html?v=1 HTTP/1.1\r\nHost: public-orbit.example\r\nSec-CH-UA-Mobile: ?1\r\n\r\n");
                    if(!browser.StartsWith("HTTP/1.1 200",StringComparison.Ordinal))throw new Exception("normal browser request was rejected: "+browser);
                    string unauthorized=Raw(proxy.Port,"POST /api/v1/sessions HTTP/1.1\r\nHost: public-orbit.example\r\nContent-Type: application/json\r\nContent-Length: 2\r\n\r\n{}");
                    if(!unauthorized.StartsWith("HTTP/1.1 401",StringComparison.Ordinal))throw new Exception("public Host request bypassed authentication: "+unauthorized);
                    string invite=server.CreateInvite("test");string[] parts=invite.Split('.');string body="{\"id\":\""+parts[0]+"\",\"code\":\""+parts[1]+"\",\"device\":\"phone\"}";
                    string routed=Raw(proxy.Port,"POST /api/v1/pair/request HTTP/1.1\r\nHost: public-orbit.example\r\nContent-Type: application/json\r\nContent-Length: "+Encoding.UTF8.GetByteCount(body)+"\r\n\r\n"+body);
                    if(!routed.StartsWith("HTTP/1.1 202",StringComparison.Ordinal))throw new Exception("POST body was not routed through loopback proxy: "+routed);
                    if(Raw(proxy.Port,"POST /api/v1/sessions HTTP/1.1\r\nHost: public-orbit.example\r\nContent-Length: 2\r\nContent-Length: 2\r\n\r\n{}").Length!=0)throw new Exception("duplicate Content-Length was accepted");
                    if(Raw(proxy.Port,"POST /api/v1/sessions HTTP/1.1\r\nHost: public-orbit.example\r\nTransfer-Encoding: chunked\r\n\r\n").Length!=0)throw new Exception("chunked request was accepted");
                }
            });
            test("picker directory navigation and exclusive file creation",()=> {
                string folder=Path.Combine(dir,"선택 폴더");
                string child=Path.Combine(folder,"하위 폴더");
                Directory.CreateDirectory(child);
                string path=Path.Combine(folder,"existing.txt");
                File.WriteAllText(path,"preserve me",new UTF8Encoding(false));
                var json=new JavaScriptSerializer();
                var listing=json.Deserialize<System.Collections.Generic.Dictionary<string,object>>(json.Serialize(Files.Browse(folder,false,true)));
                var entries=(System.Collections.IList)listing["entries"];
                if((string)listing["path"]!=folder || (string)listing["parent"]!=dir || entries.Count!=1)throw new Exception("Directory-only navigation result is incorrect");
                bool rejected=false;
                try { Files.CreateFile(path); }catch(IOException) { rejected=true; }
                if(!rejected || File.ReadAllText(path)!="preserve me")throw new Exception("Existing file was overwritten");
                string created=Path.Combine(folder,"새 파일.md");
                Files.CreateFile(created);
                if(!File.Exists(created) || new FileInfo(created).Length!=0)throw new Exception("New file was not empty");
                rejected=false;
                try { Files.Browse(Path.Combine(folder,"missing"),false,false); }catch(DirectoryNotFoundException) { rejected=true; }
                if(!rejected)throw new Exception("Missing folder was accepted");
            });
            test("explicit executable link launches through the Windows shell",()=> {
                string source=Path.Combine(dir,"external-launch-fixture.cs"),exe=Path.Combine(dir,"external-launch-fixture.exe"),marker=Path.Combine(dir,"external-launch-marker.txt");
                File.WriteAllText(source,"using System;using System.IO;class Fixture{static void Main(){File.WriteAllText(Environment.GetEnvironmentVariable(\"ORBIT_EXTERNAL_LAUNCH_MARKER\"),\"launched\");}}",new UTF8Encoding(false));
                string csc=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"Microsoft.NET","Framework64","v4.0.30319","csc.exe");
                using(var compiler=Process.Start(new ProcessStartInfo(csc,"/nologo /target:exe /out:\""+exe+"\" \""+source+"\""){UseShellExecute=false,CreateNoWindow=true})) {compiler.WaitForExit();if(compiler.ExitCode!=0)throw new Exception("fixture compiler failed");}
                string previous=Environment.GetEnvironmentVariable("ORBIT_EXTERNAL_LAUNCH_MARKER");
                try {Environment.SetEnvironmentVariable("ORBIT_EXTERNAL_LAUNCH_MARKER",marker);MainWindow.OpenExternal(exe,dir);for(int i=0;i<100&&!File.Exists(marker);i++)Thread.Sleep(50);if(!File.Exists(marker)||File.ReadAllText(marker)!="launched")throw new Exception("executable link did not launch fixture");}
                finally {Environment.SetEnvironmentVariable("ORBIT_EXTERNAL_LAUNCH_MARKER",previous);}
            });
            test("ConPTY interactive input, resize, output and exit",()=> {
                var output=new StringBuilder();var done=new ManualResetEvent(false);var marker=new ManualResetEvent(false);ConPty p=null;
                p=new ConPty("test",dir,"\""+Environment.GetEnvironmentVariable("ComSpec")+"\" /d /q",80,24,(id,text)=>{lock(output){output.Append(text);if(output.ToString().Contains("ORBIT_PTY_OK"))marker.Set();}p.Acknowledge();},(id,code)=>done.Set());
                try {p.Start();p.Resize(110,32);p.Write("echo ORBIT_PTY_OK\r\n");if(!marker.WaitOne(10000))throw new Exception("No terminal output: "+output);p.Write("exit\r\n");if(!done.WaitOne(10000))throw new Exception("Process exit did not finish");}
                finally {p.Dispose();}
            });
            File.WriteAllText(report,log.ToString());Directory.Delete(dir,true);return failed==0?0:1;
        }
        static int FreePort(){var l=new TcpListener(IPAddress.Loopback,0);l.Start();int port=((IPEndPoint)l.LocalEndpoint).Port;l.Stop();return port;}
        static string Raw(int port,string request){try{using(var c=new TcpClient()){c.ReceiveTimeout=5000;c.SendTimeout=5000;c.Connect(IPAddress.Loopback,port);using(var s=c.GetStream()){byte[] bytes=Encoding.UTF8.GetBytes(request);s.Write(bytes,0,bytes.Length);s.Flush();c.Client.Shutdown(SocketShutdown.Send);using(var reader=new StreamReader(s,Encoding.UTF8))return reader.ReadToEnd();}}}catch(IOException){return "";}}
        sealed class TestTerminals:IRemoteTerminals {public object Sessions(){return new {sessions=new object[0]};}public object SavedSessions(){return new {sessions=new object[0]};}public object DeleteSavedSession(string provider,string id){return new {sessions=new object[0]};}public object Snapshot(string id){return new { };}public object Output(string id,long after,int timeout){return new { };}public object Create(string profile,string resumeId,string project=null){return new {session="test"};}public object Projects(){return new {projects=new object[0],current=""};}public void Input(string id,string data){}}
    }
}
