using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Orbit {
 // Lets this Orbit act as a client of another PC: shows the account service's
 // login page (which redirects to the host's public mobile UI) in its own
 // window. Deliberately has no WebMessageReceived bridge, so remote content can
 // never call into the local desktop API.
 internal sealed class RemoteClientWindow:Form {
  static RemoteClientWindow current;
  // Without this every POST waits ~1s for a "100 Continue" that Tailscale Funnel's proxy never sends, and only two
  // connections per host are allowed, so one pending long poll would queue everything else behind a single connection.
  static RemoteClientWindow(){Tune();}
  internal static void Tune(){
   System.Net.ServicePointManager.Expect100Continue=false;
   System.Net.ServicePointManager.UseNagleAlgorithm=false;
   System.Net.ServicePointManager.DefaultConnectionLimit=32;
  }
  readonly WebView2 web=new WebView2();
  RemoteClientWindow(string url,CoreWebView2Environment environment,Icon icon) {
   Text="Orbit 원격 접속";Width=480;Height=860;StartPosition=FormStartPosition.CenterScreen;if(icon!=null)Icon=icon;
   web.Dock=DockStyle.Fill;Controls.Add(web);
   Load+=async delegate {
    try {
     await web.EnsureCoreWebView2Async(environment);
     var core=web.CoreWebView2;
     core.Settings.AreDevToolsEnabled=false;core.Settings.AreDefaultContextMenusEnabled=false;
     core.NavigationStarting+=delegate(object s,CoreWebView2NavigationStartingEventArgs e){if(!Allowed(e.Uri))e.Cancel=true;};
     core.NewWindowRequested+=delegate(object s,CoreWebView2NewWindowRequestedEventArgs e){e.Handled=true;};
     core.PermissionRequested+=delegate(object s,CoreWebView2PermissionRequestedEventArgs e){e.State=CoreWebView2PermissionState.Deny;};
     core.DocumentTitleChanged+=delegate{if(!String.IsNullOrEmpty(core.DocumentTitle))Text=core.DocumentTitle;};
     core.Navigate(url);
    } catch(Exception ex) { MessageBox.Show(this,"원격 접속 창을 열 수 없습니다.\n\n"+ex.Message,"Orbit");Close(); }
   };
   FormClosed+=delegate{if(current==this)current=null;web.Dispose();};
  }
  // Signs in at the account service from inside the app (nothing is stored) and
  // returns the linked PC's mobile URL with the device token in the fragment,
  // exactly like the web login page does. Throws with a user-facing message.
  internal sealed class Session {
   public string Base,Token;
   public string MobileUrl(string project){return Base+"/mobile.html#login="+Uri.EscapeDataString(Token)+(String.IsNullOrEmpty(project)?"":"&project="+Uri.EscapeDataString(project));}
  }
  // The linked PC's project list, i.e. what its Home orbit shows.
  public static object Projects(Session session) {
   var json=new JavaScriptSerializer();
   try {
    var request=(HttpWebRequest)WebRequest.Create(session.Base+"/api/v1/projects");
    request.Method="POST";request.ContentType="application/json";request.Timeout=15000;request.ReadWriteTimeout=15000;request.AllowAutoRedirect=false;request.ServicePoint.Expect100Continue=false;
    request.Headers["Authorization"]="Bearer "+session.Token;
    byte[] bytes=Encoding.UTF8.GetBytes("{}");request.ContentLength=bytes.Length;
    using(var stream=request.GetRequestStream())stream.Write(bytes,0,bytes.Length);
    using(var response=(HttpWebResponse)request.GetResponse())using(var reader=Reader(response))return json.Deserialize<Dictionary<string,object>>(reader.ReadToEnd());
   } catch(WebException ex) {
    var r=ex.Response as HttpWebResponse;
    if(r!=null&&(int)r.StatusCode==401)throw new InvalidOperationException("호스트 PC가 이 로그인을 거부했습니다. 호스트에서 계정을 다시 연결했는지 확인해 주세요.");
    // Keep the real reason (DNS, TLS trust, timeout, proxy...) so a failure on this PC's network is not mistaken for an offline host.
    string detail=r!=null?"HTTP "+(int)r.StatusCode:ex.Status+": "+(ex.InnerException!=null?ex.InnerException.Message:ex.Message);
    throw new InvalidOperationException("호스트 PC에 연결하지 못했습니다. 호스트의 Orbit이 켜져 있고 외부 주소가 준비됐는지, 이 PC에서 "+new Uri(session.Base).Host+" 에 접속할 수 있는지 확인해 주세요.\n("+detail+")");
   }
  }
  // Calls into the host's desktop bridge (see DeskBridge.cs). Errors carry a message the UI can show as is.
  public static object DeskCall(Session session,string method,Dictionary<string,object> args) {
   var reply=DeskPost(session,"desk/call",new Dictionary<string,object>{{"method",method},{"args",args??new Dictionary<string,object>()}},60000);
   object error,result;
   if(reply.TryGetValue("error",out error)&&error!=null)throw new InvalidOperationException(Convert.ToString(error));
   reply.TryGetValue("result",out result);return result;
  }
  public static Dictionary<string,object> DeskPost(Session session,string op,Dictionary<string,object> body,int timeoutMs) {
   var json=new JavaScriptSerializer{MaxJsonLength=16*1024*1024};
   try {
    var request=(HttpWebRequest)WebRequest.Create(session.Base+"/api/v1/"+op);
    request.Method="POST";request.ContentType="application/json";request.Timeout=timeoutMs;request.ReadWriteTimeout=timeoutMs;request.AllowAutoRedirect=false;request.ServicePoint.Expect100Continue=false;
    request.Headers["Authorization"]="Bearer "+session.Token;
    byte[] bytes=Encoding.UTF8.GetBytes(json.Serialize(body));request.ContentLength=bytes.Length;
    using(var stream=request.GetRequestStream())stream.Write(bytes,0,bytes.Length);
    using(var response=(HttpWebResponse)request.GetResponse())using(var reader=Reader(response))return json.Deserialize<Dictionary<string,object>>(reader.ReadToEnd())??new Dictionary<string,object>();
   } catch(WebException ex) {
    var r=ex.Response as HttpWebResponse;
    if(r!=null&&(int)r.StatusCode==401)throw new InvalidOperationException("호스트 PC가 이 로그인을 거부했습니다. 다시 로그인해 주세요.");
    if(r!=null&&(int)r.StatusCode==404)throw new InvalidOperationException("호스트 Orbit이 원격 작업 화면을 지원하지 않는 버전입니다. 호스트 PC의 Orbit도 최신 버전으로 업데이트해 주세요.");
    if(r!=null&&(int)r.StatusCode==429)throw new InvalidOperationException("요청이 너무 많습니다. 잠시 후 다시 시도해 주세요.");
    throw new InvalidOperationException("호스트 PC에 연결하지 못했습니다: "+(r!=null?"HTTP "+(int)r.StatusCode:ex.Status+" "+(ex.InnerException!=null?ex.InnerException.Message:ex.Message)));
   }
  }
  public static Session Login(string serviceUrl,string email,string password) {
   serviceUrl=(serviceUrl??"").Trim().TrimEnd('/');
   if(serviceUrl.Length>0&&serviceUrl.IndexOf("://",StringComparison.Ordinal)<0)serviceUrl="https://"+serviceUrl;
   Uri service;
   if(!Uri.TryCreate(serviceUrl,UriKind.Absolute,out service)||service.Scheme!=Uri.UriSchemeHttps)throw new InvalidOperationException("https:// 로 시작하는 계정 서비스 주소를 입력해 주세요.");
   if(String.IsNullOrWhiteSpace(email)||String.IsNullOrEmpty(password))throw new InvalidOperationException("이메일과 비밀번호를 입력해 주세요.");
   var json=new JavaScriptSerializer();Dictionary<string,object> body;
   try {
    var request=(HttpWebRequest)WebRequest.Create(service.GetLeftPart(UriPartial.Authority)+service.AbsolutePath.TrimEnd('/')+"/login");
    request.Method="POST";request.ContentType="application/json";request.Timeout=10000;request.ReadWriteTimeout=10000;request.AllowAutoRedirect=false;request.ServicePoint.Expect100Continue=false;
    byte[] bytes=Encoding.UTF8.GetBytes(json.Serialize(new Dictionary<string,object>{{"email",email.Trim()},{"password",password}}));
    request.ContentLength=bytes.Length;
    using(var stream=request.GetRequestStream())stream.Write(bytes,0,bytes.Length);
    using(var response=(HttpWebResponse)request.GetResponse())using(var reader=Reader(response))body=json.Deserialize<Dictionary<string,object>>(reader.ReadToEnd());
   } catch(WebException ex) {
    var errorResponse=ex.Response as HttpWebResponse;
    if(errorResponse==null)throw new InvalidOperationException("계정 서비스에 연결하지 못했습니다: "+ex.Message);
    int status=(int)errorResponse.StatusCode;string code="";
    try{using(errorResponse)using(var reader=new StreamReader(errorResponse.GetResponseStream())){object v;var e=json.Deserialize<Dictionary<string,object>>(reader.ReadToEnd());if(e!=null&&e.TryGetValue("error",out v))code=Convert.ToString(v);}}catch{}
    string known=LoginError(code);
    if(known==null)throw new InvalidOperationException("로그인하지 못했습니다. 계정 서비스("+service.Host+")가 예상과 다르게 응답했습니다. (HTTP "+status+")");
    throw new InvalidOperationException(known);
   }
   object hostValue=null,tokenValue=null;
   if(body!=null){body.TryGetValue("url",out hostValue);body.TryGetValue("deviceToken",out tokenValue);}
   string host=Convert.ToString(hostValue),token=Convert.ToString(tokenValue);Uri hostUri;
   if(String.IsNullOrEmpty(token)||!Uri.TryCreate(host,UriKind.Absolute,out hostUri)||hostUri.Scheme!=Uri.UriSchemeHttps)throw new InvalidOperationException("계정 서비스 응답이 올바르지 않습니다.");
   string path=System.Text.RegularExpressions.Regex.Replace(hostUri.AbsolutePath.TrimEnd('/'),@"/mobile\.html$","",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
   return new Session{Base=hostUri.GetLeftPart(UriPartial.Authority)+path.TrimEnd('/'),Token=token};
  }
  static string LoginError(string code) {
   switch(code) {
    case "invalid_email":return "이메일 형식이 올바르지 않습니다.";
    case "invalid_credentials":return ClientCredentials.InvalidCredentials;
    case "no_pc_linked":return "이 계정에 연결된 PC가 없습니다. 접속받을 PC의 Orbit에서 먼저 계정을 연결하세요.";
    case "rate_limited":return "잠시 후 다시 시도해 주세요.";
    default:return null;
   }
  }
  // Redirects are not followed (they would turn the POST into a GET); report them instead of misparsing an empty body.
  static StreamReader Reader(HttpWebResponse response){if((int)response.StatusCode>=300)throw new InvalidOperationException("주소가 다른 곳으로 이동시켰습니다. 계정 서비스 주소가 올바른지 확인해 주세요. (HTTP "+(int)response.StatusCode+")");return new StreamReader(response.GetResponseStream());}
  static bool Allowed(string value){Uri u;return Uri.TryCreate(value,UriKind.Absolute,out u)&&u.Scheme==Uri.UriSchemeHttps;}
  public static void Open(string value,CoreWebView2Environment environment,Icon icon) {
   value=(value??"").Trim();
   if(value.Length>0&&value.IndexOf("://",StringComparison.Ordinal)<0)value="https://"+value;
   Uri uri;
   if(!Uri.TryCreate(value,UriKind.Absolute,out uri)||uri.Scheme!=Uri.UriSchemeHttps)throw new InvalidOperationException("https:// 로 시작하는 계정 서비스 주소를 입력해 주세요.");
   // A page that only differs in its fragment would not reload, so replace the window.
   CloseCurrent();
   current=new RemoteClientWindow(uri.AbsoluteUri,environment,icon);
   current.Show();
  }
  public static void CloseCurrent(){var w=current;current=null;if(w!=null&&!w.IsDisposed)w.Close();}
 }
 // "Auto login": remembers the client login on this PC, encrypted with the
 // Windows user's DPAPI key (unreadable by other users or other machines).
 internal static class ClientCredentials {
  const string Entropy="Orbit.ClientLogin.v1";
  public const string InvalidCredentials="이메일 또는 비밀번호가 올바르지 않습니다.";
  public static void Save(string path,string url,string email,string password) {
   try {
    string text=new JavaScriptSerializer().Serialize(new Dictionary<string,object>{{"url",url},{"email",email},{"password",password}});
    byte[] data=ProtectedData.Protect(Encoding.UTF8.GetBytes(text),Encoding.UTF8.GetBytes(Entropy),DataProtectionScope.CurrentUser);
    Directory.CreateDirectory(Path.GetDirectoryName(path));
    string tmp=path+".tmp";File.WriteAllBytes(tmp,data);if(File.Exists(path))File.Delete(path);File.Move(tmp,path);
   } catch { }
  }
  public static Dictionary<string,object> Load(string path) {
   try {
    if(!File.Exists(path))return null;
    byte[] plain=ProtectedData.Unprotect(File.ReadAllBytes(path),Encoding.UTF8.GetBytes(Entropy),DataProtectionScope.CurrentUser);
    var value=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(plain));
    return value!=null&&value.ContainsKey("email")&&value.ContainsKey("password")&&value.ContainsKey("url")?value:null;
   } catch { return null; }
  }
  public static void Delete(string path){try{if(File.Exists(path))File.Delete(path);}catch{}}
 }
}
