using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
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
            test("update check accepts only newer releases hosted in the Orbit repository",()=> {
                var current=new Version(0,1,0,0);
                if(!Updater.IsNewer(Updater.ParseTag("v0.2.0"),current))throw new Exception("newer tag was not detected");
                if(Updater.IsNewer(Updater.ParseTag("v0.1.0"),current))throw new Exception("same version counted as an update");
                if(Updater.IsNewer(Updater.ParseTag("v0.0.9"),current))throw new Exception("older version counted as an update");
                if(Updater.ParseTag("latest")!=null||Updater.ParseTag("v1.2")!=null||Updater.ParseTag("")!=null)throw new Exception("malformed tag was accepted");
                string asset="{\"name\":\"Orbit-Setup.exe\",\"browser_download_url\":\"https://github.com/LuckyMan0277/orbit/releases/download/v0.2.0/Orbit-Setup.exe\",\"digest\":\"sha256:ABC123\"}";
                var release=Updater.Parse("{\"tag_name\":\"v0.2.0\",\"body\":\"notes\",\"assets\":["+asset+"]}");
                if(release==null||release.Sha256!="abc123"||release.Version.ToString(3)!="0.2.0")throw new Exception("valid release was not parsed");
                if(Updater.Parse("{\"tag_name\":\"v0.2.0\",\"assets\":["+asset.Replace("https://github.com/LuckyMan0277/orbit/","https://evil.example/")+"]}")!=null)throw new Exception("foreign download host was accepted");
                if(Updater.Parse("{\"tag_name\":\"v0.2.0\",\"draft\":true,\"assets\":["+asset+"]}")!=null)throw new Exception("draft release was accepted");
                if(Updater.Parse("{\"tag_name\":\"v0.2.0\",\"assets\":[]}")!=null)throw new Exception("release without installer was accepted");
            });
            test("Tailscale loopback transport preserves auth and POST routing",()=> {
                int port=FreePort();var terminals=new TestTerminals();using(var server=new RemoteServer(terminals,Path.Combine(dir,"remote-devices.json")))using(var proxy=new LoopbackHostProxy(port)){
                    server.Start(port);
                    string browser=Raw(proxy.Port,"GET /mobile.html?v=1 HTTP/1.1\r\nHost: public-orbit.example\r\nSec-CH-UA-Mobile: ?1\r\n\r\n");
                    if(!browser.StartsWith("HTTP/1.1 200",StringComparison.Ordinal))throw new Exception("normal browser request was rejected: "+browser);
                    string unauthorized=Raw(proxy.Port,"POST /api/v1/sessions HTTP/1.1\r\nHost: public-orbit.example\r\nContent-Type: application/json\r\nContent-Length: 2\r\n\r\n{}");
                    if(!unauthorized.StartsWith("HTTP/1.1 401",StringComparison.Ordinal))throw new Exception("public Host request bypassed authentication: "+unauthorized);
                    string token=server.IssueAccountToken("test");string body="{}";
                    string routed=Raw(proxy.Port,"POST /api/v1/sessions HTTP/1.1\r\nHost: public-orbit.example\r\nAuthorization: Bearer "+token+"\r\nContent-Type: application/json\r\nContent-Length: "+Encoding.UTF8.GetByteCount(body)+"\r\n\r\n"+body);
                    if(!routed.StartsWith("HTTP/1.1 200",StringComparison.Ordinal))throw new Exception("authenticated POST body was not routed through loopback proxy: "+routed);
                    if(Raw(proxy.Port,"POST /api/v1/sessions HTTP/1.1\r\nHost: public-orbit.example\r\nContent-Length: 2\r\nContent-Length: 2\r\n\r\n{}").Length!=0)throw new Exception("duplicate Content-Length was accepted");
                    if(Raw(proxy.Port,"POST /api/v1/sessions HTTP/1.1\r\nHost: public-orbit.example\r\nTransfer-Encoding: chunked\r\n\r\n").Length!=0)throw new Exception("chunked request was accepted");
                    // A phone upload larger than the proxy's normal body limit is streamed through and saved, git-ignored, in the session folder.
                    terminals.Folder=Path.Combine(dir,"upload project");Directory.CreateDirectory(terminals.Folder);
                    Func<string,string,string,string> upload=(path,auth,data)=>Raw(proxy.Port,"POST "+path+" HTTP/1.1\r\nHost: public-orbit.example\r\nAuthorization: Bearer "+auth+"\r\nContent-Type: application/octet-stream\r\nX-Orbit-Session: s1\r\nX-Orbit-Name: "+Uri.EscapeDataString("..\\사진 1.jpg")+"\r\nContent-Length: "+data.Length+"\r\n\r\n"+data);
                    string payload=new string('x',200000),uploaded=upload("/api/v1/upload",token,payload);
                    if(!uploaded.StartsWith("HTTP/1.1 200",StringComparison.Ordinal))throw new Exception("upload was not accepted: "+uploaded.Substring(0,Math.Min(300,uploaded.Length)));
                    string saved=Directory.GetFiles(Path.Combine(terminals.Folder,".orbit","uploads"),"*사진 1.jpg")[0];
                    if(File.ReadAllText(saved)!=payload)throw new Exception("uploaded file content differs");
                    if(File.ReadAllText(Path.Combine(terminals.Folder,".orbit","uploads",".gitignore"))!="*\n")throw new Exception("upload folder is not git-ignored");
                    if(!upload("/api/v1/upload","wrong","small").StartsWith("HTTP/1.1 401",StringComparison.Ordinal))throw new Exception("unauthenticated upload was accepted");
                    terminals.Folder=null;if(!upload("/api/v1/upload",token,"small").StartsWith("HTTP/1.1 400",StringComparison.Ordinal))throw new Exception("upload to an unknown session was accepted");
                    if(upload("/api/v1/sessions",token,payload).Length!=0)throw new Exception("large body was accepted outside upload");
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
            test("secret vault encrypts values, keeps them per project and hands them out only by name",()=> {
                string path=Path.Combine(dir,"secrets.dat"),value="sk-test-Secret-Value-123",projA=Path.Combine(dir,"project-a"),projB=Path.Combine(dir,"project-b"),subA=Path.Combine(projA,"src");
                var vault=new SecretVault(path);
                vault.Set("Test_Key","  "+value+"\r\n","global",null);
                byte[] disk=File.ReadAllBytes(path);
                if(Encoding.UTF8.GetString(disk).Contains(value)||Encoding.Unicode.GetString(disk).Contains(value)||Encoding.UTF8.GetString(disk).Contains("Test_Key"))throw new Exception("secret is stored in plain text");
                var reloaded=new SecretVault(path);
                if(String.Join(",",reloaded.Names(projA))!="Test_Key")throw new Exception("name was not persisted");
                if(reloaded.Env(projA,new[]{"test_key"})["TEST_KEY"]!=value)throw new Exception("value was not restored, trimmed and matched case-insensitively");
                // two projects at once: the same name holds a different value in each, and a key of one project never reaches the other
                vault.Set("SHARED_KEY","all-projects-value","global",null);vault.Set("SHARED_KEY","project-a-value","project",projA);vault.Set("ONLY_B","b-value","project",projB);
                if(vault.Env(projA,null)["SHARED_KEY"]!="project-a-value")throw new Exception("the project's own key must win over the all-projects one");
                if(vault.Env(subA,null)["SHARED_KEY"]!="project-a-value")throw new Exception("a sub-folder of a project must belong to that project");
                if(vault.Env(projB,null)["SHARED_KEY"]!="all-projects-value"||vault.Env(projB,null)["ONLY_B"]!="b-value")throw new Exception("project B did not get the shared key plus its own");
                if(vault.Env(projA,null).ContainsKey("ONLY_B")||vault.Env(null,null).ContainsKey("ONLY_B")||vault.Env(Path.Combine(dir,"project-a-other"),null).ContainsKey("SHARED_KEY")&&vault.Env(Path.Combine(dir,"project-a-other"),null)["SHARED_KEY"]!="all-projects-value")throw new Exception("a key leaked into another project");
                if(!vault.Has("only_b",projB)||vault.Has("ONLY_B",projA))throw new Exception("Has() ignores the project");
                if(vault.List(projA).Length!=3)throw new Exception("list for a project should be its own keys plus the shared ones: "+new JavaScriptSerializer().Serialize(vault.List(projA)));
                var again=new SecretVault(path);
                if(again.Env(projA,null)["SHARED_KEY"]!="project-a-value"||again.Env(projB,null)["ONLY_B"]!="b-value")throw new Exception("project keys were not persisted");
                if(!again.Delete("SHARED_KEY","project",projA)||again.Env(projA,null)["SHARED_KEY"]!="all-projects-value")throw new Exception("deleting a project key must fall back to the shared one");
                foreach(string bad in new[]{"","1KEY","A B","A-B","PATH","comspec","ORBIT_ANY",new string('A',65)}) { bool rejected=false;try{vault.Set(bad,"x","global",null);}catch(InvalidOperationException){rejected=true;}if(!rejected)throw new Exception("invalid name accepted: "+bad); }
                bool empty=false;try{vault.Set("EMPTY_KEY","  ","global",null);}catch(InvalidOperationException){empty=true;}if(!empty)throw new Exception("empty value accepted");
                bool noProject=false;try{vault.Set("X_KEY","x","project","");}catch(InvalidOperationException){noProject=true;}if(!noProject)throw new Exception("a project key needs a project");
                bool unknown=false;try{vault.Env(projA,new[]{"ONLY_B"});}catch(InvalidOperationException ex){unknown=!ex.Message.Contains(value);}if(!unknown)throw new Exception("a name from another project did not fail");
                // the first version stored one flat list: it must come back as keys for all projects
                string legacy=Path.Combine(dir,"legacy-secrets.dat");
                File.WriteAllBytes(legacy,System.Security.Cryptography.ProtectedData.Protect(Encoding.UTF8.GetBytes("{\"OLD_KEY\":\"old-value\"}"),Encoding.UTF8.GetBytes("Orbit.Secrets.v1"),System.Security.Cryptography.DataProtectionScope.CurrentUser));
                var old=new SecretVault(legacy);if(old.Env(projA,null)["OLD_KEY"]!="old-value"||old.Env(projB,null)["OLD_KEY"]!="old-value")throw new Exception("legacy flat secrets were not migrated to all projects");
                old.Set("NEW_KEY","n","project",projA);if(new SecretVault(legacy).Env(projB,null).ContainsKey("NEW_KEY")||new SecretVault(legacy).Env(projA,null)["OLD_KEY"]!="old-value")throw new Exception("saving after migration lost data");
                if(!vault.Delete("test_key","global",null)||Array.IndexOf(vault.Names(projB),"Test_Key")>=0)throw new Exception("delete failed");
            });
            test("ConPTY passes secrets only through the child's environment",()=> {
                string probe="ORBIT_SECRET_PROBE",value="probe-value-123",root=Environment.GetEnvironmentVariable("SystemRoot");
                var output=new StringBuilder();var marker=new ManualResetEvent(false);var done=new ManualResetEvent(false);ConPty p=null;string expected="["+value+"]["+root+"]";
                p=new ConPty("secret-test",dir,"\""+Environment.GetEnvironmentVariable("ComSpec")+"\" /d /q",120,24,(id,text)=>{lock(output){output.Append(text);if(output.ToString().Contains(expected))marker.Set();}p.Acknowledge();},(id,code)=>done.Set(),new System.Collections.Generic.Dictionary<string,string>{{probe,value}});
                try {p.Start();p.Write("echo [%"+probe+"%][%SystemRoot%]\r\n");if(!marker.WaitOne(10000))throw new Exception("child did not see the secret next to its normal environment: "+output);p.Write("exit\r\n");if(!done.WaitOne(10000))throw new Exception("Process exit did not finish");}
                finally {p.Dispose();}
                if(Environment.GetEnvironmentVariable(probe)!=null)throw new Exception("secret leaked into the host's own environment");
            });
            test("only Claude and Codex terminals receive stored keys on their own",()=> {
                foreach(string ai in new[]{"claude","codex"})if(!AgentNote.GetsKeys(ai))throw new Exception(ai+" must receive the project's keys");
                foreach(string plain in new[]{"powershell","cmd","","unknown"})if(AgentNote.GetsKeys(plain))throw new Exception(plain+" must not receive keys automatically");
            });
            test("AI start-up guides name the stored keys and stay safe as command-line arguments",()=> {
                var names=new[]{"OPENAI_API_KEY","STRIPE_KEY"};string codex=AgentNote.Codex(names),claude=AgentNote.Claude(names),none=AgentNote.Codex(new string[0]);
                if(!codex.Contains("OPENAI_API_KEY, STRIPE_KEY")||!claude.Contains("OPENAI_API_KEY, STRIPE_KEY")||!none.Contains("none yet"))throw new Exception("guide does not list the stored names");
                foreach(string tool in new[]{"orbit-secret run --","orbit-secret request NAME","orbit-secret list"})if(!codex.Contains(tool)||!claude.Contains(tool))throw new Exception("guide misses "+tool);
                // Codex receives it as one argument: a quote, %, &, |, <, >, ^, $, backtick or newline would be mangled by PowerShell or a cmd shim.
                foreach(char bad in "\"'%&|<>^$`\r\n")if(codex.IndexOf(bad)>=0)throw new Exception("Codex guide contains an argument-unsafe character: "+(int)bad);
                if(codex.Length>4000)throw new Exception("Codex guide is too long for a config override");
            });
            test("secret broker serves registered terminals and never carries a requested value",()=> {
                string valueA="existing-value-1",valueB="requested-value-2";var vault=new SecretVault(Path.Combine(dir,"broker-secrets.dat"));vault.Set("EXISTING_KEY",valueA,"global",null);string projA=Path.Combine(dir,"proj-a"),projB=Path.Combine(dir,"proj-b");
                var seen=new List<SecretRequest>();var closedIds=new List<string>();var broker=new SecretBroker(vault,r=>{lock(seen)seen.Add(r);},TimeSpan.FromSeconds(30),r=>{lock(closedIds)closedIds.Add(r.Id);});
                string token=broker.Register("term-1",projA),other=broker.Register("term-2",projB),session="term-1";
                var json=new JavaScriptSerializer();
                Func<string,Dictionary<string,object>,int,List<Dictionary<string,object>>> talk=(tok,payload,count)=> {
                    payload["token"]=tok;
                    using(var c=new NamedPipeClientStream(".",broker.PipeName,PipeDirection.InOut)) {
                        c.Connect(5000);var w=new StreamWriter(c,new UTF8Encoding(false)){AutoFlush=true};var r=new StreamReader(c,new UTF8Encoding(false));
                        w.WriteLine(json.Serialize(payload));var replies=new List<Dictionary<string,object>>();
                        for(int i=0;i<count;i++){string line=r.ReadLine();if(line==null)break;replies.Add(json.Deserialize<Dictionary<string,object>>(line));}
                        return replies;
                    }
                };
                if(!talk("not-a-token",new Dictionary<string,object>{{"op","list"}},1)[0].ContainsKey("error"))throw new Exception("unregistered token was accepted");
                var listed=talk(token,new Dictionary<string,object>{{"op","list"}},1)[0];
                if(json.Serialize(listed).Contains(valueA)||!json.Serialize(listed).Contains("EXISTING_KEY"))throw new Exception("list must return names only: "+json.Serialize(listed));
                var env=(Dictionary<string,object>)talk(token,new Dictionary<string,object>{{"op","env"},{"names",new[]{"EXISTING_KEY"}}},1)[0]["env"];
                if((string)env["EXISTING_KEY"]!=valueA)throw new Exception("env did not return the stored value for run");
                if(!talk(token,new Dictionary<string,object>{{"op","env"},{"names",new[]{"NO_SUCH_KEY"}}},1)[0].ContainsKey("error"))throw new Exception("unknown name in env was not refused");
                if((string)talk(token,new Dictionary<string,object>{{"op","request"},{"name","EXISTING_KEY"}},1)[0]["status"]!="exists")throw new Exception("requesting a stored name should report it exists");
                // a request stays pending until the UI answers, and the answer never includes the value
                var asked=System.Threading.Tasks.Task.Run(()=>talk(token,new Dictionary<string,object>{{"op","request"},{"name","REQUESTED_KEY"},{"reason","for a test"}},2));
                for(int i=0;i<100&&seen.Count==0;i++)Thread.Sleep(50);
                if(seen.Count!=1||seen[0].Name!="REQUESTED_KEY"||seen[0].Reason!="for a test")throw new Exception("request was not announced to the UI");
                string pendingJson=json.Serialize(broker.Pending(session));if(!pendingJson.Contains("REQUESTED_KEY")||broker.Pending("term-2") is object[]&&((object[])broker.Pending("term-2")).Length!=0)throw new Exception("pending list is wrong: "+pendingJson);
                if(seen[0].Project!=projA)throw new Exception("the request does not carry the project of the terminal that asked");
                vault.Set("REQUESTED_KEY",valueB,"project",projA);
                if(!broker.Answer(seen[0].Id,"saved"))throw new Exception("answer was not accepted");
                lock(closedIds)if(!closedIds.Contains(seen[0].Id))throw new Exception("the closed callback did not fire for an answered request");
                var askedReplies=asked.Result;if(askedReplies.Count!=2||(string)askedReplies[0]["status"]!="pending"||(string)askedReplies[1]["status"]!="saved"||json.Serialize(askedReplies).Contains(valueB))throw new Exception("request replies are wrong or carry the value: "+json.Serialize(askedReplies));
                if(broker.Answer(seen[0].Id,"saved"))throw new Exception("a request was answered twice");
                // another project's terminal, open at the same time, must not see the key that project A's terminal just stored
                if(json.Serialize(talk(other,new Dictionary<string,object>{{"op","list"}},1)[0]).Contains("REQUESTED_KEY"))throw new Exception("a key stored for project A is listed in project B's terminal");
                if(!talk(other,new Dictionary<string,object>{{"op","env"},{"names",new[]{"REQUESTED_KEY"}}},1)[0].ContainsKey("error"))throw new Exception("a key stored for project A was handed to project B's terminal");
                if(!json.Serialize(talk(token,new Dictionary<string,object>{{"op","list"}},1)[0]).Contains("\"project\""))throw new Exception("list does not tag project-only keys");
                // "saved" without the value in the vault counts as denied
                seen.Clear();var ghost=System.Threading.Tasks.Task.Run(()=>talk(token,new Dictionary<string,object>{{"op","request"},{"name","GHOST_KEY"}},2));
                for(int i=0;i<100&&seen.Count==0;i++)Thread.Sleep(50);broker.Answer(seen[0].Id,"saved");
                if((string)ghost.Result[1]["status"]!="denied")throw new Exception("saved without a stored value was not treated as denied");
                // releasing a terminal closes its token and its waiting prompts
                seen.Clear();var closing=System.Threading.Tasks.Task.Run(()=>talk(other,new Dictionary<string,object>{{"op","request"},{"name","LATE_KEY"}},2));
                for(int i=0;i<100&&seen.Count==0;i++)Thread.Sleep(50);broker.Release("term-2");
                if((string)closing.Result[1]["status"]!="closed"||!talk(other,new Dictionary<string,object>{{"op","list"}},1)[0].ContainsKey("error"))throw new Exception("released terminal kept its token or prompt");
                // the real command-line tool, when this build ships it
                string tool=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"bin","orbit-secret.exe");
                if(File.Exists(tool)) {
                    Func<string,Process> start=arguments=> {
                        var psi=new ProcessStartInfo(tool,arguments){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
                        psi.EnvironmentVariables["ORBIT_PIPE"]=broker.PipeName;psi.EnvironmentVariables["ORBIT_TERMINAL_AUTH"]=token;return Process.Start(psi);
                    };
                    Func<string,string[]> once=arguments=>{using(var p=start(arguments)){string o=p.StandardOutput.ReadToEnd(),e=p.StandardError.ReadToEnd();if(!p.WaitForExit(20000))throw new Exception("orbit-secret hung: "+arguments);return new[]{o,e,p.ExitCode.ToString()};}};
                    var listOut=once("list");if(!listOut[0].Contains("EXISTING_KEY")||listOut[0].Contains(valueA))throw new Exception("orbit-secret list output is wrong: "+listOut[0]);
                    var runOut=once("run -- cmd /c echo [%EXISTING_KEY%]");if(!runOut[0].Contains("["+valueA+"]"))throw new Exception("orbit-secret run did not give the child the secret: "+runOut[0]+runOut[1]);
                    if(Environment.GetEnvironmentVariable("EXISTING_KEY")!=null)throw new Exception("secret leaked into the test process");
                    var codeOut=once("run -- cmd /c exit 7");if(codeOut[2]!="7")throw new Exception("orbit-secret run did not pass on the exit code: "+codeOut[2]);
                    var onlyOut=once("run --only EXISTING_KEY -- cmd /c echo [%EXISTING_KEY%]");if(!onlyOut[0].Contains("["+valueA+"]"))throw new Exception("run --only failed: "+onlyOut[0]+onlyOut[1]);
                    foreach(bool save in new[]{true,false}) {
                        seen.Clear();string name=save?"CLI_SAVED_KEY":"CLI_DENIED_KEY";
                        using(var p=start("request "+name+" because tests")) {
                            for(int i=0;i<200&&seen.Count==0;i++)Thread.Sleep(50);
                            if(seen.Count!=1||seen[0].Name!=name)throw new Exception("orbit-secret request did not reach the UI");
                            if(save)vault.Set(name,"cli-entered-value","project",projA);broker.Answer(seen[0].Id,save?"saved":"denied");
                            string o=p.StandardOutput.ReadToEnd();p.StandardError.ReadToEnd();if(!p.WaitForExit(20000))throw new Exception("request hung");
                            if(p.ExitCode!=(save?0:2)||(save&&!o.Contains("Saved"))||o.Contains("cli-entered-value"))throw new Exception("orbit-secret request result is wrong ("+p.ExitCode+"): "+o);
                        }
                    }
                }
                broker.Stop();
            });
            test("remote API stores secrets write-only and passes only names to new terminals",()=> {
                int port=FreePort();var terminals=new TestSecretTerminals(Path.Combine(dir,"api-secrets.dat"));const string value="sk-remote-Secret-456";
                using(var server=new RemoteServer(terminals,Path.Combine(dir,"secret-devices.json"))) {
                    server.Start(port);string token=server.IssueAccountToken("test");
                    Func<string,string,string,string> post=(path,body,auth)=>Raw(port,"POST /api/v1/"+path+" HTTP/1.1\r\nHost: 127.0.0.1:"+port+"\r\n"+(auth==null?"":"Authorization: Bearer "+auth+"\r\n")+"Content-Type: application/json\r\nContent-Length: "+Encoding.UTF8.GetByteCount(body)+"\r\n\r\n"+body);
                    if(!post("secrets/set","{\"name\":\"API_KEY\",\"value\":\""+value+"\"}",null).StartsWith("HTTP/1.1 401",StringComparison.Ordinal))throw new Exception("unauthenticated secret write accepted");
                    string saved=post("secrets/set","{\"name\":\"API_KEY\",\"value\":\""+value+"\"}",token);
                    if(!saved.StartsWith("HTTP/1.1 200",StringComparison.Ordinal)||!saved.Contains("API_KEY")||saved.Contains(value))throw new Exception("secret save reply is wrong or echoes the value: "+saved);
                    string projectDir=(Path.Combine(dir,"api-project")).Replace("\\","\\\\");
                    if(!post("secrets/set","{\"name\":\"PROJ_KEY\",\"value\":\"proj-value-1\",\"scope\":\"project\",\"project\":\""+projectDir+"\"}",token).StartsWith("HTTP/1.1 200",StringComparison.Ordinal))throw new Exception("project-scoped secret was not stored");
                    string inProject=post("secrets","{\"project\":\""+projectDir+"\"}",token),elsewhere=post("secrets","{}",token);
                    if(!inProject.Contains("PROJ_KEY")||!inProject.Contains("\"scope\":\"project\"")||inProject.Contains("proj-value-1")||elsewhere.Contains("PROJ_KEY"))throw new Exception("project key visibility is wrong: "+inProject+" / "+elsewhere);
                    if(!post("secrets/delete","{\"name\":\"PROJ_KEY\",\"scope\":\"project\",\"project\":\""+projectDir+"\"}",token).StartsWith("HTTP/1.1 200",StringComparison.Ordinal)||post("secrets","{\"project\":\""+projectDir+"\"}",token).Contains("PROJ_KEY"))throw new Exception("project key was not deleted");
                    string listed=post("secrets","{}",token);
                    if(!listed.StartsWith("HTTP/1.1 200",StringComparison.Ordinal)||!listed.Contains("API_KEY")||listed.Contains(value))throw new Exception("secret list is wrong or exposes a value: "+listed);
                    if(!post("secrets/set","{\"name\":\"bad name\",\"value\":\"x\"}",token).StartsWith("HTTP/1.1 400",StringComparison.Ordinal))throw new Exception("invalid secret name accepted");
                    if(!post("terminals/create","{\"profile\":\"claude\",\"secrets\":[\"API_KEY\"]}",token).StartsWith("HTTP/1.1 200",StringComparison.Ordinal)||terminals.LastSecrets==null||String.Join(",",terminals.LastSecrets)!="API_KEY")throw new Exception("secret names did not reach terminal creation");
                    if(!post("terminals/create","{\"profile\":\"claude\"}",token).StartsWith("HTTP/1.1 200",StringComparison.Ordinal)||terminals.LastSecrets!=null)throw new Exception("a create without a secrets list must mean every stored secret (null), not none");
                    if(!post("terminals/create","{\"profile\":\"claude\",\"secrets\":[]}",token).StartsWith("HTTP/1.1 200",StringComparison.Ordinal)||terminals.LastSecrets==null||terminals.LastSecrets.Length!=0)throw new Exception("an explicit empty secrets list must stay empty");
                    string pending=post("snapshot","{\"session\":\"s1\"}",token);
                    if(!pending.StartsWith("HTTP/1.1 200",StringComparison.Ordinal)||!pending.Contains("secretRequests")||!pending.Contains("req-1")||pending.Contains(value))throw new Exception("pending AI prompts were not delivered with the terminal snapshot: "+pending);
                    if(!post("secrets/answer","{\"request\":\"req-1\",\"status\":\"saved\"}",token).StartsWith("HTTP/1.1 200",StringComparison.Ordinal)||terminals.LastAnswer!="req-1:saved")throw new Exception("secret request answer did not reach the host: "+terminals.LastAnswer);
                    if(!post("terminals/create","{\"profile\":\"claude\",\"secrets\":[\"MISSING_KEY\"]}",token).StartsWith("HTTP/1.1 400",StringComparison.Ordinal))throw new Exception("unknown secret did not fail terminal creation");
                    if(!post("secrets/delete","{\"name\":\"API_KEY\"}",token).StartsWith("HTTP/1.1 200",StringComparison.Ordinal)||post("secrets","{}",token).Contains("API_KEY"))throw new Exception("secret was not deleted");
                }
            });
            File.WriteAllText(report,log.ToString());Directory.Delete(dir,true);return failed==0?0:1;
        }
        static int FreePort(){var l=new TcpListener(IPAddress.Loopback,0);l.Start();int port=((IPEndPoint)l.LocalEndpoint).Port;l.Stop();return port;}
        static string Raw(int port,string request){try{using(var c=new TcpClient()){c.ReceiveTimeout=5000;c.SendTimeout=5000;c.Connect(IPAddress.Loopback,port);using(var s=c.GetStream()){byte[] bytes=Encoding.UTF8.GetBytes(request);s.Write(bytes,0,bytes.Length);s.Flush();c.Client.Shutdown(SocketShutdown.Send);using(var reader=new StreamReader(s,Encoding.UTF8))return reader.ReadToEnd();}}}catch(IOException){return "";}}
        sealed class TestSecretTerminals:TestTerminals,IRemoteSecrets {
            readonly SecretVault vault;public string[] LastSecrets=new string[0];
            public TestSecretTerminals(string path){vault=new SecretVault(path);}
            public object SecretNames(string project){return new {names=vault.Names(project),keys=vault.List(project)};}
            public object SetSecret(string name,string value,string scope,string project){vault.Set(name,value,scope,project);return SecretNames(project);}
            public object DeleteSecret(string name,string scope,string project){vault.Delete(name,scope,project);return SecretNames(project);}
            public string LastAnswer;
            public object Create(string profile,string resumeId,string project,string[] secrets){if(secrets!=null)vault.Env(project,secrets);LastSecrets=secrets;return new {session="test"};}
            public object PendingSecretRequests(string session){return new[]{new {id="req-1",name="ASKED_KEY",reason="why"}};}
            public bool AnswerSecretRequest(string request,string status){LastAnswer=request+":"+status;return true;}
        }
        class TestTerminals:IRemoteTerminals,IRemoteUploads {public string Folder;public string UploadFolder(string id){if(id!="s1"||Folder==null)throw new InvalidOperationException("terminal not found");return Folder;}public object Sessions(){return new {sessions=new object[0]};}public object SavedSessions(){return new {sessions=new object[0]};}public object DeleteSavedSession(string provider,string id){return new {sessions=new object[0]};}public object Snapshot(string id){return new { };}public object Output(string id,long after,int timeout){return new { };}public object Create(string profile,string resumeId,string project=null){return new {session="test"};}public object Projects(){return new {projects=new object[0],current=""};}public void Input(string id,string data){}}
    }
}
