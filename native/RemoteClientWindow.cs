using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
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
    request.Method="POST";request.ContentType="application/json";request.Timeout=15000;request.ReadWriteTimeout=15000;
    request.Headers["Authorization"]="Bearer "+session.Token;
    byte[] bytes=Encoding.UTF8.GetBytes("{}");request.ContentLength=bytes.Length;
    using(var stream=request.GetRequestStream())stream.Write(bytes,0,bytes.Length);
    using(var response=(HttpWebResponse)request.GetResponse())using(var reader=new StreamReader(response.GetResponseStream()))return json.Deserialize<Dictionary<string,object>>(reader.ReadToEnd());
   } catch(WebException ex) {
    var r=ex.Response as HttpWebResponse;
    if(r!=null&&(int)r.StatusCode==401)throw new InvalidOperationException("호스트 PC가 이 로그인을 거부했습니다. 호스트에서 계정을 다시 연결했는지 확인해 주세요.");
    throw new InvalidOperationException("호스트 PC에 연결하지 못했습니다. 호스트의 Orbit이 켜져 있고 외부 주소가 준비됐는지 확인해 주세요."+(r!=null?" (HTTP "+(int)r.StatusCode+")":""));
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
    request.Method="POST";request.ContentType="application/json";request.Timeout=10000;request.ReadWriteTimeout=10000;
    byte[] bytes=Encoding.UTF8.GetBytes(json.Serialize(new Dictionary<string,object>{{"email",email.Trim()},{"password",password}}));
    request.ContentLength=bytes.Length;
    using(var stream=request.GetRequestStream())stream.Write(bytes,0,bytes.Length);
    using(var response=(HttpWebResponse)request.GetResponse())using(var reader=new StreamReader(response.GetResponseStream()))body=json.Deserialize<Dictionary<string,object>>(reader.ReadToEnd());
   } catch(WebException ex) {
    var errorResponse=ex.Response as HttpWebResponse;
    if(errorResponse==null)throw new InvalidOperationException("계정 서비스에 연결하지 못했습니다: "+ex.Message);
    string code="";
    try{using(errorResponse)using(var reader=new StreamReader(errorResponse.GetResponseStream())){object v;var e=json.Deserialize<Dictionary<string,object>>(reader.ReadToEnd());if(e!=null&&e.TryGetValue("error",out v))code=Convert.ToString(v);}}catch{}
    throw new InvalidOperationException(LoginError(code));
   }
   object hostValue=null,tokenValue=null;
   if(body!=null){body.TryGetValue("url",out hostValue);body.TryGetValue("deviceToken",out tokenValue);}
   string host=Convert.ToString(hostValue),token=Convert.ToString(tokenValue);Uri hostUri;
   if(String.IsNullOrEmpty(token)||!Uri.TryCreate(host,UriKind.Absolute,out hostUri)||hostUri.Scheme!=Uri.UriSchemeHttps)throw new InvalidOperationException("계정 서비스 응답이 올바르지 않습니다.");
   return new Session{Base=hostUri.GetLeftPart(UriPartial.Authority)+hostUri.AbsolutePath.TrimEnd('/'),Token=token};
  }
  static string LoginError(string code) {
   switch(code) {
    case "invalid_email":return "이메일 형식이 올바르지 않습니다.";
    case "invalid_credentials":return "이메일 또는 비밀번호가 올바르지 않습니다.";
    case "no_pc_linked":return "이 계정에 연결된 PC가 없습니다. 접속받을 PC의 Orbit에서 먼저 계정을 연결하세요.";
    case "rate_limited":return "잠시 후 다시 시도해 주세요.";
    default:return "로그인하지 못했습니다.";
   }
  }
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
}
