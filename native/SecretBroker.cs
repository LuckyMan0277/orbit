using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
namespace Orbit {
 // A program asked the user for a secret and is waiting; the answer never carries the value.
 internal sealed class SecretRequest { public string Id,Session,Project,Name,Reason,Status="pending"; public readonly ManualResetEvent Done=new ManualResetEvent(false); }
 // Lets a program running inside an Orbit terminal (an AI CLI or the user's own script) use the vault without ever being given a
 // value in a conversation: it can ask the user to enter a key, list names, or fetch values to hand straight to a child process
 // (orbit-secret run). Each terminal gets its own token in its environment; the pipe is limited to the current Windows user.
 internal sealed class SecretBroker {
  const int MaxLine=16384,MaxPendingPerTerminal=3;
  readonly SecretVault vault; readonly Action<SecretRequest> announce,closed; readonly TimeSpan timeout; readonly JavaScriptSerializer json=new JavaScriptSerializer();
  readonly ConcurrentDictionary<string,string> tokens=new ConcurrentDictionary<string,string>(),sessionTokens=new ConcurrentDictionary<string,string>(),sessionProjects=new ConcurrentDictionary<string,string>();
  readonly ConcurrentDictionary<string,SecretRequest> requests=new ConcurrentDictionary<string,SecretRequest>();
  readonly object gate=new object(); string pipeName; bool stopped;
  public SecretBroker(SecretVault secrets,Action<SecretRequest> announceRequest,TimeSpan? requestTimeout=null,Action<SecretRequest> requestClosed=null){vault=secrets;announce=announceRequest;closed=requestClosed;timeout=requestTimeout??TimeSpan.FromMinutes(10);}
  public string PipeName{get{lock(gate)return pipeName;}}
  // Called when a terminal starts: returns the token to put in its environment. The terminal keeps the project it was opened in for its whole
  // life, whatever project the window shows later. The pipe server starts with the first terminal.
  public string Register(string session,string project){
   Start();
   byte[] bytes=new byte[24];using(var random=RandomNumberGenerator.Create())random.GetBytes(bytes);
   string token=Convert.ToBase64String(bytes).Replace('+','-').Replace('/','_').TrimEnd('=');
   tokens[token]=session;sessionTokens[session]=token;sessionProjects[session]=project??"";return token;
  }
  public void Release(string session){
   string token,ignored;if(sessionTokens.TryRemove(session,out token))tokens.TryRemove(token,out ignored);sessionProjects.TryRemove(session,out ignored);
   foreach(var request in requests.Values.Where(r=>r.Session==session))Finish(request,"closed");
  }
  public void Stop(){lock(gate)stopped=true;}
  public SecretRequest[] PendingList(string session){return requests.Values.Where(r=>r.Session==session&&r.Status=="pending").ToArray();}
  public object Pending(string session){return PendingList(session).Select(r=>new {id=r.Id,name=r.Name,reason=r.Reason,project=r.Project}).ToArray();}
  // The UI reports what the user did after it stored (or refused to store) the value. "saved" only counts if the vault has it.
  public bool Answer(string id,string status){
   SecretRequest request;if(String.IsNullOrEmpty(id)||!requests.TryGetValue(id,out request))return false;
   return Finish(request,status=="saved"&&vault.Has(request.Name,request.Project)?"saved":"denied");
  }
  bool Finish(SecretRequest request,string status){
   lock(request){if(request.Status!="pending")return false;request.Status=status;}
   request.Done.Set();
   if(closed!=null)try{closed(request);}catch{}
   return true;
  }
  void Start(){
   lock(gate){if(pipeName!=null)return;pipeName="orbit-"+Guid.NewGuid().ToString("N");}
   new Thread(delegate(){
    while(true){
     lock(gate)if(stopped)return;
     try{var pipe=Create();pipe.WaitForConnection();ThreadPool.QueueUserWorkItem(_=>Handle(pipe));}catch{Thread.Sleep(200);}
    }
   }){IsBackground=true,Name="Orbit secrets"}.Start();
  }
  NamedPipeServerStream Create(){
   var security=new PipeSecurity();
   // The per-terminal token, not this ACL, is the gate: it is only in that terminal's environment, and a sandboxed Codex command runs under a token that a current-user-only ACL would refuse.
   security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User,PipeAccessRights.FullControl,AccessControlType.Allow));
   security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid,null),PipeAccessRights.ReadWrite,AccessControlType.Allow));
   return new NamedPipeServerStream(pipeName,PipeDirection.InOut,NamedPipeServerStream.MaxAllowedServerInstances,PipeTransmissionMode.Byte,PipeOptions.None,4096,4096,security);
  }
  void Handle(NamedPipeServerStream pipe){
   try{
    using(pipe){
     var utf8=new UTF8Encoding(false);var reader=new StreamReader(pipe,utf8);var writer=new StreamWriter(pipe,utf8){AutoFlush=true};
     Action<object> reply=value=>writer.WriteLine(json.Serialize(value));
     try{
      string line=ReadLine(reader);if(line==null){reply(new {error="request too large"});return;}
      var a=json.Deserialize<Dictionary<string,object>>(line)??new Dictionary<string,object>();
      string session;if(!tokens.TryGetValue(S(a,"token"),out session)){reply(new {error="This terminal is not registered with Orbit."});return;}
      string project;sessionProjects.TryGetValue(session,out project);
      switch(S(a,"op")){
       case "list":reply(new {names=vault.Names(project),keys=vault.List(project)});break;
       case "env":reply(new {env=vault.Env(project,Names(a,"names"))});break;
       case "request":Request(session,project,a,reply);break;
       default:reply(new {error="unknown operation"});break;
      }
     }catch(InvalidOperationException ex){reply(new {error=ex.Message});}
    }
   }catch{}
  }
  void Request(string session,string project,Dictionary<string,object> a,Action<object> reply){
   string name=SecretVault.CheckName(S(a,"name")),reason=S(a,"reason").Trim();if(reason.Length>200)reason=reason.Substring(0,200);
   if(vault.Has(name,project)){reply(new {status="exists"});return;}
   if(requests.Values.Count(r=>r.Session==session&&r.Status=="pending")>=MaxPendingPerTerminal)throw new InvalidOperationException("Too many requests are already waiting for the user.");
   var request=new SecretRequest{Id=Guid.NewGuid().ToString("N"),Session=session,Project=project,Name=name,Reason=reason};
   requests[request.Id]=request;
   try{
    announce(request);
    // The client prints its "waiting" note only after this acknowledgement, so the note (terminal output) reaches a phone after the request exists.
    reply(new {status="pending"});
    if(!request.Done.WaitOne(timeout))Finish(request,"timeout");
    reply(new {status=request.Status});
   }finally{SecretRequest ignored;requests.TryRemove(request.Id,out ignored);}
  }
  static string ReadLine(StreamReader reader){var text=new StringBuilder();int c;while((c=reader.Read())>=0&&c!='\n'){if(text.Length>=MaxLine)return null;text.Append((char)c);}return text.ToString();}
  static string S(Dictionary<string,object> a,string key){object v;return a.TryGetValue(key,out v)&&v!=null?Convert.ToString(v):"";}
  static string[] Names(Dictionary<string,object> a,string key){object v;var list=a.TryGetValue(key,out v)?v as System.Collections.IEnumerable:null;return list==null||v is string?null:list.Cast<object>().Select(x=>Convert.ToString(x)).Where(x=>!String.IsNullOrEmpty(x)).ToArray();}
 }
}
