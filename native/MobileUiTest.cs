using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Linq;

namespace Orbit {
    // Real mobile page verification. It uses the production loopback HTTP boundary and
    // WebView2, but a deterministic terminal implementation so the interaction tests
    // do not launch shells or expose a test listener through a public tunnel.
    internal static class MobileUiTest {
        sealed class Terminals : IRemoteTerminals, IRemoteSecrets {
            sealed class Session { internal string Id,Name,Profile,Project="C:\\orbit";internal int Pid;internal long Seq,LastOutputMs=-1;internal bool Asking; }
            internal readonly List<string> CreatedProjects=new List<string>();
            internal void AddSession(string id,string name,string project) { lock(sessions)sessions.Add(new Session{Id=id,Name=name,Profile="claude",Project=project,Pid=300+sessions.Count}); }
            internal void SetActivity(string id,long lastOutputMs,long seq,bool asking=false) { lock(sessions){var item=sessions.First(x=>x.Id==id);item.LastOutputMs=lastOutputMs;item.Seq=seq;item.Asking=asking;} }
            internal readonly List<string> Inputs=new List<string>();
            internal readonly List<string> Created=new List<string>();
            internal readonly List<string> SecretCreates=new List<string>(),SetCalls=new List<string>();
            internal string PendingId,PendingName,PendingReason,Answered;
            internal void Ask(string id,string name,string reason) { lock(Secrets){PendingId=id;PendingName=name;PendingReason=reason;Answered=null;} }
            public object PendingSecretRequests(string session) { lock(Secrets)return PendingId==null?new object[0]:new object[]{new {id=PendingId,name=PendingName,reason=PendingReason,project="C:\\orbit",projectName="orbit",terminal="orbit · PowerShell"}}; }
            public bool AnswerSecretRequest(string request,string status) { lock(Secrets){if(request!=PendingId)return false;Answered=request+":"+status;PendingId=null;return true;} }
            internal readonly Dictionary<string,string> Secrets=new Dictionary<string,string>(),Scopes=new Dictionary<string,string>();
            public object SecretNames(string project) { lock(Secrets)return new {names=Secrets.Keys.OrderBy(x=>x).ToArray(),keys=Secrets.Keys.OrderBy(x=>x).Select(n=>new {name=n,scope=Scopes[n]}).ToArray()}; }
            public object SetSecret(string name,string value,string scope,string project) { lock(Secrets){Secrets[name]=value;Scopes[name]=scope=="project"?"project":"global";SetCalls.Add(name+"|"+scope+"|"+project);}return SecretNames(project); }
            public object DeleteSecret(string name,string scope,string project) { lock(Secrets){Secrets.Remove(name);Scopes.Remove(name);}return SecretNames(project); }
            object IRemoteSecrets.Create(string profile,string resumeId,string project,string[] secrets) { lock(SecretCreates)SecretCreates.Add(profile+":"+(secrets==null?"*":String.Join(",",secrets)));return Create(profile,resumeId,project); }
            readonly List<Session> sessions=new List<Session> {new Session{Id="cmd",Name="CMD",Profile="cmd",Pid=101},new Session{Id="ps",Name="PowerShell",Profile="powershell",Pid=102}};
            bool dropped; long outputSeq=9; string pendingOutput="";
            public object Sessions() { lock(sessions)return new {sessions=sessions.Select(s=>new {id=s.Id,name=s.Name,profile=s.Profile,pid=s.Pid,project=s.Project,seq=s.Seq,lastOutputMs=s.LastOutputMs,asking=s.Asking}).ToArray()}; }
            public object Snapshot(string id) {
                lock(sessions)if(!sessions.Any(s=>s.Id==id)) throw new InvalidOperationException("terminal not found");
                string longLine="\uD55C\uAD6D\uC5B4 \uAE34 \uCD9C\uB825 "+new string('\uAC00',260)+" https://example.test/very/long/path/without/a/natural/break";
                string useful="## \uC791\uC5C5 \uACB0\uACFC\r\n\uC694\uCCAD\uD55C \uBCC0\uACBD\uC744 \uC644\uB8CC\uD588\uC2B5\uB2C8\uB2E4. \uD14C\uC2A4\uD2B8 \uACB0\uACFC\uB3C4 \uD655\uC778\uD588\uC2B5\uB2C8\uB2E4.\r\n\r\n\uC77C\uBC18 \uD654\uBA74\uACFC \uD0A4\uBCF4\uB4DC \uD654\uBA74\uC5D0\uC11C \uC77D\uAE30 \uD3B8\uD55C \uCD9C\uB825\uC744 \uC720\uC9C0\uD569\uB2C8\uB2E4.\r\n\r\n- \uC138\uC158 \uC5F0\uACB0 \uD655\uC778\r\n- \uCD9C\uB825 \uC904\uBC14\uAFC8 \uBCF4\uC874\r\n\r\n    orbit remote --resume\r\n\r\nC:\\workspace\\very\\long\\path\\for\\readable\\mobile\\result\\details\\report.md\r\n\r\n";
                return new {seq=9,data=id=="cmd"?"cmd ready\r\n"+useful+longLine+"\r\n":"powershell ready\r\n",cols=80,rows=24,items=new object[0]};
            }
            public object Output(string id,long after,int timeout) { Thread.Sleep(35);lock(this){return new {reset=false,seq=outputSeq,items=after<outputSeq?new[]{new {seq=outputSeq,data=pendingOutput}}:new object[0]};} }
            internal void EmitOutput(string value) { lock(this){pendingOutput=value;outputSeq++;} }
            public object SavedSessions() { return new {sessions=new[]{new {provider="codex",id="11111111-1111-4111-8111-111111111111",title="Saved mobile Codex",cwd="C:\\orbit"}}}; }
            public object DeleteSavedSession(string provider,string id) { return new {sessions=new object[0]}; }
            public object Create(string profile,string resumeId,string project=null) { lock(Created)Created.Add(profile+(String.IsNullOrEmpty(resumeId)?"":"/resume"));lock(CreatedProjects)CreatedProjects.Add(project??"");string id=String.IsNullOrEmpty(resumeId)?"new-"+profile:"resume-"+profile;lock(sessions)sessions.Add(new Session{Id=id,Name=profile,Profile=profile,Pid=200+sessions.Count});return new {session=id}; }
            public object Projects() { return new {projects=new[]{new {path="C:\\orbit",name="orbit"}},current="C:\\orbit"}; }
            public void Input(string id,string data) {
                lock(Inputs)Inputs.Add(id+"|"+data);
                if(data.IndexOf("SLOW",StringComparison.Ordinal)>=0) Thread.Sleep(450);
                if(data=="DROP"&&!dropped) { dropped=true;throw new IOException("simulated response drop after terminal accepted input"); }
            }
            internal int Count(string fragment) { lock(Inputs)return Inputs.FindAll(x=>x.IndexOf(fragment,StringComparison.Ordinal)>=0).Count; }
            internal int CountExact(string data) { lock(Inputs)return Inputs.FindAll(x=>x.EndsWith("|"+data,StringComparison.Ordinal)).Count; }
        }
        static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
        internal static async Task Run(MainWindow window) {
            int port=0;
            string store=TestArtifacts.PathFor("mobile-devices-test.json");
            try { if(File.Exists(store))File.Delete(store); } catch { }
            var terminals=new Terminals();
            using(var server=new RemoteServer(terminals,store)) {
                Exception startFailure=null;
                for(int attempt=0;attempt<5;attempt++) { port=FreePort();try { server.Start(port);startFailure=null;break; } catch(HttpListenerException ex) { startFailure=ex; } }
                if(startFailure!=null) throw new InvalidOperationException("mobile loopback server could not claim a test port",startFailure);
                string token=server.IssueAccountToken("mobile-ui-test");
                if(String.IsNullOrEmpty(token)) throw new InvalidOperationException("mobile test account token setup failed");

                var original=window.Size;var originalMinimum=window.MinimumSize;
                try {
                    window.MinimumSize=Size.Empty;
                    window.Size=new Size(402,856); // client area is exactly 390 x 844 with the six-pixel host gutter.
                    await window.SetMobileColorSchemeForTest("dark");
                    window.NavigateForMobileTest("http://127.0.0.1:"+port+"/mobile.html");
                    await Wait(window,"document.readyState==='complete'",5000,"mobile page load");
                    await window.ExecuteForTest("localStorage.setItem('orbit.remote.token',"+Json.Serialize(token)+");location.reload();'token set'");
                    await Wait(window,"Boolean(document.querySelector('#workspace')&&!document.querySelector('#workspace').hidden&&document.querySelectorAll('.session-tab').length===2)",8000,"mobile authenticated workspace");
                    await Stage(window,@"const reconnectDraft='reconnect draft';const reconnectInput=document.querySelector('#input');reconnectInput.value=reconnectDraft;reconnectInput.dispatchEvent(new Event('input',{bubbles:true}));const reconnectFetch=window.fetch;let unreadableSessions=true;window.fetch=async(...args)=>{const url=new URL(args[0],location.href);if(unreadableSessions&&url.pathname.endsWith('/api/v1/sessions')){unreadableSessions=false;return new Response('',{status:502,statusText:'Bad Gateway',headers:{'content-type':'application/json'}});}return reconnectFetch(...args);};document.querySelector('#refresh').click();for(let i=0;i<100&&document.querySelector('#reconnect').hidden;i++)await pause(40);const reconnect=document.querySelector('#reconnect');check(!reconnect.hidden&&!document.querySelector('#workspace').hidden,'unreadable reconnect response did not keep the workspace visible');check(reconnect.textContent.includes('HTTP 502')&&reconnect.textContent.includes('/api/v1/sessions'),'unreadable response diagnostic omitted HTTP status or endpoint');check(!document.querySelector('#resume').disabled,'reconnect action was disabled');window.fetch=reconnectFetch;document.querySelector('#resume').click();for(let i=0;i<160&&(document.querySelector('#workspace').hidden||document.querySelector('#send').disabled);i++)await pause(40);check(!document.querySelector('#workspace').hidden&&!document.querySelector('#send').disabled,'manual reconnect did not restore the composer');check(reconnectInput.value===reconnectDraft,'manual reconnect lost the draft');await pause(1700);check(document.querySelector('#reconnect').hidden,'stale reconnect retry replaced the newer workspace');");
                    await window.ExecuteForTest("document.querySelector('#logout').click();'paused'");
                    await Wait(window,"Boolean(!document.querySelector('#reconnect').hidden&&document.querySelector('#workspace').hidden&&localStorage.getItem('orbit.remote.token'))",2000,"disconnect preserves trusted browser token");
                    await window.ExecuteForTest("document.querySelector('#resume').click();'resumed'");
                    await Wait(window,"Boolean(!document.querySelector('#workspace').hidden&&document.querySelectorAll('.session-tab').length===2)",8000,"disconnect reconnects without pairing");
                    window.NavigateForMobileTest("http://127.0.0.1:"+port+"/mobile.html?trusted-revisit=1");
                    await Wait(window,"Boolean(!document.querySelector('#workspace').hidden&&document.querySelector('#login').hidden&&document.querySelectorAll('.session-tab').length===2&&localStorage.getItem('orbit.remote.token'))",8000,"trusted same-origin revisit without a fresh login");
                    await Stage(window,PhoneScript);
                    string readable=Json.Deserialize<string>(await window.ExecuteForTest("document.querySelector('#readable-output').textContent"));
                    File.WriteAllText(TestArtifacts.PathFor("mobile-readable-output.txt"),readable,new UTF8Encoding(false));
                    string expectedCjk="\uD55C\uAD6D\uC5B4 \uAE34 \uCD9C\uB825 "+new string('\uAC00',260)+" https://example.test/very/long/path/without/a/natural/break";
                    if(readable.IndexOf(expectedCjk,StringComparison.Ordinal)<0) throw new InvalidOperationException("CJK long line changed while wrapping; rendered length "+readable.Length+", expected length "+expectedCjk.Length);
                    await window.CaptureForTest(TestArtifacts.PathFor("mobile-phone-dark.png"));
                    await window.ExecuteForTest("document.querySelector('#view-toggle').click();'raw'");
                    await Wait(window,"!document.querySelector('#raw-output').hidden&&document.querySelector('#raw-output .xterm')",2000,"raw terminal view");
                    await window.CaptureForTest(TestArtifacts.PathFor("mobile-phone-raw-dark.png"));
                    await window.ExecuteForTest("document.querySelector('#view-toggle').click();'readable'");
                    await window.SetMobileColorSchemeForTest("light");
                    await Task.Delay(120);
                    await window.CaptureForTest(TestArtifacts.PathFor("mobile-phone-light.png"));

                    window.Size=new Size(402,442); // 390 x 430 keyboard-constrained viewport.
                    await Task.Delay(150);
                    await Stage(window,"if(innerWidth!==390||innerHeight!==430)throw Error('keyboard viewport expected 390x430, got '+innerWidth+'x'+innerHeight);if(document.documentElement.scrollWidth>innerWidth)throw Error('keyboard viewport horizontal overflow');const saved=document.querySelector('#saved-sessions');saved.open=true;saved.dispatchEvent(new Event('toggle'));for(let i=0;i<80&&!document.querySelector('#saved-list button');i++)await pause(50);check(saved.open&&document.querySelector('#saved-list button'),'saved list did not open in keyboard viewport');const output=document.querySelector('#readable-output');check(output.getBoundingClientRect().height>=100,'keyboard readable area is below 100px');check(!document.querySelector('#send').disabled,'keyboard composer is not usable');");
                    await window.CaptureForTest(TestArtifacts.PathFor("mobile-phone-keyboard.png"));
                    var outputArrival=Task.Run(async delegate { await Task.Delay(250);terminals.EmitOutput("\r\n\uC0C8 \uCD9C\uB825: \uC2A4\uD06C\uB864 \uC704\uCE58\uB97C \uC720\uC9C0\uD569\uB2C8\uB2E4.\r\n"); });
                    await Stage(window,"const readable=document.querySelector('#readable-output');document.querySelector('#saved-sessions').open=false;readable.scrollTop=Math.min(28,Math.max(0,readable.scrollHeight-readable.clientHeight));const before=readable.scrollTop;for(let i=0;i<100&&!readable.textContent.includes('새 출력');i++)await pause(40);check(readable.textContent.includes('새 출력'),'controlled terminal output did not render');check(Math.abs(readable.scrollTop-before)<=2,'new output moved a manually positioned readable view');");
                    await outputArrival;
                    window.Size=new Size(832,1192); // The host is bounded by the desktop work area; CDP retains the 820 x 1180 page viewport.
                    await window.SetMobileViewportForTest(820,1180);
                    await Task.Delay(150);
                    await Stage(window,"if(innerWidth!==820||innerHeight!==1180)throw Error('tablet viewport expected 820x1180, got '+innerWidth+'x'+innerHeight);if(document.documentElement.scrollWidth>innerWidth)throw Error('tablet viewport horizontal overflow');");
                    await window.CaptureMobileViewportForTest(TestArtifacts.PathFor("mobile-tablet-light.png"));
                    await Stage(window,InteractionScript);
                    if(terminals.Created.Count!=5||new[]{"cmd","powershell","codex","claude","codex/resume"}.Any(profile=>!terminals.Created.Contains(profile))) throw new InvalidOperationException("mobile create controls or saved resume did not select every supported profile");
                    if(terminals.CountExact("SLOW")!=1||terminals.CountExact("CONFIRMED DROP")!=1||terminals.CountExact("\r")<3) throw new InvalidOperationException("mobile controls did not reach expected body and Enter packets");
                    if(terminals.CountExact("DROP")!=1||terminals.CountExact("DROP\r")!=0) throw new InvalidOperationException("dropped response body was repeated or its Enter packet was emitted");
                    await Stage(window,SecretsScript);
                    string secretValue;lock(terminals.Secrets)terminals.Secrets.TryGetValue("OPENAI_API_KEY",out secretValue);
                    if(secretValue!="sk-mobile-secret-789") throw new InvalidOperationException("mobile secret was not stored on the host");
                    lock(terminals.Secrets)if(!terminals.SetCalls.Contains("OPENAI_API_KEY|project|C:\\orbit")) throw new InvalidOperationException("a key saved from the phone must default to the selected project: "+String.Join(";",terminals.SetCalls));
                    if(terminals.SecretCreates.Count!=6||terminals.SecretCreates.Any(x=>!x.EndsWith(":*"))) throw new InvalidOperationException("the phone must not choose keys per session; the host decides: "+String.Join(";",terminals.SecretCreates));
                    if(terminals.Created.Count!=6) throw new InvalidOperationException("secret-carrying create did not go through the normal create path");
                    // an AI in the session asks for another key: the page shows a prompt, the value goes to the PC and never through the terminal
                    terminals.Ask("req-1","STRIPE_KEY","need it to test payments");terminals.EmitOutput("\r\nwaiting for the user\r\n");
                    await Stage(window,AskShowScript);
                    await window.CaptureMobileViewportForTest(TestArtifacts.PathFor("mobile-secret-request-tablet.png"));
                    await Stage(window,AskSaveScript);
                    string requested;lock(terminals.Secrets)terminals.Secrets.TryGetValue("STRIPE_KEY",out requested);
                    if(requested!="sk-stripe-request-1"||terminals.Answered!="req-1:saved") throw new InvalidOperationException("prompted secret was not stored and answered: "+requested+" / "+terminals.Answered);
                    lock(terminals.Secrets)if(!terminals.SetCalls.Contains("STRIPE_KEY|project|C:\\orbit")) throw new InvalidOperationException("a requested key must be saved for the project of the terminal that asked: "+String.Join(";",terminals.SetCalls));
                    if(terminals.Count("sk-stripe-request-1")!=0) throw new InvalidOperationException("prompted secret value reached a terminal");
                    await window.ExecuteForTest("document.querySelector('#menu-toggle').click();document.querySelector('#secrets').open=true;'secrets menu'");
                    await Task.Delay(150);
                    await window.CaptureMobileViewportForTest(TestArtifacts.PathFor("mobile-secrets-tablet.png"));
                    await window.ExecuteForTest("document.querySelector('#menu-panel').hidden=true;'menu closed'");
                    // sessions of two projects: grouped in the menu, a status dot each, and the phone follows the session in front
                    terminals.AddSession("other","other · Claude","C:\\other");
                    await Stage(window,SessionsShowScript);
                    terminals.SetActivity("other",300,5);
                    await Stage(window,SessionsWorkingScript);
                    await window.CaptureMobileViewportForTest(TestArtifacts.PathFor("mobile-sessions-tablet.png"));
                    terminals.SetActivity("other",-1,5);
                    await Stage(window,SessionsAttentionScript);
                    terminals.SetActivity("cmd",-1,0,true);
                    await Stage(window,SessionsAskingScript);
                    terminals.SetActivity("cmd",-1,0,false);
                    await Stage(window,SessionsProjectScript);
                    lock(terminals.CreatedProjects)if(terminals.CreatedProjects.Count==0||terminals.CreatedProjects[terminals.CreatedProjects.Count-1]!="C:\\orbit") throw new InvalidOperationException("a session started from the New Session box must use the project chosen there: "+String.Join(";",terminals.CreatedProjects));
                    string replacementToken=server.IssueAccountToken("replacement-token-test");
                    if(String.IsNullOrEmpty(replacementToken)) throw new InvalidOperationException("mobile test replacement token setup failed");
                    await window.ExecuteForTest("localStorage.setItem('orbit.remote.token','revoked-token');'replacement token set'");
                    window.NavigateForMobileTest("http://127.0.0.1:"+port+"/mobile.html#login="+Uri.EscapeDataString(replacementToken));
                    await Wait(window,"Boolean(document.querySelector('#workspace')&&!document.querySelector('#workspace').hidden&&document.querySelectorAll('.session-tab').length>=2&&localStorage.getItem('orbit.remote.token')&&localStorage.getItem('orbit.remote.token')!=='revoked-token')",10000,"#login fragment replaces a revoked token");
                    string confirmedToken=Json.Deserialize<string>(await window.ExecuteForTest("localStorage.getItem('orbit.remote.token')"));
                    if(String.IsNullOrEmpty(confirmedToken)||confirmedToken=="revoked-token"||confirmedToken!=replacementToken) throw new InvalidOperationException("revoked token was not replaced by the #login fragment");
                    File.WriteAllText(TestArtifacts.PathFor("mobile-report.txt"),"PASS WebView2 mobile page over loopback RemoteServer: account-token login, trusted same-origin revisit without a fresh login, disconnect/reconnect preserving the trusted browser token, #login fragment replacing a revoked token, CMD/PowerShell/Codex/Claude create API, 390x844 phone, 390x430 keyboard, 820x1180 tablet, dark/light captures, wrapping/raw view, drafts, async session switch, special keys, and dropped-response receipt confirmation. No public tunnel was enabled.\r\nInputs: "+terminals.Count("\r"));
                } finally { window.MinimumSize=originalMinimum;window.Size=original; }
            }
        }
        static async Task Wait(MainWindow window,string test,int milliseconds,string label) { for(int i=0;i<milliseconds/40;i++){if((await window.ExecuteForTest("Boolean("+test+")"))=="true")return;await Task.Delay(40);}throw new TimeoutException("Mobile UI timed out: "+label); }
        static async Task Stage(MainWindow window,string body) {
            string script="window.__orbitMobileTest={done:false};(async()=>{try{const pause=ms=>new Promise(r=>setTimeout(r,ms));const check=(v,m)=>{if(!v)throw Error(m)};"+body+"window.__orbitMobileTest={done:true};}catch(e){window.__orbitMobileTest={done:true,error:String(e.stack||e)}}})();'started'";
            await window.ExecuteForTest(script);
            for(int i=0;i<200;i++) { string value=await window.ExecuteForTest("JSON.stringify(window.__orbitMobileTest)");if(value.IndexOf("\\\"done\\\":true",StringComparison.Ordinal)>=0){if(value.IndexOf("\\\"error\\\"",StringComparison.Ordinal)>=0)throw new InvalidOperationException(value);return;}await Task.Delay(50); }
            throw new TimeoutException("Mobile UI stage timed out");
        }
        static int FreePort() { var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();return port; }
        const string PhoneScript=@"
check(innerWidth===390&&innerHeight===844,'phone viewport expected 390x844, got '+innerWidth+'x'+innerHeight);
check(document.documentElement.scrollWidth<=innerWidth,'phone page horizontal overflow');
check(getComputedStyle(document.querySelector('#readable-output')).whiteSpace==='pre-wrap','readable output does not wrap');
for(let i=0;i<100&&!document.querySelector('#readable-output').textContent.includes('\uD55C\uAD6D\uC5B4 \uAE34 \uCD9C\uB825');i++)await pause(40);
check(document.querySelector('#readable-output').textContent.includes('\uD55C\uAD6D\uC5B4 \uAE34 \uCD9C\uB825'),'CJK output missing');
const expectedCjk='\uD55C\uAD6D\uC5B4 \uAE34 \uCD9C\uB825 '+String.fromCharCode(0xAC00).repeat(260)+' https://example.test/very/long/path/without/a/natural/break';
document.querySelector('#view-toggle').click();await pause(60);check(!document.querySelector('#raw-output').hidden&&document.querySelector('#readable-output').hidden,'raw toggle did not switch rendered view');
document.querySelector('#view-toggle').click();await pause(60);check(!document.querySelector('#readable-output').hidden,'readable toggle did not restore');
";
        // API keys: pasted into a password field (not the composer), stored write-only on the host for the selected project or for all projects.
        const string SecretsScript=@"
const tabs=()=>Array.from(document.querySelectorAll('.session-tab'));const drawer=document.querySelector('#secrets');
check(drawer&&document.querySelector('#secret-value')&&document.querySelector('#secret-value').type==='password','API key drawer or its password field is missing');
drawer.open=true;drawer.dispatchEvent(new Event('toggle'));
for(let i=0;i<80&&document.querySelector('#secret-form').hidden;i++)await pause(50);
const value=document.querySelector('#secret-value'),name=document.querySelector('#secret-name'),scope=document.querySelector('#secret-scope');
check(!document.querySelector('#secret-form').hidden&&document.querySelector('#secret-add').hidden,'with no keys the paste form must be open right away');
check(document.querySelector('#secret-summary').textContent==='저장된 키 없음','the summary should say nothing is stored');
check(!scope.hidden&&scope.value==='project'&&scope.options[0].text.includes('orbit'),'the scope must default to the selected project');
value.value='sk-mobile-secret-789';value.dispatchEvent(new Event('input'));
check(name.value==='OPENAI_API_KEY','pasting a key did not suggest its name');
document.querySelector('#secret-save').click();
for(let i=0;i<80&&!document.querySelector('#secret-list [data-secret=OPENAI_API_KEY]');i++)await pause(50);
const row=document.querySelector('#secret-list [data-secret=OPENAI_API_KEY]');check(row&&row.textContent.includes('이 프로젝트'),'the saved key is not listed with its scope');
check(value.value===''&&name.value==='','the form was not cleared after saving');
check(document.querySelector('#secret-summary').textContent==='1개 저장됨','the summary does not count the key');
check(!document.documentElement.outerHTML.includes('sk-mobile-secret-789'),'the key value is present in the page after saving');
check(!document.querySelector('#secret-add').hidden&&document.querySelector('#secret-form').hidden,'after saving, the list shows with an add button');
// a key for all projects, and deleting asks twice
document.querySelector('#secret-add').click();value.value='temp-value';value.dispatchEvent(new Event('input'));name.value='TEMP_KEY';name.dispatchEvent(new Event('input'));scope.value='global';document.querySelector('#secret-save').click();
for(let i=0;i<80&&!document.querySelector('#secret-list [data-secret=TEMP_KEY]');i++)await pause(50);
const temp=document.querySelector('#secret-list [data-secret=TEMP_KEY]');check(temp&&temp.textContent.includes('모든 프로젝트'),'the all-projects key is not listed');
const del=temp.querySelector('.secret-delete');del.click();check(del.textContent==='한 번 더'&&document.querySelector('#secret-list [data-secret=TEMP_KEY]'),'the first tap must only ask for confirmation');
del.click();for(let i=0;i<80&&document.querySelector('#secret-list [data-secret=TEMP_KEY]');i++)await pause(50);
check(!document.querySelector('#secret-list [data-secret=TEMP_KEY]')&&document.querySelector('#secret-list [data-secret=OPENAI_API_KEY]'),'the second tap should delete only that key');
const target=tabs().length+1;document.querySelector('[data-create=powershell]').click();for(let i=0;i<80&&tabs().length!==target;i++)await pause(50);check(tabs().length===target,'a session was not created with keys stored');
";
        const string SessionsPrelude=@"
const until=async(test,label,ms=14000)=>{for(let i=0;i<ms/40&&!test();i++)await pause(40);check(test(),label);};
const tabOf=id=>document.querySelector('.session-tab[data-session-id=""'+id+'""]'),rowOf=id=>document.querySelector('.session-group-row[data-session-id=""'+id+'""]');
";
        const string SessionsShowScript=SessionsPrelude+@"
await until(()=>tabOf('other'),'a session that appeared on the PC did not show up on the phone');
check(tabOf('other').querySelector('.session-dot'),'the session tab has no status dot');
document.querySelector('#menu-toggle').click();await pause(100);
const groups=Array.from(document.querySelectorAll('#session-groups .session-group')).map(g=>({head:g.querySelector('.session-group-head').textContent,rows:Array.from(g.querySelectorAll('.session-label')).map(x=>x.textContent)}));
check(groups.length===2&&groups[0].head==='orbit'&&groups[1].head==='other','the menu does not group sessions by project: '+JSON.stringify(groups));
check(JSON.stringify(groups[1].rows)===JSON.stringify(['Claude']),'the project prefix should not repeat inside its group: '+JSON.stringify(groups[1].rows));
check(!document.querySelector('#project-row'),'the old project row is still in the menu');
";
        const string SessionsWorkingScript=SessionsPrelude+@"
await until(()=>tabOf('other')&&tabOf('other').dataset.status==='working','a session that is printing should read as working');
check(rowOf('other').querySelector('.session-status').textContent==='작업 중','the working label is wrong');
";
        const string SessionsAttentionScript=SessionsPrelude+@"
await until(()=>tabOf('other')&&tabOf('other').dataset.status==='attention','a session that printed while not in front and went quiet should ask for a look');
check(rowOf('other').querySelector('.session-status').textContent==='확인해 보세요','the attention label is wrong');
";
        const string SessionsAskingScript=SessionsPrelude+@"
await until(()=>rowOf('cmd')&&rowOf('cmd').dataset.status==='attention'&&rowOf('cmd').querySelector('.session-status').textContent==='키 요청','a session waiting for an API key should read as such');
";
        const string SessionsProjectScript=SessionsPrelude+@"
// opening the session that wants a look clears the mark and moves the phone to its project
rowOf('other').click();
await until(()=>document.querySelector('#session-select').value==='other','the session was not opened from the menu');
await until(()=>tabOf('other')&&tabOf('other').dataset.status==='idle','looking at a session should clear its attention mark');
const select=document.querySelector('#new-session .project-select');check(select&&select.options.length>=2,'the New Session box has no project picker');
check(select.value==='C:\\other','the phone did not follow the session to its project: '+select.value);
// the project picker and the agent are chosen together in that box
select.value='C:\\orbit';select.dispatchEvent(new Event('change',{bubbles:true}));
const before=Array.from(document.querySelectorAll('.session-tab')).length;
document.querySelector('#new-session').open=true;document.querySelector('#new-session [data-create=powershell]').click();
await until(()=>document.querySelectorAll('.session-tab').length===before+1,'the session was not created');
";
        const string AskShowScript=@"
const box=document.querySelector('#secret-request');
for(let i=0;i<100&&box.hidden;i++)await pause(50);
check(!box.hidden,'the AI request prompt did not appear');
const reason=document.querySelector('#secret-request-reason').textContent;
check(document.querySelector('#secret-request-title').textContent==='STRIPE_KEY'&&reason.includes('need it to test payments'),'the prompt does not show the key name and reason');
check(reason.includes('orbit')&&reason.includes('PowerShell'),'the prompt does not say which project and terminal asked: '+reason);
const scope=document.querySelector('#secret-request-scope');check(!scope.hidden&&scope.value==='project'&&scope.options[0].text.includes('orbit'),'the scope must default to the project that asked');
check(document.querySelector('#secret-request-value').type==='password','the prompt value field is not a password field');
";
        const string AskSaveScript=@"
const box=document.querySelector('#secret-request'),input=document.querySelector('#secret-request-value');
document.querySelector('#secret-request-save').click();await pause(120);check(!box.hidden&&!document.querySelector('#secret-request-error').hidden,'an empty value was accepted');
input.value='sk-stripe-request-1';document.querySelector('#secret-request-save').click();
for(let i=0;i<100&&!box.hidden;i++)await pause(50);
check(box.hidden,'the prompt stayed open after saving');check(input.value==='','the prompt value was not cleared');
await pause(300);check(box.hidden,'the answered prompt came back');
for(let i=0;i<80&&!document.querySelector('#secret-list [data-secret=STRIPE_KEY]');i++)await pause(50);
check(document.querySelector('#secret-list [data-secret=STRIPE_KEY]'),'the requested key is missing from the list');
";
        const string InteractionScript=@"
const tabs=()=>Array.from(document.querySelectorAll('.session-tab'));const input=document.querySelector('#input'),picker=document.querySelector('#session-select');const switchTo=async id=>{picker.value=id;picker.dispatchEvent(new Event('change',{bubbles:true}));for(let i=0;i<80&&document.querySelector('#send').disabled;i++)await pause(50);check(picker.value===id&&!document.querySelector('#send').disabled,'session did not become ready '+id);};
document.querySelector('#saved-sessions').open=true;document.querySelector('#saved-sessions').dispatchEvent(new Event('toggle'));for(let i=0;i<80&&!document.querySelector('#saved-list button');i++)await pause(50);const saved=document.querySelector('#saved-list button');check(saved&&saved.textContent.includes('Saved mobile Codex'),'saved session row missing');const savedTarget=tabs().length+1;saved.click();for(let i=0;i<80&&tabs().length!==savedTarget;i++)await pause(50);check(tabs().length===savedTarget&&picker.value==='resume-codex','saved session did not resume into its returned session');
for(const profile of ['cmd','powershell','codex','claude']){const button=document.querySelector('[data-create='+profile+']');check(button,'missing create choice '+profile);const target=tabs().length+1;button.click();for(let i=0;i<80&&tabs().length!==target;i++)await pause(50);check(tabs().length===target,'create choice did not refresh session '+profile);}
await switchTo('cmd');input.value='draft cmd';input.dispatchEvent(new Event('input',{bubbles:true}));await switchTo('ps');check(input.value==='','other session inherited draft');input.value='draft powershell';input.dispatchEvent(new Event('input',{bubbles:true}));await switchTo('cmd');check(input.value==='draft cmd','cmd draft was lost after session switch');
input.value='SLOW';input.dispatchEvent(new Event('input',{bubbles:true}));document.querySelector('#send').click();await pause(45);await switchTo('ps');input.value='pending powershell draft';input.dispatchEvent(new Event('input',{bubbles:true}));await pause(650);check(input.value==='pending powershell draft','pending cmd send cleared switched-session draft');await switchTo('cmd');check(input.value==='','acknowledged cmd send left stale draft');
input.value='special';input.dispatchEvent(new Event('input',{bubbles:true}));document.querySelector('[data-key=enter]').click();await pause(130);check(input.value==='special','special key unexpectedly cleared composer');
const savedFetch=window.fetch;let loseSuccessfulResponse=true;window.fetch=async(...args)=>{const response=await savedFetch(...args);if(loseSuccessfulResponse&&new URL(args[0],location.href).pathname.endsWith('/api/v1/input')){loseSuccessfulResponse=false;throw new TypeError('simulated dropped successful response')}return response};
input.value='CONFIRMED DROP';input.dispatchEvent(new Event('input',{bubbles:true}));document.querySelector('#send').click();await pause(220);window.fetch=savedFetch;check(input.value==='','receipt confirmation did not clear confirmed input');
input.value='DROP';input.dispatchEvent(new Event('input',{bubbles:true}));document.querySelector('#send').click();await pause(180);check(input.value==='DROP','indeterminate input cleared');document.querySelector('#send').click();await pause(180);check(input.value==='DROP','indeterminate receipt was resent or cleared');
";
    }
}
