using System;using System.Diagnostics;using System.Text.RegularExpressions;using System.Threading;
namespace Orbit {
 internal sealed class RemoteTunnel:IDisposable {
  readonly object gate=new object();Process process;Timer timeout;string url,error;bool starting;int generation;
  public event Action<string,string> Changed;public string Url{get{lock(gate)return url;}}
  public object Status(){lock(gate)return new {starting=starting,url=url,error=error};}
  public void Start(int port){lock(gate){if(process!=null||starting)return;starting=true;error=null;int g=++generation;string exe=System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"tools","cloudflared.exe");if(!System.IO.File.Exists(exe)){starting=false;error="cloudflared.exe를 찾을 수 없습니다.";Raise();return;}var p=new Process{StartInfo=new ProcessStartInfo(exe,"tunnel --no-autoupdate --url http://127.0.0.1:"+port+" --http-host-header 127.0.0.1:"+port+" --metrics 127.0.0.1:0"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}};process=p;p.EnableRaisingEvents=true;p.OutputDataReceived+=(s,e)=>Output(p,g,e);p.ErrorDataReceived+=(s,e)=>Output(p,g,e);p.Exited+=(s,e)=>StopInternal(p,g,"tunnel exited");try{p.Start();p.BeginOutputReadLine();p.BeginErrorReadLine();timeout=new Timer(_=>StopInternal(p,g,"temporary connection timed out"),null,45000,Timeout.Infinite);}catch(Exception ex){StopInternal(p,g,ex.Message);}}}
  void Output(Process p,int g,DataReceivedEventArgs e){if(String.IsNullOrEmpty(e.Data))return;var m=Regex.Match(e.Data,@"https://[a-z0-9-]+\.trycloudflare\.com",RegexOptions.IgnoreCase);if(!m.Success)return;lock(gate){if(process!=p||generation!=g)return;url=m.Value;starting=false;if(timeout!=null){timeout.Dispose();timeout=null;}}Raise();}
  public void Stop(){Process p;int g;lock(gate){p=process;g=generation;}StopInternal(p,g,null);}
  void StopInternal(Process expected,int g,string why){Process p;lock(gate){if(g!=generation||process!=expected)return;p=process;process=null;starting=false;url=null;error=why;if(timeout!=null){timeout.Dispose();timeout=null;}}try{if(p!=null&&!p.HasExited)p.Kill();}catch{}try{if(p!=null)p.Dispose();}catch{}Raise();}
  void Raise(){var h=Changed;if(h!=null)h(Url,error);}public void Dispose(){Stop();}
 }
}
