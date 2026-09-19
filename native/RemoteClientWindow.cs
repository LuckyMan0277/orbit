using System;
using System.Drawing;
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
  static bool Allowed(string value){Uri u;return Uri.TryCreate(value,UriKind.Absolute,out u)&&u.Scheme==Uri.UriSchemeHttps;}
  public static void Open(string value,CoreWebView2Environment environment,Icon icon) {
   value=(value??"").Trim();
   if(value.Length>0&&value.IndexOf("://",StringComparison.Ordinal)<0)value="https://"+value;
   Uri uri;
   if(!Uri.TryCreate(value,UriKind.Absolute,out uri)||uri.Scheme!=Uri.UriSchemeHttps)throw new InvalidOperationException("https:// 로 시작하는 계정 서비스 주소를 입력해 주세요.");
   if(current!=null&&!current.IsDisposed){current.Activate();return;}
   current=new RemoteClientWindow(uri.AbsoluteUri,environment,icon);
   current.Show();
  }
 }
}
