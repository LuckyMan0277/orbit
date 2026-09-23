using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Orbit {
    // Remote desktop UI. A client PC signs in with its account, and its Orbit forwards the
    // file and terminal calls of the normal desktop UI to this PC (host side below) while
    // this PC streams the terminal output of the terminals that client opened back to it.
    internal interface IDeskBridge {
        object DeskCall(string client,string method,Dictionary<string,object> args);
        object DeskEvents(string client,long after,int timeoutMs);
    }

    internal sealed partial class MainWindow {
        // ---------------------------------------------------------------- host side
        sealed class DeskEvent { public long Seq; public int Size; public object Message; }
        sealed class DeskClient {
            public readonly object Gate=new object();
            public readonly List<DeskEvent> Events=new List<DeskEvent>();
            public readonly HashSet<string> Paused=new HashSet<string>();
            public long Seq,Pending;
            public DateTime LastSeen=DateTime.UtcNow;
        }
        const int DeskPendingLimit=512*1024;
        static readonly TimeSpan DeskIdle=TimeSpan.FromMinutes(2);
        // Deliberately the same surface the desktop UI needs for its workspace; account, update, window and
        // remote-connection settings of this PC are not reachable from a remote client.
        static readonly HashSet<string> DeskAllowed=new HashSet<string> {
            "list","move","read","save","stat","createFile","browse","pickerPlaces","createTerminal","write","ack","resize",
            "closeTerminal","terminalName","savedSessions","deleteSavedSession","metrics","hostSessions","attachTerminal","detachTerminal",
            "secretList","secretSet","secretDelete","secretAnswer" };
        readonly ConcurrentDictionary<string,DeskClient> deskClients=new ConcurrentDictionary<string,DeskClient>();
        readonly ConcurrentDictionary<string,string> sessionOwners=new ConcurrentDictionary<string,string>();
        // Remote clients watching a terminal that the host itself opened (session id -> client ids).
        readonly ConcurrentDictionary<string,ConcurrentDictionary<string,bool>> sessionViewers=new ConcurrentDictionary<string,ConcurrentDictionary<string,bool>>();
        const int DeskViewerLimit=4*1024*1024;
        bool IsViewer(string key,string client){ConcurrentDictionary<string,bool> v;return sessionViewers.TryGetValue(key,out v)&&v.ContainsKey(client);}
        System.Windows.Forms.Timer deskSweep;

        object IDeskBridge.DeskCall(string client,string method,Dictionary<string,object> args) {
            if(!DeskAllowed.Contains(method))throw new InvalidOperationException("원격에서 지원하지 않는 요청입니다.");
            if(quitting||IsDisposed||!IsHandleCreated)throw new InvalidOperationException("호스트 Orbit이 종료 중입니다.");
            var done=new TaskCompletionSource<object>();
            BeginInvoke(new Action(async delegate {
                try { Desk(client); done.SetResult(await Dispatch(method,args??new Dictionary<string,object>(),client)); }
                catch(Exception ex) { done.SetException(ex); }
            }));
            // WaitOne, not Task.Wait: Wait would wrap a failed call in an AggregateException and hide its message.
            if(!((IAsyncResult)done.Task).AsyncWaitHandle.WaitOne(60000))throw new TimeoutException("호스트가 응답하지 않습니다.");
            return done.Task.GetAwaiter().GetResult();
        }

        // Long poll. The client confirms what it has processed by passing that sequence back as "after".
        object IDeskBridge.DeskEvents(string client,long after,int timeoutMs) {
            var c=Desk(client);List<string> resume=null;var batch=new List<object>();long last=after;
            lock(c.Gate) {
                int drop=0;
                while(drop<c.Events.Count&&c.Events[drop].Seq<=after) { c.Pending-=c.Events[drop].Size;drop++; }
                if(drop>0)c.Events.RemoveRange(0,drop);
                if(c.Pending<=DeskPendingLimit&&c.Paused.Count>0) { resume=c.Paused.ToList();c.Paused.Clear(); }
                if(c.Events.Count==0&&timeoutMs>0&&resume==null)Monitor.Wait(c.Gate,timeoutMs);
                int bytes=0;
                foreach(var e in c.Events) { if(bytes>300000&&batch.Count>0)break;batch.Add(e.Message);bytes+=e.Size+64;last=e.Seq; }
            }
            if(resume!=null)foreach(string key in resume) { ConPty t;if(sessions.TryGetValue(key,out t))t.Acknowledge(); }
            c.LastSeen=DateTime.UtcNow;
            return new { events=batch.ToArray(),seq=last };
        }

        DeskClient Desk(string id) {
            var c=deskClients.GetOrAdd(id,_=>new DeskClient());c.LastSeen=DateTime.UtcNow;
            if(deskSweep==null&&IsHandleCreated&&!InvokeRequired) {
                deskSweep=new System.Windows.Forms.Timer { Interval=30000 };
                deskSweep.Tick+=async delegate { await SweepDesk(); };
                deskSweep.Start();
            } else if(deskSweep==null&&IsHandleCreated) BeginInvoke(new Action(()=>Desk(id)));
            return c;
        }

        // A client that stopped polling (closed its window, lost the network) must not leave shells running.
        private async Task SweepDesk() {
            foreach(var pair in deskClients.ToArray()) {
                if(DateTime.UtcNow-pair.Value.LastSeen<DeskIdle)continue;
                foreach(var owned in sessionOwners.Where(x=>x.Value==pair.Key).Select(x=>x.Key).ToArray()) {
                    try { await Dispatch("closeTerminal",new Dictionary<string,object>{{"session",owned}},pair.Key); } catch { }
                    string ignored;sessionOwners.TryRemove(owned,out ignored);
                }
                foreach(var viewers in sessionViewers.Values){bool gone;viewers.TryRemove(pair.Key,out gone);}
                DeskClient removed;deskClients.TryRemove(pair.Key,out removed);
            }
            if(deskClients.IsEmpty&&deskSweep!=null) { deskSweep.Stop();deskSweep.Dispose();deskSweep=null; }
        }

        private void EmitTo(string owner,object message,int size=0) {
            if(owner==null) { Send(message);return; }
            DeskClient c;if(!deskClients.TryGetValue(owner,out c))return;
            lock(c.Gate) { c.Events.Add(new DeskEvent { Seq=++c.Seq,Size=size,Message=message });c.Pending+=size;Monitor.PulseAll(c.Gate); }
        }

        // The local UI acknowledges output after xterm has rendered it; a remote client sits behind a slow link, so the
        // host acknowledges on its behalf until too much is waiting to be fetched, then resumes when the client catches up.
        private void EmitOutput(string key,object message,int size) {
            string owner;
            if(!sessionOwners.TryGetValue(key,out owner)) {
                Send(message);
                ConcurrentDictionary<string,bool> viewers;
                if(sessionViewers.TryGetValue(key,out viewers))foreach(string id in viewers.Keys)EmitToViewer(id,key,message,size);
                return;
            }
            EmitTo(owner,message,size);
            DeskClient c;bool pause=false;
            if(deskClients.TryGetValue(owner,out c))lock(c.Gate) { pause=c.Pending>DeskPendingLimit;if(pause)c.Paused.Add(key); }
            if(!pause) { ConPty t;if(sessions.TryGetValue(key,out t))t.Acknowledge(); }
        }

        // Viewers do not take part in the host's acknowledgements (the host UI still acks). A viewer that falls too far
        // behind is dropped and told to re-attach, which starts from a fresh snapshot instead of an unbounded backlog.
        private void EmitToViewer(string clientId,string key,object message,int size) {
            DeskClient c;if(!deskClients.TryGetValue(clientId,out c))return;
            bool behind;lock(c.Gate)behind=c.Pending>DeskViewerLimit;
            if(behind) {
                ConcurrentDictionary<string,bool> viewers;bool gone;
                if(sessionViewers.TryGetValue(key,out viewers))viewers.TryRemove(clientId,out gone);
                EmitTo(clientId,new { type="viewerDropped",session=key });return;
            }
            EmitTo(clientId,message,size);
        }
        private void EmitExit(string key,string owner,object message) {
            EmitTo(owner,message);
            ConcurrentDictionary<string,bool> viewers;
            if(owner==null&&sessionViewers.TryRemove(key,out viewers))foreach(string id in viewers.Keys)EmitTo(id,message);
        }

        // ---------------------------------------------------------------- client side
        static readonly HashSet<string> DeskForward=new HashSet<string> {
            "list","move","read","save","stat","createFile","browse","pickerPlaces","createTerminal","write","resize",
            "closeTerminal","terminalName","savedSessions","deleteSavedSession","metrics","hostSessions","attachTerminal","detachTerminal",
            "secretList","secretSet","secretDelete","secretAnswer" };
        // Terminal input must reach the host in the order it was typed; file calls may overlap.
        static readonly HashSet<string> DeskOrdered=new HashSet<string> { "createTerminal","write","resize","closeTerminal","terminalName" };
        readonly object deskLaneLock=new object();
        readonly ConcurrentDictionary<string,ConcurrentQueue<string>> deskWrites=new ConcurrentDictionary<string,ConcurrentQueue<string>>();
        Task deskLane=Task.FromResult(0);
        CancellationTokenSource deskLoop;

        private Task<object> DeskForwardCall(string method,Dictionary<string,object> a) {
            var session=clientSession;
            if(method=="write") {
                // Keystrokes queue up while a request is in flight; send whatever is waiting as one request so typing
                // does not fall a full round trip behind per key. Order is kept by the queue and the ordered lane.
                string sid=S(a,"session");var queue=deskWrites.GetOrAdd(sid,_=>new ConcurrentQueue<string>());queue.Enqueue(S(a,"data"));
                lock(deskLaneLock) {
                    var next=deskLane.ContinueWith<object>(_=>{
                        var text=new System.Text.StringBuilder();string piece;while(queue.TryDequeue(out piece))text.Append(piece);
                        if(text.Length==0)return null;
                        return RemoteClientWindow.DeskCall(session,"write",new Dictionary<string,object>{{"session",sid},{"data",text.ToString()}});
                    },TaskContinuationOptions.None);
                    deskLane=next;return next;
                }
            }
            if(DeskOrdered.Contains(method)) {
                lock(deskLaneLock) {
                    var next=deskLane.ContinueWith<object>(_=>RemoteClientWindow.DeskCall(session,method,a),TaskContinuationOptions.None);
                    deskLane=next;return next;
                }
            }
            return Task.Run(()=>RemoteClientWindow.DeskCall(session,method,a));
        }

        private void StartDeskEvents() {
            StopDeskEvents();
            var source=deskLoop=new CancellationTokenSource();var session=clientSession;
            Task.Run(()=>DeskEventLoop(session,source.Token));
        }
        private void StopDeskEvents() { var previous=deskLoop;deskLoop=null;if(previous!=null)previous.Cancel(); }

        private void DeskEventLoop(RemoteClientWindow.Session session,CancellationToken token) {
            long after=0;int failures=0;
            while(!token.IsCancellationRequested) {
                try {
                    var reply=RemoteClientWindow.DeskPost(session,"desk/events",new Dictionary<string,object>{{"after",after}},40000);
                    object events,seq;
                    if(reply.TryGetValue("events",out events)) { var list=events as System.Collections.IEnumerable;if(list!=null)foreach(object e in list)Send(e); }
                    if(reply.TryGetValue("seq",out seq))after=Convert.ToInt64(seq);
                    failures=0;
                } catch(Exception ex) {
                    if(token.IsCancellationRequested)break;
                    if(++failures==3)Send(new { type="error",message="호스트와의 연결이 끊겼습니다: "+ex.Message });
                    try { Task.Delay(Math.Min(5000,500*failures),token).Wait(); } catch { break; }
                }
            }
        }
    }
}
