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
        sealed class Terminals : IRemoteTerminals {
            sealed class Session { internal string Id,Name,Profile;internal int Pid; }
            internal readonly List<string> Inputs=new List<string>();
            internal readonly List<string> Created=new List<string>();
            readonly List<Session> sessions=new List<Session> {new Session{Id="cmd",Name="CMD",Profile="cmd",Pid=101},new Session{Id="ps",Name="PowerShell",Profile="powershell",Pid=102}};
            bool dropped; long outputSeq=9; string pendingOutput="";
            public object Sessions() { lock(sessions)return new {sessions=sessions.Select(s=>new {id=s.Id,name=s.Name,profile=s.Profile,pid=s.Pid}).ToArray()}; }
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
            public object Create(string profile,string resumeId,string project=null) { lock(Created)Created.Add(profile+(String.IsNullOrEmpty(resumeId)?"":"/resume"));string id=String.IsNullOrEmpty(resumeId)?"new-"+profile:"resume-"+profile;lock(sessions)sessions.Add(new Session{Id=id,Name=profile,Profile=profile,Pid=200+sessions.Count});return new {session=id}; }
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
