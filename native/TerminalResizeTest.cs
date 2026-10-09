using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;

namespace Orbit {
    internal static class TerminalResizeTest {
        internal static void Run(string root) {
            ConPty terminal=null;var ready=new ManualResetEvent(false);var paused=new ManualResetEvent(false);int drain=1;
            terminal=new ConPty("resize",root,"cmd.exe /d /q",2,1,(id,text,seq,retry)=>{ready.Set();if(Volatile.Read(ref drain)!=0)terminal.Acknowledge(seq);else paused.Set();},(id,code)=>{});
            try {
                terminal.Start();if(!ready.WaitOne(10000))throw new Exception("resize fixture did not start");
                if(Field<int>(terminal,"appliedCols")!=10||Field<int>(terminal,"appliedRows")!=3)throw new Exception("initial grid not clamped");
                Volatile.Write(ref drain,0);terminal.Write("for /l %i in (1,1,20000) do @echo RESIZE_BACKPRESSURE_%i\r");
                if(!paused.WaitOne(5000))throw new Exception("ACK pause not reached");Thread.Sleep(300);
                bool blocked=false;var timer=Stopwatch.StartNew();
                for(int i=0;i<1500;i++) {
                    int cols=i%2==0?500:10,rows=i%2==0?300:3;terminal.Resize(cols,rows);
                    if(!blocked&&!SpinWait.SpinUntil(()=>Field<int>(terminal,"appliedCols")==cols&&Field<int>(terminal,"appliedRows")==rows,100))blocked=true;
                }
                terminal.Resize(123,37);
                if(timer.ElapsedMilliseconds>5000)throw new Exception("resize requests blocked caller under output backpressure");
                if(Process.GetProcessById(terminal.ProcessId).HasExited)throw new Exception("resize killed shell");
                Volatile.Write(ref drain,1);terminal.Acknowledge();
                if(!SpinWait.SpinUntil(()=>Field<int>(terminal,"appliedCols")==123&&Field<int>(terminal,"appliedRows")==37,10000))throw new Exception("latest coalesced size not applied");
                Volatile.Write(ref drain,0);paused.Reset();terminal.Write("for /l %i in (1,1,20000) do @echo CLOSE_BACKPRESSURE_%i\r");
                if(!paused.WaitOne(5000))throw new Exception("close ACK pause not reached");Thread.Sleep(300);
                for(int i=0;i<1000;i++) {int cols=i%2==0?500:10;terminal.Resize(cols,i%2==0?300:3);if(!SpinWait.SpinUntil(()=>Field<int>(terminal,"appliedCols")==cols,100))break;}
                var process=Process.GetProcessById(terminal.ProcessId);timer.Restart();terminal.Dispose();
                if(timer.ElapsedMilliseconds>250)throw new Exception("close blocked caller behind resize");
                if(!process.WaitForExit(5000)||!Field<ManualResetEvent>(terminal,"resizeDone").WaitOne(5000))throw new Exception("close left shell/resize worker running");
            }finally{terminal.Dispose();}
        }
        internal static void InputFault(string root) {
            ConPty terminal=null;var ready=new ManualResetEvent(false);var failed=new ManualResetEvent(false);TerminalFault fault=null;int exitCount=0;FileStream original=null;
            terminal=new ConPty("input-fault",root,"cmd.exe /d /q",80,24,(id,text)=>{ready.Set();terminal.Acknowledge();},(id,code)=>Interlocked.Increment(ref exitCount));
            terminal.OnFault=value=>{TerminalDiagnostics.Record(root,value);if(value.Operation=="write"){fault=value;failed.Set();}};
            try {
                terminal.Start();if(!ready.WaitOne(10000))throw new Exception("input fault fixture did not start");
                var process=Process.GetProcessById(terminal.ProcessId);var handle=process.Handle;
                original=Field<FileStream>(terminal,"writer");
                typeof(ConPty).GetField("writer",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(terminal,new FailingWriteStream(Path.Combine(root,"input-fault-sink.bin")));
                terminal.Write("echo INPUT_FAULT_FIXTURE\r");
                if(!failed.WaitOne(5000))throw new Exception("input failure not reported");Thread.Sleep(150);
                if(process.HasExited||exitCount!=0||fault.ProcessState!="running"||!fault.InputDisconnected)throw new Exception("pipe failure incorrectly killed/reported shell exit");
                bool rejected=false;try{terminal.Write("rejected");}catch(InvalidOperationException){rejected=true;}
                if(!rejected||Field<int>(terminal,"queuedBytes")!=0)throw new Exception("dead writer accepted/retained input");
                terminal.Dispose();if(!process.WaitForExit(5000))throw new Exception("disconnected terminal deliberate close failed");
            }finally{terminal.Dispose();if(original!=null)try{original.Dispose();}catch(Exception){}}
            ConPty exiting=null;var exited=new ManualResetEvent(false);int codeSeen=-1;
            exiting=new ConPty("genuine-exit",root,"cmd.exe /d /q",80,24,(id,text)=>exiting.Acknowledge(),(id,code)=>{codeSeen=code;exited.Set();});
            try {exiting.Start();exiting.Write("exit 17\r");if(!exited.WaitOne(10000)||codeSeen!=17)throw new Exception("genuine exit code changed");}finally{exiting.Dispose();}
        }
        static T Field<T>(ConPty terminal,string name) {return (T)typeof(ConPty).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(terminal);}
        sealed class ThrowingDisposeStream : FileStream {
            internal ThrowingDisposeStream(string path):base(path,FileMode.Create,FileAccess.Write,FileShare.ReadWrite){}
            protected override void Dispose(bool disposing) {base.Dispose(disposing);if(disposing)throw new IOException("Injected cleanup failure");}
        }
        sealed class FailingWriteStream : FileStream {
            internal FailingWriteStream(string path):base(path,FileMode.Create,FileAccess.Write,FileShare.ReadWrite){}
            public override void Write(byte[] bytes,int offset,int count) {throw new IOException("Injected input transport fault",unchecked((int)0x800700E8));}
        }
        internal static void CleanupFault(string root) {
            ConPty terminal=null;var ready=new ManualResetEvent(false);FileStream original=null;
            terminal=new ConPty("cleanup-fault",root,"cmd.exe /d /q",80,24,(id,text)=>{ready.Set();terminal.Acknowledge();},(id,code)=>{});
            try {
                terminal.Start();if(!ready.WaitOne(10000))throw new Exception("cleanup fixture did not start");
                original=Field<FileStream>(terminal,"writer");
                typeof(ConPty).GetField("writer",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(terminal,new ThrowingDisposeStream(Path.Combine(root,"cleanup-sink.bin")));
                var process=Process.GetProcessById(terminal.ProcessId);terminal.Dispose();
                if(!process.WaitForExit(5000)||!SpinWait.SpinUntil(()=>Field<IntPtr>(terminal,"process")==IntPtr.Zero,5000))throw new Exception("cleanup exception prevented process handle release");
            }finally{terminal.Dispose();if(original!=null)try{original.Dispose();}catch(Exception){}}
        }
        internal static void CookedInput(string root) {
            ConPty terminal=null;var ready=new ManualResetEvent(false);var complete=new ManualResetEvent(false);int drain=1;var output=new System.Text.StringBuilder();
            terminal=new ConPty("cooked-input",root,"cmd.exe /d /q",147,41,(id,text,seq,retry)=> {
                ready.Set();lock(output){if(!retry)output.Append(text);if(TerminalOutputRecoveryTest.WithoutOsc(output.ToString()).Contains("\r\n한글_COOKED_OK"))complete.Set();}
                if(Volatile.Read(ref drain)!=0)terminal.Acknowledge(seq);
            },(id,code)=>{});
            try {
                terminal.Start();if(!ready.WaitOne(10000))throw new Exception("cooked input fixture did not start");
                terminal.Resize(10,3);if(!SpinWait.SpinUntil(()=>Field<int>(terminal,"appliedCols")==10,5000))throw new Exception("cooked input shrink not applied");
                terminal.Write("echo "+new string('X',180)+String.Concat(System.Linq.Enumerable.Repeat("한글",30)));Thread.Sleep(150);Volatile.Write(ref drain,0);
                for(int i=0;i<240;i++){int cols=i%2==0?500:10,rows=i%2==0?300:3;terminal.Resize(cols,rows);SpinWait.SpinUntil(()=>Field<int>(terminal,"appliedCols")==cols&&Field<int>(terminal,"appliedRows")==rows,100);}
                if(Process.GetProcessById(terminal.ProcessId).HasExited)throw new Exception("cooked ASCII/CJK input resize crashed host/shell");
                terminal.Resize(120,40);Volatile.Write(ref drain,1);terminal.Acknowledge();
                if(!SpinWait.SpinUntil(()=>Field<int>(terminal,"appliedCols")==120,5000))throw new Exception("cooked input resize did not recover");
                // Commit the harmless pending echo, then test a fresh Unicode command.
                // This fixture does not assume that Ctrl+C cancels cooked input while
                // the host is still draining queued redraws from the ACK pause.
                terminal.Write("\r");Thread.Sleep(250);terminal.Write("echo 한글_COOKED_OK\r");
                if(!complete.WaitOne(10000)) {lock(output){string text=TerminalOutputRecoveryTest.WithoutOsc(output.ToString());throw new Exception("Unicode command output did not survive cooked input resize; synthetic fixture tail="+text.Substring(Math.Max(0,text.Length-600)));}}
            }finally{terminal.Dispose();}
        }
    }
}
