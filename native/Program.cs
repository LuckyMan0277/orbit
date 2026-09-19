using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Orbit {
    internal static class Program {
        [STAThread] private static int Main(string[] args) {
            if(args.Contains("--self-test")) return SelfTest.Run();
            try { Win32.SetProcessDpiAwarenessContext(new IntPtr(-4)); }catch(EntryPointNotFoundException) { }
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainWindow(args));return Environment.ExitCode;
        }
    }
    internal sealed class MainWindow : Form, IRemoteTerminals {
        private readonly WebView2 web=new WebView2();
        private readonly WindowChrome chrome;
        private CoreWebView2Environment environment;
        private readonly JavaScriptSerializer json=new JavaScriptSerializer { MaxJsonLength=16*1024*1024 };
        private readonly ConcurrentDictionary<string,ConPty> sessions=new ConcurrentDictionary<string,ConPty>();
        private readonly ConcurrentDictionary<string,string> sessionProfiles=new ConcurrentDictionary<string,string>();
        private readonly ConcurrentDictionary<string,string> sessionResumeIds=new ConcurrentDictionary<string,string>();
        private readonly ConcurrentDictionary<string,string> sessionNames=new ConcurrentDictionary<string,string>();
        private readonly ConcurrentDictionary<string,OutputRing> outputRings=new ConcurrentDictionary<string,OutputRing>();
        private readonly ConcurrentDictionary<string,RemoteSnapshot> remoteSnapshots=new ConcurrentDictionary<string,RemoteSnapshot>();
        private readonly ConcurrentDictionary<string,RemoteCreate> remoteCreates=new ConcurrentDictionary<string,RemoteCreate>();
        private readonly ConcurrentDictionary<string,RemoteProjects> remoteProjectsRequests=new ConcurrentDictionary<string,RemoteProjects>();
        private readonly ConcurrentDictionary<string,SavedSession> savedSessions=new ConcurrentDictionary<string,SavedSession>();
        private long savedSessionRequest;
        private long nextSnapshotId;
        private RemoteServer remote;
        private RemoteAccount account;
        private string configuredRemoteOrigin;
        private readonly RemoteTunnel tunnel=new RemoteTunnel();
        private readonly RemoteTailscale tailscale=new RemoteTailscale();
        private readonly HashSet<string> pendingTerminals=new HashSet<string>();
        private readonly string dataRoot, initialFolder;
        private readonly bool uiTest, remoteTest, remoteTestTunnel;
        private readonly int remoteTestPort;
        private bool dirty, quitting, mobileUiNavigation;
        public MainWindow(string[] args) {
            Text="Orbit — Agent workspace";Size=new Size(1320,870);MinimumSize=new Size(900,620);StartPosition=FormStartPosition.CenterScreen;BackColor=Color.FromArgb(19,21,26);FormBorderStyle=FormBorderStyle.None;Padding=new Padding(6);MaximizeBox=true;
            using(var iconStream=typeof(Program).Assembly.GetManifestResourceStream("Orbit.Icon")) Icon=new Icon(iconStream);
            // Remote devices depend on this PC staying awake. Keep the system from sleeping
            // (idle or on battery) for as long as Orbit is running; the display may still turn off.
            Win32.SetThreadExecutionState(Win32.ES_CONTINUOUS|Win32.ES_SYSTEM_REQUIRED);
            chrome=new WindowChrome(this);
            uiTest=args.Contains("--ui-test");remoteTest=args.Contains("--remote-test")||args.Contains("--remote-test-tunnel");remoteTestTunnel=args.Contains("--remote-test-tunnel");remoteTestPort=remoteTest?RemoteTestPort(args):49821;
            dataRoot=remoteTest ? Path.Combine(TestArtifacts.Root,"remote-test-profile") : uiTest ? Path.Combine(TestArtifacts.Root,"webview-profile") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OrbitAgentDesktop");
            ApplyHostTheme(ReadSavedTheme());
            remote=new RemoteServer(this,Path.Combine(dataRoot,"remote-devices.json"));remote.Changed+=delegate { Send(new {type="remoteStatus",status=remote.Status()}); };
            account=new RemoteAccount(remote,Path.Combine(dataRoot,"account.json"));account.Changed+=delegate { Send(new {type="remoteAccount",account=account.Status()}); };
            tunnel.Changed+=delegate(string url,string error){RefreshRemoteOrigin();if(!String.IsNullOrEmpty(url)&&remoteTestTunnel)try{File.WriteAllText(Path.Combine(TestArtifacts.Root,"remote-test-url.txt"),remote.Url()+"#login="+Uri.EscapeDataString(remote.IssueAccountToken("remote-test-tunnel")));}catch{}Send(new {type="remoteTunnel",tunnel=tunnel.Status(),url=url,error=error});};
            tailscale.Changed+=delegate {RefreshRemoteOrigin();Send(new {type="remoteTailscale",tailscale=tailscale.Status()});};
            initialFolder=args.FirstOrDefault(x=>Directory.Exists(x)) ?? ((uiTest||remoteTest) ? TestArtifacts.Root : Environment.CurrentDirectory);
            if(uiTest||remoteTest) Directory.CreateDirectory(initialFolder);
            web.Dock=DockStyle.Fill;web.DefaultBackgroundColor=BackColor;Controls.Add(web);
            Load+=async delegate { await Initialize(); };
            FormClosing+=delegate(object sender,FormClosingEventArgs e) {
                if(!quitting && (dirty || sessions.Count>0)) {
                    string msg=(dirty?"저장하지 않은 파일 변경 사항이 있습니다.\n":"")+(sessions.Count>0?"실행 중인 터미널과 하위 프로세스가 종료됩니다.\n":"")+"Orbit을 종료할까요?";
                    if(MessageBox.Show(this,msg,"Orbit 종료",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes) {e.Cancel=true;return;}
                }
                quitting=true;Win32.SetThreadExecutionState(Win32.ES_CONTINUOUS);tunnel.Dispose();tailscale.Dispose();account.Dispose();remote.Dispose();foreach(var session in sessions.Values.ToArray())session.Dispose();sessions.Clear();foreach(var ring in outputRings.Values)ring.Close();outputRings.Clear();
            };
        }
        private async Task Initialize() {
            try {
                Directory.CreateDirectory(dataRoot);
                if(remoteTest) try { Directory.CreateDirectory(TestArtifacts.Root);string startUrl=remote.Start(remoteTestPort);string token=remote.IssueAccountToken("remote-test"); string url=startUrl+ "#login="+Uri.EscapeDataString(token); File.WriteAllText(Path.Combine(TestArtifacts.Root,"remote-test-url.txt"),url); }catch(Exception ex){Directory.CreateDirectory(TestArtifacts.Root);File.WriteAllText(Path.Combine(TestArtifacts.Root,"remote-test-error.txt"),ex.ToString());throw;}
                // Long-idle windows (minimized or occluded) get frozen by Chromium's background
                // throttling, which stalls xterm's write() callback and, with it, the ConPTY
                // ack-gated read loop (ConPty.cs ReadLoop). Keep the terminal pumping regardless
                // of window visibility.
                var webviewOptions=new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments="--disable-backgrounding-occluded-windows --disable-renderer-backgrounding --disable-background-timer-throttling" };
                environment=await CoreWebView2Environment.CreateAsync(null,Path.Combine(dataRoot,"WebView"),webviewOptions);
                await web.EnsureCoreWebView2Async(environment);
                var core=web.CoreWebView2;
                core.Settings.AreDefaultContextMenusEnabled=false;core.Settings.AreDevToolsEnabled=false;core.Settings.IsStatusBarEnabled=false;core.Settings.IsZoomControlEnabled=false;core.Settings.IsNonClientRegionSupportEnabled=true;
                core.SetVirtualHostNameToFolderMapping("orbit.local",Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"web"),CoreWebView2HostResourceAccessKind.DenyCors);
                core.NavigationStarting+=delegate(object s,CoreWebView2NavigationStartingEventArgs e) { if(!e.Uri.StartsWith("https://orbit.local/",StringComparison.OrdinalIgnoreCase)&&!(uiTest&&mobileUiNavigation&&e.Uri.StartsWith("http://127.0.0.1:",StringComparison.OrdinalIgnoreCase)))e.Cancel=true; };
                core.NewWindowRequested+=delegate(object s,CoreWebView2NewWindowRequestedEventArgs e) {e.Handled=true;};
                core.PermissionRequested+=delegate(object s,CoreWebView2PermissionRequestedEventArgs e) {e.State=CoreWebView2PermissionState.Deny;};
                core.WebMessageReceived+=OnMessage;
                core.NavigationCompleted+=async delegate(object sender,CoreWebView2NavigationCompletedEventArgs e) {
                    if(uiTest && !mobileUiNavigation && e.IsSuccess) await UiTest.Run(this);
                    if(remoteTest && e.IsSuccess) await StartRemoteTestPane();
                };
                core.Navigate("https://orbit.local/index.html");
            }catch(Exception ex) { MessageBox.Show(this,"시작할 수 없습니다. WebView2 Runtime 설치와 web 폴더를 확인해 주세요.\n\n"+ex.Message,"Orbit");quitting=true;Close(); }
        }
        internal Task<string> ExecuteForTest(string script) { return web.CoreWebView2.ExecuteScriptAsync(script); }
        // The mobile UI test is deliberately scoped to its own loopback RemoteServer.
        // Production navigation remains limited to the virtual host above.
        internal void NavigateForMobileTest(string url) { mobileUiNavigation=true;web.CoreWebView2.Navigate(url); }
        internal Task<string> SetMobileColorSchemeForTest(string value) { return web.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia",new JavaScriptSerializer().Serialize(new {media="prefers-color-scheme",features=new[]{new {name="prefers-color-scheme",value=value}}})); }
        internal Task<string> SetMobileViewportForTest(int width,int height) { return web.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setDeviceMetricsOverride",new JavaScriptSerializer().Serialize(new {width=width,height=height,deviceScaleFactor=1,mobile=false,screenWidth=width,screenHeight=height})); }
        internal async Task CaptureMobileViewportForTest(string path) { var value=json.Deserialize<Dictionary<string,object>>(await web.CoreWebView2.CallDevToolsProtocolMethodAsync("Page.captureScreenshot",new JavaScriptSerializer().Serialize(new {format="png",captureBeyondViewport=false})));File.WriteAllBytes(path,Convert.FromBase64String((string)value["data"])); }
        private async Task StartRemoteTestPane() {
            try {
                for(int i=0;i<100;i++) { if((await ExecuteForTest("Boolean(window.orbitUiTest && window.orbitDiagnostics && window.orbitDiagnostics().ready)"))=="true")break; await Task.Delay(50); }
                await ExecuteForTest("window.orbitUiTest.startCmd().then(()=>window.orbitUiTest.send('echo ORBIT_REMOTE_TEST_READY\\r'))");
                for(int i=0;i<100;i++) { if((await ExecuteForTest("Boolean(window.orbitDiagnostics().sessions[0]&&window.orbitDiagnostics().sessions[0].pid)"))=="true")break; await Task.Delay(50); }
                await CaptureForTest(Path.Combine(TestArtifacts.Root,"remote-test-pane.png"));
                if(remoteTestTunnel) tunnel.Start(remote.Port);
            } catch(Exception ex) { File.WriteAllText(Path.Combine(TestArtifacts.Root,"remote-test-pane-error.txt"),ex.ToString()); }
        }
        internal void SetChromeMaximizedBounds(Rectangle bounds) { MaximizedBounds=bounds; }
        internal int[] GetTestWebViewProcessIds() { return environment.GetProcessInfos().Select(x=>x.ProcessId).ToArray(); }
        internal async Task CaptureForTest(string path) {
            using(var stream=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.None))
                await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);
        }
        internal void EndUiTest(bool failed) {
            quitting=true; Environment.ExitCode=failed?1:0;
            if(IsHandleCreated) BeginInvoke(new Action(Close)); else Close();
        }
        private void Send(object message) {
            if(quitting || IsDisposed || !IsHandleCreated)return;
            if(InvokeRequired) { try { BeginInvoke(new Action<object>(Send),message); }catch(InvalidOperationException){} return; }
            if(web.CoreWebView2!=null)web.CoreWebView2.PostWebMessageAsJson(json.Serialize(message));
        }
        private static string S(Dictionary<string,object> a,string key,string fallback="") {object v;return a.TryGetValue(key,out v) && v!=null?Convert.ToString(v):fallback;}
        private static int N(Dictionary<string,object> a,string key,int fallback=0) {object v;return a.TryGetValue(key,out v)?Convert.ToInt32(v):fallback;}
        private static long L(Dictionary<string,object> a,string key,long fallback=0) {object v;return a.TryGetValue(key,out v)?Convert.ToInt64(v):fallback;}
        private async void OnMessage(object sender,CoreWebView2WebMessageReceivedEventArgs e) {
            if(mobileUiNavigation) return;
            if(!e.Source.StartsWith("https://orbit.local/",StringComparison.OrdinalIgnoreCase))return;
            int id=0;
            try {
                var request=json.Deserialize<Dictionary<string,object>>(e.WebMessageAsJson);
                id=N(request,"id");string method=S(request,"method");
                var a=request.ContainsKey("args")?request["args"] as Dictionary<string,object>:new Dictionary<string,object>();
                object result=null;
                switch(method) {
                    case "init": result=new { folder=initialFolder,version=Updater.CurrentText(),settings=LoadSettings(),testMode=uiTest||remoteTest };break;
                    case "pickerPlaces":result=await Task.Run(()=>Files.PickerPlaces());break;
                    case "browse":result=await Task.Run(()=>Files.Browse(S(a,"path",initialFolder),S(a,"hidden")=="True",S(a,"directoriesOnly")=="True"));break;
                    case "createFile":result=await Task.Run(()=>Files.CreateFile(S(a,"path")));break;
                    case "list":result=await Task.Run(()=>Files.List(S(a,"path"),S(a,"hidden")=="True"));break;
                    case "read":result=await Task.Run(()=>Files.Read(Files.FullPath(S(a,"path"),S(a,"cwd",initialFolder))));break;
                    case "save":result=await Task.Run(()=>Files.Save(S(a,"path"),S(a,"content"),S(a,"encoding"),S(a,"revision",null)));break;
                    case "stat": {string p=Files.FullPath(S(a,"path"),S(a,"cwd",initialFolder));result=new { path=p,directory=Directory.Exists(p),exists=Directory.Exists(p)||File.Exists(p) };break;}
                    case "external":OpenExternal(S(a,"path"),S(a,"cwd",initialFolder));break;
                    case "clipboard":Clipboard.SetText(S(a,"text"));break;
                    case "clipboardRead":result=Clipboard.ContainsText()?Clipboard.GetText():"";break;
                    case "windowMinimize":chrome.Minimize();break;
                    case "windowMaximize":chrome.ToggleMaximize();break;
                    case "windowClose":chrome.CloseWindow();break;
                    case "dirty":dirty=S(a,"value")=="True";break;
                    case "settings":File.WriteAllText(Path.Combine(dataRoot,"settings.json"),json.Serialize(a));break;
                    case "theme":ApplyHostTheme(S(a,"value"));break;
                    case "savedSessions": {
                        long savedRequest=Interlocked.Increment(ref savedSessionRequest);
                        string workspace=S(a,"cwd",initialFolder); var saved=await Task.Run(()=>SessionHistory.List(workspace,S(a,"allWorkspaces")=="True"));
                        // Browser requests can finish out of order while a folder changes.
                        // Keep the cache that corresponds to the newest displayed list.
                        if(savedRequest==Interlocked.Read(ref savedSessionRequest)) { savedSessions.Clear(); foreach(var item in saved)savedSessions[item.Provider+":"+item.Id]=item; }
                        result=new {sessions=saved.Select(x=>new {provider=x.Provider,id=x.Id,title=x.Title,cwd=x.Cwd,updated=x.Updated.ToString("o")}).ToArray()};break;
                    }
                    case "createTerminal": {
                        string sid=S(a,"session");
                        if(string.IsNullOrWhiteSpace(sid))throw new InvalidOperationException("터미널 식별자가 없습니다.");
                        if(sessions.Count+pendingTerminals.Count>=8)throw new InvalidOperationException("동시에 8개까지 터미널을 열 수 있습니다.");
                        if(sessions.ContainsKey(sid) || !pendingTerminals.Add(sid))throw new InvalidOperationException("이미 생성 중인 터미널입니다.");
                        try {
                        string cwd=S(a,"cwd",initialFolder),profile=S(a,"profile","powershell"),resumeId=S(a,"resumeId");
                        if(profile!="codex"&&profile!="claude"&&profile!="powershell"&&profile!="cmd")throw new InvalidOperationException("Unsupported terminal profile.");
                        if(!String.IsNullOrEmpty(resumeId)) {
                            if((profile!="codex"&&profile!="claude")||!SessionHistory.IsId(resumeId))throw new InvalidOperationException("Invalid saved session.");
                            SavedSession saved;if(!savedSessions.TryGetValue(profile+":"+resumeId,out saved))throw new InvalidOperationException("Refresh saved sessions before resuming.");
                            cwd=saved.Cwd;
                        }
                        if(!Directory.Exists(cwd))throw new DirectoryNotFoundException("작업 폴더를 찾을 수 없습니다.");
                        string shell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell\\v1.0\\powershell.exe");
                        string command="\""+shell+"\" -NoLogo -NoProfile -NoExit";
                        if(profile=="cmd")command="\""+Environment.GetEnvironmentVariable("ComSpec")+"\" /d";
                        else {
                            string script="[Console]::InputEncoding = New-Object System.Text.UTF8Encoding; [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding; $OutputEncoding = [Console]::OutputEncoding; $env:TERM = 'xterm-256color'; $env:COLORTERM = 'truecolor'; ";
                            if(profile=="codex" || profile=="claude") {
                                if(String.IsNullOrEmpty(resumeId))script+="if (Get-Command "+profile+" -ErrorAction SilentlyContinue) { "+profile+" } else { Write-Host '"+profile+" CLI is not installed or is not on PATH.' -ForegroundColor Yellow }";
                                else if(profile=="codex")script+="if (Get-Command codex -ErrorAction SilentlyContinue) { codex -C '"+cwd.Replace("'","''")+"' resume "+resumeId+" } else { Write-Host 'codex CLI is not installed or is not on PATH.' -ForegroundColor Yellow }";
                                else script+="if (Get-Command claude -ErrorAction SilentlyContinue) { claude --resume "+resumeId+" } else { Write-Host 'claude CLI is not installed or is not on PATH.' -ForegroundColor Yellow }";
                            }
                            command+=" -EncodedCommand "+Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
                        }
                        outputRings[sid]=new OutputRing();
                        var terminal=new ConPty(sid,cwd,command,N(a,"cols",100),N(a,"rows",30),(key,chunk)=>{long seq=RecordOutput(key,chunk);Send(new {type="output",session=key,data=chunk,seq=seq});},(key,code)=> {
                            if(!quitting && IsHandleCreated)try {BeginInvoke(new Action(delegate { ConPty ignored;sessions.TryRemove(key,out ignored);string removedProfile;sessionProfiles.TryRemove(key,out removedProfile);string removedResume;sessionResumeIds.TryRemove(key,out removedResume);string removedName;sessionNames.TryRemove(key,out removedName);OutputRing ring;if(outputRings.TryRemove(key,out ring))ring.Close();Send(new {type="exit",session=key,code=code}); }));}catch(InvalidOperationException){}
                        });
                        if(!sessions.TryAdd(sid,terminal))throw new InvalidOperationException("이미 생성 중인 터미널입니다.");sessionProfiles[sid]=profile;if(!String.IsNullOrEmpty(resumeId))sessionResumeIds[sid]=profile+":"+resumeId;sessionNames[sid]=TerminalName(S(a,"name"),profile);terminal.Start();result=new {pid=terminal.ProcessId};
                        } finally { pendingTerminals.Remove(sid); }
                        break;
                    }
                    case "write":GetSession(S(a,"session")).Write(S(a,"data"));break;
                    case "ack": {ConPty terminal;if(sessions.TryGetValue(S(a,"session"),out terminal))terminal.Acknowledge();break;}
                    case "resize":GetSession(S(a,"session")).Resize(N(a,"cols",80),N(a,"rows",24));break;
                    case "closeTerminal": {string key=S(a,"session");ConPty terminal;if(sessions.TryRemove(key,out terminal)){string removedProfile;sessionProfiles.TryRemove(key,out removedProfile);string removedResume;sessionResumeIds.TryRemove(key,out removedResume);string removedName;sessionNames.TryRemove(key,out removedName);OutputRing ring;if(outputRings.TryRemove(key,out ring))ring.Close();terminal.Dispose();}break;}
                    case "deleteSavedSession": result=await Task.Run(()=>DeleteSavedSession(S(a,"provider"),S(a,"id"),S(a,"cwd",initialFolder),S(a,"allWorkspaces")=="True"));break;
                    case "terminalName": {string key=S(a,"session"),name=S(a,"name");if(sessions.ContainsKey(key))sessionNames[key]=TerminalName(name,"terminal");break;}
                    case "metrics":result=await Task.Run(()=>Measure());break;
                    case "remoteStatus":result=remote.Status();break;
                    case "remoteStart":result=new {url=remote.Start(N(a,"port",49821))};break;
                    case "remoteStop":tunnel.Stop();tailscale.Stop();remote.Stop();ClearRemoteRings();result=remote.Status();break;
                    case "remoteTunnelStart": tailscale.Stop();if(!remote.Enabled)remote.Start(N(a,"port",49821));tunnel.Start(remote.Port);result=new {status=remote.Status(),tunnel=tunnel.Status(),tailscale=tailscale.Status()};break;
                    case "remoteTunnelStatus":result=tunnel.Status();break;
                    case "remoteTailscaleStatus":result=tailscale.Status();break;
                    case "remoteTailscaleLogin":result=await Task.Run(()=>tailscale.Login());break;
                    case "remoteTailscaleStart":tunnel.Stop();if(!remote.Enabled)remote.Start(N(a,"port",49821));await Task.Run(()=>tailscale.Start(remote.Port));result=new {status=remote.Status(),tailscale=tailscale.Status(),tunnel=tunnel.Status()};break;
                    case "remoteConnectStart":tunnel.Stop();if(!remote.Enabled)remote.Start(N(a,"port",49821));if(!remote.Url().StartsWith("https:",StringComparison.OrdinalIgnoreCase))await Task.Run(()=>tailscale.Start(remote.Port));result=new {status=remote.Status(),tailscale=tailscale.Status(),tunnel=tunnel.Status()};break;
                    case "remoteTunnelStop":tunnel.Stop();result=remote.Status();break;
                    case "remotePublicOrigin":configuredRemoteOrigin=S(a,"url");RefreshRemoteOrigin();result=remote.Status();break;
                    case "updateCheck": {var check=await Task.Run(()=>Updater.Check());try{File.WriteAllText(Path.Combine(dataRoot,"update-check.log"),DateTime.Now.ToString("s")+" "+json.Serialize(check)+Environment.NewLine);}catch{}result=check;break;}
                    case "updateInstall":await Task.Run(()=>Updater.DownloadAndLaunch());result=new {ok=true};quitting=true;BeginInvoke(new Action(Close));break;
                    case "accountStatus":result=account.Status();break;
                    case "accountServiceUrl":account.ServiceUrl=S(a,"url");result=account.Status();break;
                    case "accountSignUp":result=await Task.Run(()=>account.SignUp(S(a,"email"),S(a,"password")));break;
                    case "accountLink":result=await Task.Run(()=>account.Link(S(a,"email"),S(a,"password")));break;
                    case "accountUnlink":result=await Task.Run(()=>account.Unlink());break;
                    case "remoteClientLogin":{string target=await Task.Run(()=>RemoteClientWindow.Login(S(a,"url"),S(a,"email"),S(a,"password")));RemoteClientWindow.Open(target,environment,Icon);result=new {ok=true};break;}
                    case "remoteClientOpen":RemoteClientWindow.Open(S(a,"url"),environment,Icon);result=new {ok=true};break;
                    case "remoteTailscaleSetup":OpenTailscaleSetup(S(a,"url"));result=new {ok=true};break;
                    case "remoteDiagnostic":result=await Task.Run(()=>RemoteDiagnostic());break;
                    case "remoteSnapshot": { RemoteSnapshot snapshot; string key=S(a,"request"); if(remoteSnapshots.TryGetValue(key,out snapshot)){snapshot.Data=S(a,"data");snapshot.Seq=L(a,"seq");snapshot.Cols=N(a,"cols");snapshot.Rows=N(a,"rows");try{snapshot.Ready.Set();}catch(ObjectDisposedException){}}break; }
                    case "remoteCreate": { RemoteCreate creationRequest;string key=S(a,"request");if(remoteCreates.TryGetValue(key,out creationRequest)){creationRequest.Session=S(a,"session");creationRequest.Error=S(a,"error");try{creationRequest.Ready.Set();}catch(ObjectDisposedException){}}break; }
                    case "remoteProjects": { RemoteProjects projectsRequest;string key=S(a,"request");if(remoteProjectsRequests.TryGetValue(key,out projectsRequest)){projectsRequest.List=a.ContainsKey("projects")?a["projects"]:new object[0];projectsRequest.Current=S(a,"current");try{projectsRequest.Ready.Set();}catch(ObjectDisposedException){}}break; }
                    default:throw new InvalidOperationException("지원하지 않는 요청입니다.");
                }
                if(id!=0)Send(new {id=id,result=result});
            }catch(Exception ex) {if(id!=0)Send(new {id=id,error=ex.Message});else Send(new {type="error",message=ex.Message});}
        }
        private ConPty GetSession(string id) {ConPty value;if(!sessions.TryGetValue(id,out value))throw new InvalidOperationException("종료된 터미널입니다.");return value;}
        private static int RemoteTestPort(string[] args) { string value=args.FirstOrDefault(x=>x.StartsWith("--remote-test-port=",StringComparison.OrdinalIgnoreCase));int port;if(value!=null&&Int32.TryParse(value.Substring("--remote-test-port=".Length),out port)&&port>=1024&&port<=65535)return port;return 49821; }
        private long RecordOutput(string id,string data) { OutputRing ring;if(!outputRings.TryGetValue(id,out ring))return 0;return ring.Advance(data,remote.Enabled); }
        private void RefreshRemoteOrigin() {var values=json.Deserialize<Dictionary<string,object>>(json.Serialize(tailscale.Status()));string ts=values.ContainsKey("url")?Convert.ToString(values["url"]):null;remote.SetPublicOrigin(!String.IsNullOrEmpty(ts)?ts:(!String.IsNullOrEmpty(tunnel.Url)?tunnel.Url:configuredRemoteOrigin));if(remote.Enabled&&!String.IsNullOrEmpty(remote.PublicOrigin))account.PushUrl(remote.Url());}
        private object RemoteDiagnostic() {var ts=tailscale.Status();var cf=tunnel.Status();string current=remote.PublicOrigin;bool checkedUrl=false,reachable=false;string error=null;Uri uri;if(Uri.TryCreate(current,UriKind.Absolute,out uri)&&uri.Scheme=="https"){checkedUrl=true;try{var request=(System.Net.HttpWebRequest)System.Net.WebRequest.Create(new Uri(uri.GetLeftPart(UriPartial.Authority)+"/mobile.html"));request.Method="GET";request.Timeout=3000;request.ReadWriteTimeout=3000;request.AllowAutoRedirect=false;using(var response=(System.Net.HttpWebResponse)request.GetResponse())reachable=(int)response.StatusCode>=200&&(int)response.StatusCode<400;}catch(Exception ex){error=ex.Message;}}return new {tailscale=ts,tunnel=cf,checkedUrl=checkedUrl,reachable=reachable,error=error};}
        private static void OpenTailscaleSetup(string value) {Uri uri;if(!Uri.TryCreate(value,UriKind.Absolute,out uri)||uri.Scheme!="https"||!(String.Equals(uri.Host,"tailscale.com",StringComparison.OrdinalIgnoreCase)||uri.Host.EndsWith(".tailscale.com",StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("허용되지 않은 Tailscale 주소입니다.");Process.Start(new ProcessStartInfo(uri.AbsoluteUri){UseShellExecute=true});}
        private void ClearRemoteRings() { foreach(var ring in outputRings.Values)ring.Clear(); }
        private static string TerminalName(string value,string fallback) { value=(value??"").Trim();return value.Length==0?fallback:value.Substring(0,Math.Min(value.Length,40)); }
        object IRemoteTerminals.Sessions() { return new {sessions=sessions.Values.Select(x=> { string profile,name; if(!sessionProfiles.TryGetValue(x.Id,out profile))profile="terminal"; if(!sessionNames.TryGetValue(x.Id,out name))name=profile; return new {id=x.Id,name=name,pid=x.ProcessId,profile=profile};}).ToArray()}; }
        object IRemoteTerminals.SavedSessions() { var saved=SessionHistory.List(initialFolder,true);return new {sessions=saved.Select(x=>new {provider=x.Provider,id=x.Id,title=x.Title,cwd=x.Cwd,updated=x.Updated.ToString("o")}).ToArray()}; }
        object IRemoteTerminals.DeleteSavedSession(string provider,string id) { return DeleteSavedSession(provider,id,initialFolder,true); }
        private object DeleteSavedSession(string provider,string id,string workspace,bool allWorkspaces) {
            if((provider!="codex"&&provider!="claude")||!SessionHistory.IsCanonicalId(id))throw new InvalidOperationException("Invalid saved session.");
            if(sessionResumeIds.Values.Any(x=>String.Equals(x,provider+":"+id,StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("Close the resumed terminal before permanently deleting this conversation.");
            SessionHistory.Delete(provider,id,workspace,allWorkspaces);
            var saved=SessionHistory.List(workspace,allWorkspaces);savedSessions.Clear();foreach(var item in saved)savedSessions[item.Provider+":"+item.Id]=item;
            return new {sessions=saved.Select(x=>new {provider=x.Provider,id=x.Id,title=x.Title,cwd=x.Cwd,updated=x.Updated.ToString("o")}).ToArray()};
        }
        object IRemoteTerminals.Snapshot(string id) { if(!sessions.ContainsKey(id))throw new InvalidOperationException("terminal not found");string key=id+":"+Interlocked.Increment(ref nextSnapshotId);var request=new RemoteSnapshot();if(!remoteSnapshots.TryAdd(key,request))throw new InvalidOperationException("snapshot busy");try {Send(new {type="remoteSnapshot",session=id,request=key});if(request.Ready.WaitOne(3000))return new {seq=request.Seq,data=request.Data,cols=request.Cols,rows=request.Rows,items=new object[0]};throw new TimeoutException("snapshot timed out");}finally{RemoteSnapshot ignored;remoteSnapshots.TryRemove(key,out ignored);request.Ready.Dispose();} }
        object IRemoteTerminals.Output(string id,long after,int timeoutMs) { if(!sessions.ContainsKey(id))throw new InvalidOperationException("terminal not found");OutputRing r;if(!outputRings.TryGetValue(id,out r))throw new InvalidOperationException("terminal not found");return r.After(after,timeoutMs); }
        object IRemoteTerminals.Create(string profile,string resumeId,string project) { SavedSession match=null;if(!String.IsNullOrEmpty(resumeId)){match=SessionHistory.List(initialFolder,true).FirstOrDefault(x=>x.Provider==profile&&x.Id.Equals(resumeId,StringComparison.OrdinalIgnoreCase));if(match==null)throw new InvalidOperationException("saved session is unavailable");} string key="create:"+Interlocked.Increment(ref nextSnapshotId);var request=new RemoteCreate();if(!remoteCreates.TryAdd(key,request))throw new InvalidOperationException("create busy");try{Send(new {type="remoteCreate",request=key,profile=profile,resumeId=resumeId,project=match==null?project:null,cwd=match==null?null:match.Cwd,title=match==null?null:match.Title});if(!request.Ready.WaitOne(10000))throw new TimeoutException("terminal creation timed out");if(!String.IsNullOrEmpty(request.Error))throw new InvalidOperationException(request.Error);if(String.IsNullOrEmpty(request.Session))throw new InvalidOperationException("terminal creation failed");return new {session=request.Session};}finally{RemoteCreate ignored;remoteCreates.TryRemove(key,out ignored);request.Ready.Dispose();} }
        object IRemoteTerminals.Projects() { string key="projects:"+Interlocked.Increment(ref nextSnapshotId);var request=new RemoteProjects();if(!remoteProjectsRequests.TryAdd(key,request))throw new InvalidOperationException("projects busy");try{Send(new {type="remoteProjects",request=key});if(!request.Ready.WaitOne(5000))throw new TimeoutException("projects timed out");return new {projects=request.List,current=request.Current};}finally{RemoteProjects ignored;remoteProjectsRequests.TryRemove(key,out ignored);request.Ready.Dispose();} }
        void IRemoteTerminals.Input(string id,string data) { GetSession(id).Write(data); }
        private sealed class RemoteSnapshot { public readonly ManualResetEvent Ready=new ManualResetEvent(false); public string Data=""; public long Seq; public int Cols,Rows; }
        private sealed class RemoteCreate { public readonly ManualResetEvent Ready=new ManualResetEvent(false); public string Session="",Error=""; }
        private sealed class RemoteProjects { public readonly ManualResetEvent Ready=new ManualResetEvent(false); public object List=new object[0]; public string Current=""; }
        private object LoadSettings() {try {return json.DeserializeObject(File.ReadAllText(Path.Combine(dataRoot,"settings.json")));}catch {return null;}}
        private string ReadSavedTheme() {
            try {
                var settings=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(dataRoot,"settings.json")));
                return S(settings,"theme","dark");
            } catch { return "dark"; }
        }
        private void ApplyHostTheme(string value) {
            BackColor=string.Equals(value,"light",StringComparison.OrdinalIgnoreCase) ? Color.FromArgb(246,244,250) : Color.FromArgb(19,21,26);
            web.DefaultBackgroundColor=BackColor;
        }
        private object Measure() {
            using(var p=Process.GetCurrentProcess()) {
                var start=p.TotalProcessorTime;var time=Stopwatch.StartNew();System.Threading.Thread.Sleep(1000);p.Refresh();
                return new { hostMemoryMb=Math.Round(p.WorkingSet64/1048576.0,1),hostCpu=Math.Round((p.TotalProcessorTime-start).TotalMilliseconds/time.Elapsed.TotalMilliseconds/Environment.ProcessorCount*100,2),note="앱 호스트만 측정 · WebView2와 CLI 프로세스 제외" };
            }
        }
        internal static void OpenExternal(string path,string cwd) {
            Uri uri;
            if(Uri.TryCreate(path,UriKind.Absolute,out uri) && (uri.Scheme=="http" || uri.Scheme=="https")) {Process.Start(new ProcessStartInfo(path) {UseShellExecute=true});return;}
            string full=Files.FullPath(path,cwd);
            if(!Directory.Exists(full) && !File.Exists(full))throw new FileNotFoundException("파일 또는 폴더를 찾을 수 없습니다.");
            if(new []{".com",".bat",".cmd",".ps1",".vbs",".js",".msi",".scr",".url",".hta"}.Contains(Path.GetExtension(full).ToLowerInvariant()) && !Directory.Exists(full))throw new InvalidOperationException("실행 파일은 터미널에서 실행해 주세요.");
            Process.Start(new ProcessStartInfo(full) {UseShellExecute=true,WorkingDirectory=Directory.Exists(full)?full:Path.GetDirectoryName(full)});
        }
    }
}
