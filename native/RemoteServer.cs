using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
namespace Orbit {
 internal interface IRemoteTerminals { object Sessions(); object SavedSessions(); object DeleteSavedSession(string provider,string id); object Snapshot(string id); object Output(string id,long after,int timeoutMs); object Create(string profile,string resumeId,string project=null); object Projects(); void Input(string id,string data); }
 internal sealed class RemoteServer:IDisposable {
  HttpListener listener=new HttpListener(); readonly IRemoteTerminals terminals; readonly JavaScriptSerializer json=new JavaScriptSerializer {MaxJsonLength=2*1024*1024};
  readonly ConcurrentDictionary<string,Device> devices=new ConcurrentDictionary<string,Device>(); readonly ConcurrentDictionary<string,Rate> rates=new ConcurrentDictionary<string,Rate>(); readonly ConcurrentDictionary<string,Receipt> receipts=new ConcurrentDictionary<string,Receipt>(); readonly string serverEpoch=Guid.NewGuid().ToString("N");
  readonly string deviceFile; readonly object lifecycle=new object(),persistence=new object(); bool running; int active; long rateSweepTicks; const int MaxBody=65536,DeskMaxBody=1900000,MaxClients=12;
  sealed class Device {public string Hash,Name;public DateTime Added;}
  sealed class Rate {public DateTime Window=DateTime.UtcNow;public int Count;}
  sealed class Receipt {public string Session,Payload;public DateTime Expires;public volatile int State;}
  public int Port{get;private set;} public string PublicOrigin{get;private set;} public event Action Changed;
  public bool Enabled { get { lock(lifecycle)return running; } }
  public RemoteServer(IRemoteTerminals value,string storage){terminals=value;deviceFile=storage;Load();}
  public string Start(int port){lock(lifecycle){if(running)return Url();Port=port;listener=new HttpListener();listener.Prefixes.Add("http://127.0.0.1:"+port+"/");listener.Start();running=true;BeginAccept();}Raise();return Url();}
  public void Stop(){lock(lifecycle){if(!running)return;running=false;try{listener.Stop();listener.Close();}catch{}}Raise();}
  public void SetPublicOrigin(string value){Uri u;PublicOrigin=Uri.TryCreate(value,UriKind.Absolute,out u)&&u.Scheme=="https"?u.GetLeftPart(UriPartial.Authority):null;Raise();}
  public string Url(){return OriginUrl()+"/mobile.html";}
  // What the account service must store: clients append /mobile.html and /api/v1/... themselves.
  // The token behind the QR shown in the host UI. One QR token is valid at a time: rendering it again reuses it, renewing revokes the old one.
  readonly object qrLock=new object();
  public string QrUrl(bool renew){
   lock(qrLock){
    string file=Path.Combine(Path.GetDirectoryName(deviceFile),"remote-qr.token"),token=null;
    try{if(File.Exists(file))token=File.ReadAllText(file).Trim();}catch{}
    if(renew||String.IsNullOrEmpty(token)||!devices.ContainsKey(Hash(token))){
     if(!String.IsNullOrEmpty(token))RevokeToken(token);
     token=IssueAccountToken("QR 접속");
     if(token==null)throw new IOException("기기 등록 정보를 저장하지 못했습니다.");
     try{File.WriteAllText(file,token);}catch{}
    }
    return Url()+"?login="+Uri.EscapeDataString(token);
   }
  }
  public string OriginUrl(){return PublicOrigin??("http://127.0.0.1:"+Port);}
  public object Status(){return new {enabled=running,url=running?Url():null,devices=Devices()};}
  public object Devices(){return devices.Values.OrderByDescending(x=>x.Added).Select(x=>new {id=x.Hash,name=x.Name,added=x.Added.ToString("o")}).ToArray();}
  // Issues a fresh bearer token for an account-login-based client and registers it exactly like a
  // previously-approved device, so RemoteServer.Auth() needs no changes to trust it.
  public string IssueAccountToken(string name="계정 로그인"){string token=Token(32),hash=Hash(token);lock(lifecycle){devices[hash]=new Device{Hash=hash,Name=Clean(name,80,"계정 로그인"),Added=DateTime.UtcNow};if(!Save()){Device removed;devices.TryRemove(hash,out removed);return null;}}Raise();return token;}
  public static string HashToken(string token){return Hash(token);}
  public void RevokeToken(string token){if(String.IsNullOrEmpty(token))return;Revoke(Hash(token));}
  public void Revoke(string hash){Device d;if(devices.TryRemove(hash,out d)){if(!Save()){devices[hash]=d;throw new IOException("device store could not be saved");}}Raise();}
  void BeginAccept(){try{listener.BeginGetContext(Accepted,null);}catch(ObjectDisposedException){}catch(HttpListenerException){}}
  void Accepted(IAsyncResult ar){HttpListenerContext c=null;try{c=listener.EndGetContext(ar);if(running)BeginAccept();if(Interlocked.Increment(ref active)>MaxClients){try{Reply(c,429,new {error="too many clients"});}finally{Interlocked.Decrement(ref active);}return;}ThreadPool.QueueUserWorkItem(_=>{try{Handle(c);}catch(Exception ex){try{Reply(c,400,new {error=Safe(ex.Message)});}catch{}}finally{Interlocked.Decrement(ref active);}});}catch{}}
  void Handle(HttpListenerContext c) {
   Security(c);if(!AllowedOrigin(c)){Reply(c,403,new {error="origin not allowed"});return;}
   string path=c.Request.Url.AbsolutePath;if(!path.StartsWith("/api/v1/",StringComparison.Ordinal)){Static(c,path);return;}
   if(c.Request.HttpMethod!="POST"){Reply(c,405,new {error="POST required"});return;}
   string op=path.Substring(8).Trim('/');bool desk=op.StartsWith("desk/",StringComparison.Ordinal);
   Dictionary<string,object>a;Device device;
   // Desktop-bridge bodies may carry file contents, so authenticate before accepting the larger body.
   if(desk){device=Auth(c);if(device==null){Reply(c,401,new {error="unauthorized"});return;}a=Parse(Body(c,DeskMaxBody));}
   else{a=Parse(Body(c));device=Auth(c);if(device==null){Reply(c,401,new {error="unauthorized"});return;}}
   if(!RateOk("device:"+device.Hash,desk?6000:900)){Reply(c,429,new {error="rate limited"});return;}
   if(desk){DeskRoute(c,a,op,device);return;}
   if(op=="sessions"){Reply(c,200,WithEpoch(terminals.Sessions()));return;}
   if(op=="saved-sessions"){Reply(c,200,WithEpoch(terminals.SavedSessions()));return;}
   if(op=="saved-sessions/delete"){string provider=S(a,"provider"),id=S(a,"id");if((provider!="codex"&&provider!="claude")||!SessionHistory.IsCanonicalId(id)){Reply(c,400,new {error="invalid saved session"});return;}Reply(c,200,WithEpoch(terminals.DeleteSavedSession(provider,id)));return;}
   if(op=="projects"){Reply(c,200,WithEpoch(terminals.Projects()));return;}
   if(op=="terminals/create"){string profile=S(a,"profile"),resumeId=S(a,"resumeId"),project=S(a,"project");if(profile!="codex"&&profile!="claude"&&profile!="powershell"&&profile!="cmd"){Reply(c,400,new {error="invalid profile"});return;}if(!String.IsNullOrEmpty(resumeId)&&((profile!="codex"&&profile!="claude")||!SessionHistory.IsId(resumeId))){Reply(c,400,new {error="invalid saved session"});return;}if(project.Length>1024){Reply(c,400,new {error="invalid project"});return;}Reply(c,200,terminals.Create(profile,resumeId,project));return;}
   if(op=="snapshot"){object value=terminals.Snapshot(S(a,"session"));if(Auth(c)==null){Reply(c,401,new {error="revoked"});return;}Reply(c,200,WithEpoch(value));return;}
   if(op=="output"){object value=terminals.Output(S(a,"session"),L(a,"seq"),25000);if(Auth(c)==null){Reply(c,401,new {error="revoked"});return;}Reply(c,200,WithEpoch(value));return;}
   if(op=="input/status"){InputStatus(c,a,device);return;}
   if(op=="input"){Input(c,a,device);return;}
   Reply(c,404,new {error="unknown api"});
  }
  void DeskRoute(HttpListenerContext c,Dictionary<string,object> a,string op,Device device){
   var bridge=terminals as IDeskBridge;if(bridge==null){Reply(c,404,new {error="unknown api"});return;}
   if(op=="desk/call"){
    try{var args=a.ContainsKey("args")?a["args"] as Dictionary<string,object>:null;Reply(c,200,new {result=bridge.DeskCall(device.Hash,S(a,"method"),args)});}
    catch(Exception ex){Reply(c,200,new {error=ex.Message});}return;}
   if(op=="desk/events"){object value=bridge.DeskEvents(device.Hash,L(a,"after"),25000);if(Auth(c)==null){Reply(c,401,new {error="revoked"});return;}Reply(c,200,value);return;}
   Reply(c,404,new {error="unknown api"});
  }
  object WithEpoch(object value){var map=json.Deserialize<Dictionary<string,object>>(json.Serialize(value))??new Dictionary<string,object>();map["serverEpoch"]=serverEpoch;return map;}
  void Input(HttpListenerContext c,Dictionary<string,object>a,Device device){string data=S(a,"data"),session=S(a,"session"),request=S(a,"requestId"),clientEpoch=S(a,"serverEpoch");if(data.Length==0||data.Length>8192)throw new InvalidOperationException("invalid input size");if(String.IsNullOrEmpty(request)){terminals.Input(session,data);Reply(c,200,new {ok=true,serverEpoch=serverEpoch});return;}if(!Fixed(clientEpoch,serverEpoch)){Reply(c,409,new {error="server epoch changed",serverEpoch=serverEpoch});return;}DateTime expires;if(!FreshRequest(request,out expires)){Reply(c,409,new {error="request receipt expired",serverEpoch=serverEpoch});return;}string key=device.Hash+":"+request,payload=Hash(session+"\n"+data);Receipt receipt;if(receipts.TryGetValue(key,out receipt)){if(!Fixed(receipt.Session,session)||!Fixed(receipt.Payload,payload)){Reply(c,409,new {error="request id payload mismatch",serverEpoch=serverEpoch});return;}Reply(c,receipt.State==1?200:409,new {ok=receipt.State==1,status=receipt.State==1?"confirmed":"indeterminate",serverEpoch=serverEpoch});return;}CleanupReceipts(device.Hash);if(ReceiptCount(device.Hash)>=2048){Reply(c,429,new {error="too many input receipts"});return;}receipt=new Receipt{Session=session,Payload=payload,Expires=expires};if(!receipts.TryAdd(key,receipt)){Input(c,a,device);return;}try{terminals.Input(session,data);}catch{receipt.State=2;throw;}receipt.State=1;Reply(c,200,new {ok=true,status="confirmed",serverEpoch=serverEpoch});}
  void InputStatus(HttpListenerContext c,Dictionary<string,object>a,Device device){string request=S(a,"requestId"),session=S(a,"session"),data=S(a,"data"),clientEpoch=S(a,"serverEpoch");if(!Fixed(clientEpoch,serverEpoch)){Reply(c,409,new {error="server epoch changed",serverEpoch=serverEpoch});return;}if(!FreshRequest(request)){Reply(c,409,new {error="request receipt expired",serverEpoch=serverEpoch});return;}Receipt receipt;if(!receipts.TryGetValue(device.Hash+":"+request,out receipt)){Reply(c,404,new {status="unknown",serverEpoch=serverEpoch});return;}if(!Fixed(receipt.Session,session)||!Fixed(receipt.Payload,Hash(session+"\n"+data))){Reply(c,409,new {error="request id payload mismatch",serverEpoch=serverEpoch});return;}Reply(c,200,new {status=receipt.State==1?"confirmed":"indeterminate",serverEpoch=serverEpoch});}
  static bool FreshRequest(string value){DateTime ignored;return FreshRequest(value,out ignored);}static bool FreshRequest(string value,out DateTime expires){expires=DateTime.MinValue;if(String.IsNullOrEmpty(value)||value.Length>96)return false;int dash=value.IndexOf('-');long ms;if(dash<1||!Int64.TryParse(value.Substring(0,dash),out ms))return false;DateTime created;try{created=new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddMilliseconds(ms);}catch{return false;}double age=(DateTime.UtcNow-created).TotalMilliseconds;if(age< -120000||age>600000)return false;try{expires=created.AddMinutes(10);}catch{return false;}return true;}
  void CleanupReceipts(string device){foreach(var pair in receipts)if(pair.Key.StartsWith(device+":",StringComparison.Ordinal)&&pair.Value.Expires<DateTime.UtcNow){Receipt removed;receipts.TryRemove(pair.Key,out removed);}}
  int ReceiptCount(string device){return receipts.Keys.Count(x=>x.StartsWith(device+":",StringComparison.Ordinal));}
  Device Auth(HttpListenerContext c){string h=c.Request.Headers["Authorization"]??"";if(!h.StartsWith("Bearer ",StringComparison.Ordinal)||h.Length>256)return null;Device d;return devices.TryGetValue(Hash(h.Substring(7)),out d)?d:null;}
  bool AllowedOrigin(HttpListenerContext c){string o=c.Request.Headers["Origin"];if(String.IsNullOrEmpty(o))return true;Uri u;if(!Uri.TryCreate(o,UriKind.Absolute,out u))return false;string local="http://127.0.0.1:"+Port;return String.Equals(o.TrimEnd('/'),local,StringComparison.OrdinalIgnoreCase)||(!String.IsNullOrEmpty(PublicOrigin)&&String.Equals(o.TrimEnd('/'),PublicOrigin,StringComparison.OrdinalIgnoreCase));}
  // iOS keeps a home-screen web app's storage apart from Safari's, so the app cannot see the token Safari saved. Put the
  // token of a valid login into the manifest's start_url instead; the app then signs itself in on its first launch.
  string Manifest(string text,string token){
   var map=json.Deserialize<Dictionary<string,object>>(text)??new Dictionary<string,object>();
   map["id"]="/mobile.html";
   if(!String.IsNullOrEmpty(token)&&token.Length<=128&&devices.ContainsKey(Hash(token)))map["start_url"]="/mobile.html?login="+Uri.EscapeDataString(token);
   return json.Serialize(map);
  }
  void Static(HttpListenerContext c,string p){if(c.Request.HttpMethod!="GET"&&c.Request.HttpMethod!="HEAD"){Reply(c,405,new {error="GET required"});return;}if(p=="/")p="/mobile.html";bool allowed=p=="/mobile.html"||p=="/mobile.js"||p=="/mobile.css"||p=="/mobile.webmanifest"||p=="/mobile-sw.js"||p=="/orbit.svg"||p=="/orbit-180.png"||p=="/orbit-192.png"||p=="/orbit-512.png"||(p.StartsWith("/chunks/",StringComparison.Ordinal)&&!p.Contains(".."));if(!allowed){Reply(c,404,new {error="not found"});return;}string file=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"web",p.TrimStart('/').Replace('/','\\'))),root=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"web"));if(!file.StartsWith(root+"\\",StringComparison.OrdinalIgnoreCase)||!File.Exists(file)){Reply(c,404,new {error="not found"});return;}string ext=Path.GetExtension(file).ToLowerInvariant();if(ext!=".html"&&ext!=".js"&&ext!=".css"&&ext!=".webmanifest"&&ext!=".svg"&&ext!=".png"){Reply(c,404,new {error="not found"});return;}if(ext==".webmanifest"){byte[] manifest=Encoding.UTF8.GetBytes(Manifest(File.ReadAllText(file),c.Request.QueryString["login"]));c.Response.ContentType="application/manifest+json";c.Response.Headers["Cache-Control"]="no-store";if(c.Request.HttpMethod=="GET")c.Response.OutputStream.Write(manifest,0,manifest.Length);c.Response.Close();return;}c.Response.ContentType=ext==".js"?"text/javascript":ext==".css"?"text/css":ext==".webmanifest"?"application/manifest+json":ext==".svg"?"image/svg+xml":ext==".png"?"image/png":"text/html";c.Response.Headers["Cache-Control"]="no-store";if(c.Request.HttpMethod=="GET"){byte[]b=File.ReadAllBytes(file);c.Response.OutputStream.Write(b,0,b.Length);}c.Response.Close();}
  static void Security(HttpListenerContext c){c.Response.Headers["Cache-Control"]="no-store, max-age=0";c.Response.Headers["Pragma"]="no-cache";c.Response.Headers["X-Content-Type-Options"]="nosniff";c.Response.Headers["X-Frame-Options"]="DENY";c.Response.Headers["Referrer-Policy"]="no-referrer";c.Response.Headers["Content-Security-Policy"]="default-src 'self'; connect-src 'self'; style-src 'self' 'unsafe-inline'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'";}
  static Dictionary<string,object> Parse(string text){try{return new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(text)??new Dictionary<string,object>();}catch{throw new InvalidOperationException("invalid JSON");}}
  static string Body(HttpListenerContext c,int limit=MaxBody){if(!String.Equals(c.Request.ContentType??"","application/json",StringComparison.OrdinalIgnoreCase)||c.Request.ContentLength64<2||c.Request.ContentLength64>limit)throw new InvalidOperationException("invalid request body");using(var r=new StreamReader(c.Request.InputStream,Encoding.UTF8,false,limit)){string s=r.ReadToEnd();if(Encoding.UTF8.GetByteCount(s)>limit)throw new InvalidOperationException("request too large");return s;}}
  void Reply(HttpListenerContext c,int status,object value){byte[]b=Encoding.UTF8.GetBytes(json.Serialize(value));c.Response.StatusCode=status;c.Response.ContentType="application/json; charset=utf-8";c.Response.ContentLength64=b.Length;c.Response.OutputStream.Write(b,0,b.Length);c.Response.Close();}
  bool RateOk(string key,int limit){SweepRatesIfDue();Rate r=rates.GetOrAdd(key,_=>new Rate());lock(r){if((DateTime.UtcNow-r.Window).TotalMinutes>=1){r.Window=DateTime.UtcNow;r.Count=0;}return ++r.Count<=limit;}}
  void SweepRatesIfDue(){long now=DateTime.UtcNow.Ticks,last=Interlocked.Read(ref rateSweepTicks);if(now-last<TimeSpan.FromMinutes(5).Ticks||Interlocked.CompareExchange(ref rateSweepTicks,now,last)!=last)return;DateTime cutoff=DateTime.UtcNow.AddMinutes(-5);foreach(var pair in rates)if(pair.Value.Window<cutoff){Rate removed;rates.TryRemove(pair.Key,out removed);}}
  static string S(Dictionary<string,object>a,string k){object v;return a!=null&&a.TryGetValue(k,out v)&&v!=null?Convert.ToString(v):"";}static long L(Dictionary<string,object>a,string k){object v;return a!=null&&a.TryGetValue(k,out v)?Convert.ToInt64(v):0;}static string Token(int n){byte[]b=new byte[n];using(var r=RandomNumberGenerator.Create())r.GetBytes(b);return Convert.ToBase64String(b).Replace('+','-').Replace('/','_').TrimEnd('=');}static string Hash(string v){using(var s=SHA256.Create())return Convert.ToBase64String(s.ComputeHash(Encoding.UTF8.GetBytes(v)));}static bool Fixed(string a,string b){if(a==null||b==null)return false;byte[]x=Encoding.UTF8.GetBytes(a),y=Encoding.UTF8.GetBytes(b);if(x.Length!=y.Length)return false;int d=0;for(int i=0;i<x.Length;i++)d|=x[i]^y[i];return d==0;}static string Clean(string s,int max,string fallback){s=(s??"").Trim();return s.Length==0?fallback:s.Substring(0,Math.Min(max,s.Length));}static string Safe(string s){return String.IsNullOrEmpty(s)?"request failed":s.Substring(0,Math.Min(s.Length,120));}
  void Load(){try{foreach(Device d in json.Deserialize<Device[]>(File.ReadAllText(deviceFile))??new Device[0])if(!String.IsNullOrEmpty(d.Hash))devices[d.Hash]=d;}catch{}}bool Save(){lock(persistence)try{string folder=Path.GetDirectoryName(deviceFile);if(!String.IsNullOrEmpty(folder))Directory.CreateDirectory(folder);string tmp=deviceFile+".tmp";File.WriteAllText(tmp,json.Serialize(devices.Values.ToArray()));if(File.Exists(deviceFile))File.Replace(tmp,deviceFile,null);else File.Move(tmp,deviceFile);return true;}catch{return false;}}
  void Raise(){var h=Changed;if(h!=null)h();}public void Dispose(){Stop();}
 }
}
