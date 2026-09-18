using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace Orbit {
    // Loopback protocol test. It deliberately uses the HTTP boundary so auth,
    // origin and one-time pairing cannot accidentally pass through internals.
    internal static class RemoteUiTest {
        sealed class Terminals : IRemoteTerminals {
            public int Created,Inputs;
            readonly ManualResetEvent inputStarted=new ManualResetEvent(false),inputContinue=new ManualResetEvent(false); bool blockNext;
            public void BlockNextInput(){lock(this){blockNext=true;inputStarted.Reset();inputContinue.Reset();}}
            public bool WaitForInputStart(){return inputStarted.WaitOne(2000);}
            public void ReleaseInput(){inputContinue.Set();}
            public object Sessions(){return new {sessions=new[]{new {id="cmd",name="CMD",pid=1}}};}
            public object SavedSessions(){return new {sessions=new[]{new {provider="codex",id="11111111-1111-4111-8111-111111111111",title="Saved Codex",cwd="C:\\work"}}};}
            public object DeleteSavedSession(string provider,string id){return new {sessions=new object[0]};}
            public object Create(string profile,string resumeId,string project=null){Created++;if(!String.IsNullOrEmpty(resumeId)&&resumeId!="11111111-1111-4111-8111-111111111111")throw new InvalidOperationException("bad resume");return new {session="new-"+profile};}
            public object Projects(){return new {projects=new[]{new {path="C:\\work",name="work"}},current="C:\\work"};}
            public object Snapshot(string id){if(id!="cmd")throw new InvalidOperationException("terminal not found");return new {seq=7,data="\u001b[32mready\u001b[0m",cols=100,rows=30,items=new object[0]};}
            public object Output(string id,long after,int timeout){if(id!="cmd")throw new InvalidOperationException("terminal not found");return new {reset=false,seq=8,items=new[]{new {seq=8,data="80235"}}};}
            public void Input(string id,string data){if(id!="cmd"||(data!="set /a 12345+67890\r"&&data!=new string('\uD55C',7000)&&data!="idempotent\r"))throw new InvalidOperationException("bad terminal input");bool block;lock(this){Inputs++;block=blockNext;blockNext=false;}if(block){inputStarted.Set();if(!inputContinue.WaitOne(3000))throw new TimeoutException("test input was not released");}}
        }
        static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
        internal static void Run() {
            int port=49170+(Environment.TickCount&255);string store=TestArtifacts.PathFor("remote-devices-test.json");try{if(File.Exists(store))File.Delete(store);}catch{}
            var terminals=new Terminals();
            using(var server=new RemoteServer(terminals,store)) {
                server.Start(port); string baseUrl="http://127.0.0.1:"+port+"/api/v1/";
                Expect(Post(baseUrl,"sessions",new { }).Status==401,"unauthorized sessions");
                Expect(Post(baseUrl,"terminals/create",new {profile="cmd"}).Status==401&&terminals.Created==0,"unauthorized create");
                Expect(Post(baseUrl,"pair/request",new { },null,"https://evil.example").Status==403,"origin rejection");
                string[] invite=server.CreateInvite("test").Split('.');
                var request=Post(baseUrl,"pair/request",new {id=invite[0],code=invite[1],device="test phone"});Expect(request.Status==202,"pair request");
                var claim=(string)request.Value["claim"];Expect(Post(baseUrl,"pair/poll",new {id=invite[0],code=invite[1]}).Status==403,"claim required");
                Expect(server.Approve(invite[0]),"approve pending");
                var approved=Post(baseUrl,"pair/poll",new {id=invite[0],code=invite[1],claim=claim});Expect(approved.Status==200,"approved poll");string token=(string)approved.Value["token"];
                Expect(Post(baseUrl,"pair/complete",new {id=invite[0]},token).Status==200,"authenticated pair completion");
                Expect(Json.Serialize(server.Status()).Contains(invite[0]),"completed invite is correlated in desktop status");
                Expect(Post(baseUrl,"pair/poll",new {id=invite[0],code=invite[1],claim=claim}).Status==403,"replay rejected");
                Expect(Post(baseUrl,"sessions",new { },token).Status==200,"authorized sessions");
                server.Stop();using(var restarted=new RemoteServer(terminals,store)){restarted.Start(port);Expect(Post(baseUrl,"sessions",new { },token).Status==200,"trusted token survives new server");string[] revisit=restarted.CreateInvite("trusted revisit").Split('.');Expect(Post(baseUrl,"pair/complete",new {id=revisit[0],code="00000000"},token).Status==403,"trusted revisit wrong secret rejected");Expect(Post(baseUrl,"pair/complete",new {id=revisit[0],code=revisit[1]},token).Status==200,"trusted revisit consumes QR without approval");}server.Start(port);
                Expect(Post(baseUrl,"saved-sessions",new { },token).Status==200,"authorized saved sessions");
                Expect(Post(baseUrl,"terminals/create",new {profile="codex",resumeId="x;calc"},token).Status==400,"resume id validation");
                Expect(Post(baseUrl,"terminals/create",new {profile="codex",resumeId="11111111-1111-4111-8111-111111111111"},token).Status==200,"authorized saved resume");
                Expect(Post(baseUrl,"terminals/create",new {profile="cmd"},token,"https://evil.example").Status==403&&terminals.Created==1,"create origin rejection");
                Expect(Post(baseUrl,"terminals/create",new {profile="cmd /c whoami"},token).Status==400&&terminals.Created==1,"create profile whitelist");
                Expect(Post(baseUrl,"terminals/create",new {},token).Status==400&&terminals.Created==1,"create profile required");
                foreach(string profile in new[]{"cmd","powershell","codex","claude"}) {
                    var created=Post(baseUrl,"terminals/create",new {profile=profile},token);
                    Expect(created.Status==200&&(string)created.Value["session"]=="new-"+profile,"authorized create "+profile);
                }
                Expect(terminals.Created==5,"create exactly once");
                var snapshot=Post(baseUrl,"snapshot",new {session="cmd"},token);Expect(snapshot.Status==200&&(int)snapshot.Value["cols"]==100,"snapshot dimensions");
                Expect(Post(baseUrl,"input",new {session="cmd",data="set /a 12345+67890\r"},token).Status==200,"terminal input");
                Expect(Post(baseUrl,"input",new {session="cmd",data=new string('\uD55C',7000)},token).Status==200,"long Korean input");
                Expect(Post(baseUrl,"input",new {session="cmd",data=new string('x',8193)},token).Status==400,"input size bound");
                string epoch=(string)Post(baseUrl,"sessions",new { },token).Value["serverEpoch"],requestId=UnixMs()+"-idempotent";int before=terminals.Inputs;Response first=null,second=null;terminals.BlockNextInput();var one=new Thread(()=>first=Post(baseUrl,"input",new {session="cmd",data="idempotent\r",requestId=requestId,serverEpoch=epoch},token));one.Start();Expect(terminals.WaitForInputStart(),"first idempotent input reaches terminal");var two=new Thread(()=>second=Post(baseUrl,"input",new {session="cmd",data="idempotent\r",requestId=requestId,serverEpoch=epoch},token));two.Start();two.Join();terminals.ReleaseInput();one.Join();
                Expect(first.Status==200&&second.Status==409&&terminals.Inputs==before+1,"in-flight duplicate executes once and reports indeterminate");
                Expect(Post(baseUrl,"input",new {session="cmd",data="set /a 12345+67890\r",requestId=requestId,serverEpoch=epoch},token).Status==409,"request payload mismatch");
                Expect(Post(baseUrl,"input/status",new {session="cmd",data="idempotent\r",requestId=requestId,serverEpoch=epoch},token).Status==200,"input receipt confirmation");
                Expect(Post(baseUrl,"input",new {session="cmd",data="idempotent\r",requestId=(UnixMs()-700000)+"-stale-time",serverEpoch=epoch},token).Status==409,"stale timestamp request rejected");
                Expect(Post(baseUrl,"input",new {session="other",data="idempotent\r",requestId=requestId,serverEpoch=epoch},token).Status==409,"request id session payload mismatch");
                string[] secondInvite=server.CreateInvite("second device").Split('.');var secondRequest=Post(baseUrl,"pair/request",new {id=secondInvite[0],code=secondInvite[1],device="second device"});Expect(server.Approve(secondInvite[0]),"approve second device");var secondPoll=Post(baseUrl,"pair/poll",new {id=secondInvite[0],code=secondInvite[1],claim=(string)secondRequest.Value["claim"]});string secondToken=(string)secondPoll.Value["token"];Expect(Post(baseUrl,"pair/complete",new {id=secondInvite[0]},secondToken).Status==200,"complete second device");int secondBefore=terminals.Inputs;Expect(Post(baseUrl,"input",new {session="cmd",data="idempotent\r",requestId=requestId,serverEpoch=epoch},secondToken).Status==200&&terminals.Inputs==secondBefore+1,"same request id is isolated by paired device");
                Expect(Post(baseUrl,"input",new {session="cmd",data="idempotent\r",requestId=UnixMs()+"-stale",serverEpoch="stale"},token).Status==409,"server epoch mismatch");
                server.Revoke(Convert.ToBase64String(System.Security.Cryptography.SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(token))));
                Expect(Post(baseUrl,"sessions",new { },token).Status==401,"revoked token");
                Expect(Post(baseUrl,"terminals/create",new {profile="cmd"},token).Status==401&&terminals.Created==5,"revoked create");
                string[] rejected=server.CreateInvite("reject").Split('.');var pending=Post(baseUrl,"pair/request",new {id=rejected[0],code=rejected[1],device="reject"});server.Reject(rejected[0]);Expect(Post(baseUrl,"pair/poll",new {id=rejected[0],code=rejected[1],claim=(string)pending.Value["claim"]}).Status==403,"rejected pairing");
                server.Stop();server.Start(port);Expect(Post(baseUrl,"sessions",new { }).Status==401,"restart");
            }
        }
        sealed class Response { public int Status;public Dictionary<string,object> Value; }
        static Response Post(string root,string operation,object body,string token=null,string origin=null) {
            var request=(HttpWebRequest)WebRequest.Create(root+operation);request.Method="POST";request.ContentType="application/json";if(token!=null)request.Headers[HttpRequestHeader.Authorization]="Bearer "+token;if(origin!=null)request.Headers["Origin"]=origin;
            byte[] bytes=Encoding.UTF8.GetBytes(Json.Serialize(body));request.ContentLength=bytes.Length;using(var stream=request.GetRequestStream())stream.Write(bytes,0,bytes.Length);
            try {using(var response=(HttpWebResponse)request.GetResponse())return Read(response);}catch(WebException ex){return Read((HttpWebResponse)ex.Response);}
        }
        static Response Read(HttpWebResponse response){using(response){using(var reader=new StreamReader(response.GetResponseStream()))return new Response {Status=(int)response.StatusCode,Value=Json.Deserialize<Dictionary<string,object>>(reader.ReadToEnd())};}}
        static long UnixMs(){return (long)(DateTime.UtcNow-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalMilliseconds;}
        static void Expect(bool ok,string name){if(!ok)throw new InvalidOperationException("Remote test failed: "+name);}
    }
}
