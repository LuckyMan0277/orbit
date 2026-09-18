using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace Orbit {
 // Replaces QR pairing: any device that logs into the linked cloud account gets this
 // PC's current public URL and a valid bearer token back, with no local approval step.
 // The device token is also kept here in plaintext (not just hashed, unlike
 // remote-devices.json) because the cloud service must hand the same token back on
 // every login -- that tradeoff is inherent to "log in anywhere, no PC confirmation".
 internal sealed class RemoteAccount:IDisposable {
  readonly object gate=new object(); readonly RemoteServer remote; readonly string stateFile; readonly JavaScriptSerializer json=new JavaScriptSerializer();
  string email,deviceToken,pushSecret,lastError; int generation;
  public event Action Changed;
  public string ServiceUrl{get;set;}
  public RemoteAccount(RemoteServer server,string storage){remote=server;stateFile=storage;Load();}
  public object Status(){lock(gate)return new {linked=!String.IsNullOrEmpty(email),email=email,serviceUrl=ServiceUrl,error=lastError};}
  public object SignUp(string emailValue,string password){return Attach("/signup",emailValue,password);}
  public object Link(string emailValue,string password){return Attach("/account/link",emailValue,password);}
  object Attach(string path,string emailValue,string password) {
   emailValue=(emailValue??"").Trim().ToLowerInvariant();
   if(emailValue.Length==0||String.IsNullOrEmpty(password))return Failure("이메일과 비밀번호를 입력해 주세요.");
   string baseUrl=ServiceUrl;
   if(String.IsNullOrEmpty(baseUrl))return Failure("계정 서비스 주소가 설정되지 않았습니다.");
   string token=remote.IssueAccountToken();
   if(token==null)return Failure("기기 등록 정보를 저장하지 못했습니다.");
   var body=new Dictionary<string,object>{{"email",emailValue},{"password",password},{"url",remote.Url()},{"deviceToken",token}};
   string error;var response=Post(baseUrl+path,body,out error);
   if(response==null){remote.RevokeToken(token);return Failure(error??"계정 서비스에 연결하지 못했습니다.");}
   object errorValue;
   if(response.TryGetValue("error",out errorValue)){remote.RevokeToken(token);return Failure(ErrorMessage(Convert.ToString(errorValue)));}
   object secretValue;string secret=response.TryGetValue("pushSecret",out secretValue)?Convert.ToString(secretValue):null;
   if(String.IsNullOrEmpty(secret)){remote.RevokeToken(token);return Failure("계정 서비스 응답이 올바르지 않습니다.");}
   string previousToken;lock(gate)previousToken=deviceToken;
   lock(gate){email=emailValue;deviceToken=token;pushSecret=secret;lastError=null;}
   Save();
   if(!String.IsNullOrEmpty(previousToken)&&previousToken!=token)remote.RevokeToken(previousToken);
   Raise();
   return new {ok=true,email=emailValue};
  }
  public object Unlink() {
   string secret,tokenValue,baseUrl=ServiceUrl,emailValue;
   lock(gate){secret=pushSecret;tokenValue=deviceToken;emailValue=email;}
   if(String.IsNullOrEmpty(emailValue)){lock(gate)lastError=null;return new {ok=true};}
   if(!String.IsNullOrEmpty(baseUrl)&&!String.IsNullOrEmpty(secret)){string error;Post(baseUrl+"/account/unlink",new Dictionary<string,object>{{"email",emailValue},{"pushSecret",secret}},out error);}
   if(!String.IsNullOrEmpty(tokenValue))remote.RevokeToken(tokenValue);
   lock(gate){email=null;deviceToken=null;pushSecret=null;lastError=null;}
   Save();Raise();
   return new {ok=true};
  }
  // Fired whenever the Funnel/Tunnel public origin changes. Runs on a background thread
  // and never throws back to the caller, since it commonly runs from an event handler.
  public void PushUrl(string url) {
   string secret,tokenValue,baseUrl=ServiceUrl,emailValue;int attempt;
   lock(gate){secret=pushSecret;tokenValue=deviceToken;emailValue=email;attempt=++generation;}
   if(String.IsNullOrEmpty(emailValue)||String.IsNullOrEmpty(secret)||String.IsNullOrEmpty(baseUrl)||String.IsNullOrEmpty(url))return;
   ThreadPool.QueueUserWorkItem(delegate {
    string error;var body=new Dictionary<string,object>{{"email",emailValue},{"pushSecret",secret},{"url",url},{"deviceToken",tokenValue}};
    var response=Post(baseUrl+"/account/push",body,out error);
    lock(gate){
     if(generation!=attempt)return;
     if(response==null){lastError=error;}
     else{object errorValue;lastError=response.TryGetValue("error",out errorValue)?ErrorMessage(Convert.ToString(errorValue)):null;}
    }
    Raise();
   });
  }
  static string ErrorMessage(string code) {
   if(code=="invalid_email")return "이메일 형식이 올바르지 않습니다.";
   if(code=="invalid_password")return "비밀번호가 올바르지 않습니다(8자 이상).";
   if(code=="account_exists")return "이미 존재하는 계정입니다.";
   if(code=="invalid_credentials")return "이메일 또는 비밀번호가 올바르지 않습니다.";
   if(code=="no_pc_linked")return "이 계정에 연결된 PC가 없습니다.";
   if(code=="rate_limited")return "잠시 후 다시 시도해 주세요.";
   return String.IsNullOrEmpty(code)?"요청이 거부되었습니다.":code;
  }
  object Failure(string message){lock(gate)lastError=message;Raise();return new {ok=false,error=message};}
  Dictionary<string,object> Post(string url,Dictionary<string,object> body,out string error) {
   error=null;
   try {
    var request=(HttpWebRequest)WebRequest.Create(url);
    request.Method="POST";request.ContentType="application/json";request.Timeout=8000;request.ReadWriteTimeout=8000;
    byte[]bytes=Encoding.UTF8.GetBytes(json.Serialize(body));
    request.ContentLength=bytes.Length;
    using(var stream=request.GetRequestStream())stream.Write(bytes,0,bytes.Length);
    using(var response=(HttpWebResponse)request.GetResponse())using(var reader=new StreamReader(response.GetResponseStream()))
     return json.Deserialize<Dictionary<string,object>>(reader.ReadToEnd());
   } catch(WebException ex) {
    var errorResponse=ex.Response as HttpWebResponse;
    if(errorResponse!=null) {
     try{using(errorResponse)using(var reader=new StreamReader(errorResponse.GetResponseStream()))return json.Deserialize<Dictionary<string,object>>(reader.ReadToEnd());}catch{}
    }
    error="계정 서비스에 연결하지 못했습니다: "+ex.Message;return null;
   } catch(Exception ex) {
    error="계정 서비스 요청이 실패했습니다: "+ex.Message;return null;
   }
  }
  void Load() {
   try {
    var state=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(stateFile));
    if(state==null)return;
    object v;
    email=state.TryGetValue("email",out v)?Convert.ToString(v):null;
    deviceToken=state.TryGetValue("deviceToken",out v)?Convert.ToString(v):null;
    pushSecret=state.TryGetValue("pushSecret",out v)?Convert.ToString(v):null;
    ServiceUrl=state.TryGetValue("serviceUrl",out v)?Convert.ToString(v):ServiceUrl;
   } catch { }
  }
  void Save() {
   try {
    string folder=Path.GetDirectoryName(stateFile);if(!String.IsNullOrEmpty(folder))Directory.CreateDirectory(folder);
    string tmp=stateFile+".tmp";
    Dictionary<string,object> state;lock(gate)state=new Dictionary<string,object>{{"email",email},{"deviceToken",deviceToken},{"pushSecret",pushSecret},{"serviceUrl",ServiceUrl}};
    File.WriteAllText(tmp,json.Serialize(state));
    if(File.Exists(stateFile))File.Replace(tmp,stateFile,null);else File.Move(tmp,stateFile);
   } catch { }
  }
  void Raise(){var h=Changed;if(h!=null)h();}
  public void Dispose(){}
 }
}
