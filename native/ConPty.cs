using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Orbit {
    internal static class Win32 {
        [StructLayout(LayoutKind.Sequential)] internal struct COORD { public short X, Y; public COORD(int x, int y) { X=(short)x; Y=(short)y; } }
        [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] internal struct STARTUPINFO { public int cb; public string reserved, desktop, title; public int x,y,xSize,ySize,xChars,yChars,fill,flags; public short show, reserved2; public IntPtr reservedPtr,input,output,error; }
        [StructLayout(LayoutKind.Sequential)] internal struct STARTUPINFOEX { public STARTUPINFO StartupInfo; public IntPtr AttributeList; }
        [StructLayout(LayoutKind.Sequential)] internal struct PROCESS_INFORMATION { public IntPtr process,thread; public int processId,threadId; }
        [StructLayout(LayoutKind.Sequential)] internal struct BASIC_LIMIT { public long processTime,jobTime; public uint flags; public UIntPtr min,max; public uint active; public UIntPtr affinity; public uint priority,scheduling; }
        [StructLayout(LayoutKind.Sequential)] internal struct IO_COUNTERS { public ulong readOps,writeOps,otherOps,readBytes,writeBytes,otherBytes; }
        [StructLayout(LayoutKind.Sequential)] internal struct EXTENDED_LIMIT { public BASIC_LIMIT basic; public IO_COUNTERS io; public UIntPtr processMemory,jobMemory,peakProcess,peakJob; }
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool CreatePipe(out IntPtr read, out IntPtr write, IntPtr attrs, uint size);
        // Use one matched, app-local Microsoft ConPTY backend for the entire HPCON
        // lifetime. Old Windows 10 inbox conhost crashes on cooked input + shrink.
        [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
        [DllImport("conpty.dll",EntryPoint="ConptyCreatePseudoConsole")] internal static extern int CreatePseudoConsole(COORD size, IntPtr input, IntPtr output, uint flags, out IntPtr console);
        [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
        [DllImport("conpty.dll",EntryPoint="ConptyResizePseudoConsole")] internal static extern int ResizePseudoConsole(IntPtr console, COORD size);
        [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
        [DllImport("conpty.dll",EntryPoint="ConptyClosePseudoConsole")] internal static extern void ClosePseudoConsole(IntPtr console);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attr, IntPtr value, IntPtr size, IntPtr previous, IntPtr returned);
        [DllImport("kernel32.dll")] internal static extern void DeleteProcThreadAttributeList(IntPtr list);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] internal static extern bool CreateProcess(string app, StringBuilder command, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr env, string cwd, ref STARTUPINFOEX info, out PROCESS_INFORMATION process);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern IntPtr CreateJobObject(IntPtr attrs, string name);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool SetInformationJobObject(IntPtr job, int kind, ref EXTENDED_LIMIT info, int size);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll")] internal static extern bool TerminateProcess(IntPtr process, uint code);
        [DllImport("kernel32.dll")] internal static extern bool TerminateJobObject(IntPtr job, uint code);
        [DllImport("kernel32.dll")] internal static extern uint ResumeThread(IntPtr thread);
        [DllImport("kernel32.dll")] internal static extern uint WaitForSingleObject(IntPtr handle, uint timeout);
        [DllImport("kernel32.dll")] internal static extern bool GetExitCodeProcess(IntPtr process, out uint code);
        [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr handle);
        [DllImport("user32.dll")] internal static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("kernel32.dll")] internal static extern uint SetThreadExecutionState(uint flags);
        internal const uint ES_CONTINUOUS=0x80000000, ES_SYSTEM_REQUIRED=0x00000001;
        internal const uint WAIT_OBJECT_0=0, WAIT_TIMEOUT=258, WAIT_FAILED=0xffffffff;
        // Taskbar flash until the window is brought forward: says "look here" without taking the keyboard focus from what the user is typing.
        [StructLayout(LayoutKind.Sequential)] internal struct FLASHWINFO { public uint size; public IntPtr window; public uint flags, count, timeout; }
        [DllImport("user32.dll")] internal static extern bool FlashWindowEx(ref FLASHWINFO info);
        internal static void Flash(IntPtr window) { var info=new FLASHWINFO { size=(uint)Marshal.SizeOf(typeof(FLASHWINFO)),window=window,flags=15,count=uint.MaxValue };FlashWindowEx(ref info); }
        internal static void Check(bool ok) { if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error()); }
        // Unicode environment block for CreateProcess: this process's variables with `extra` laid over them, sorted as Windows expects.
        // The caller frees it with Marshal.FreeHGlobal. Values stay out of the command line, so other processes cannot read them there.
        internal static IntPtr AllocEnvironment(System.Collections.Generic.IDictionary<string,string> extra) {
            var merged=new System.Collections.Generic.SortedDictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            foreach(System.Collections.DictionaryEntry e in Environment.GetEnvironmentVariables()) merged[(string)e.Key]=(string)e.Value;
            foreach(var pair in extra) merged[pair.Key]=pair.Value;
            var text=new StringBuilder();
            foreach(var pair in merged) text.Append(pair.Key).Append('=').Append(pair.Value).Append('\0');
            return Marshal.StringToHGlobalUni(text.Append('\0').ToString());
        }
        // Ties a helper child process (Tailscale/cloudflared) to Orbit's own lifetime: Windows
        // kills every process in the job the moment the job's last handle closes, which happens
        // automatically even if Orbit is killed abruptly (crash, Task Manager, force-kill) rather
        // than exiting through FormClosing. Best-effort: returns Zero on failure, caller proceeds unprotected.
        internal static IntPtr AssignKillOnCloseJob(IntPtr processHandle) {
            IntPtr job=CreateJobObject(IntPtr.Zero,null); if(job==IntPtr.Zero) return IntPtr.Zero;
            var limit=new EXTENDED_LIMIT(); limit.basic.flags=0x2000;
            if(!SetInformationJobObject(job,9,ref limit,Marshal.SizeOf(limit))||!AssignProcessToJobObject(job,processHandle)) { CloseHandle(job); return IntPtr.Zero; }
            return job;
        }
    }

    internal sealed class ConPty : IDisposable {
        public readonly string Id;
        public int ProcessId;
        private IntPtr console, process, job;
        private StreamReader reader;
        private FileStream writer;
        private readonly AutoResetEvent ack = new AutoResetEvent(false), inputReady = new AutoResetEvent(false);
        private readonly ConcurrentQueue<byte[]> input = new ConcurrentQueue<byte[]>();
        private readonly object lifecycle = new object();
        private readonly object resizeGate=new object(), inputGate=new object();
        private readonly ManualResetEvent resizeDone=new ManualResetEvent(true);
        private readonly ManualResetEvent consoleCloseDone=new ManualResetEvent(false);
        private int disposed, queuedBytes, exitRaised, consoleClosed, readerEnded, inputFailed;
        private int requestedCols,requestedRows,appliedCols,appliedRows;
        private DateTime lastResizeUtc;
        private readonly System.Collections.Generic.Queue<TerminalResizeTrace> recentResizes=new System.Collections.Generic.Queue<TerminalResizeTrace>();
        private bool resizePending,resizeWorkerRunning;
        private readonly System.Collections.Generic.HashSet<string> reportedFaults=new System.Collections.Generic.HashSet<string>();
        internal Action<TerminalFault> OnFault;
        private long outputSequence, acknowledgedSequence;
        private readonly Action<string,string,long,bool> onData;
        private readonly Action<string,int> onExit;

        public ConPty(string id, string cwd, string command, int cols, int rows, Action<string,string> data, Action<string,int> exit, System.Collections.Generic.IDictionary<string,string> env=null)
            : this(id,cwd,command,cols,rows,(key,text,seq,retry)=>data(key,text),exit,env) { }
        public ConPty(string id, string cwd, string command, int cols, int rows, Action<string,string,long,bool> data, Action<string,int> exit, System.Collections.Generic.IDictionary<string,string> env=null) {
            Id=id; onData=data; onExit=exit;
            cols=ClampCols(cols);rows=ClampRows(rows);requestedCols=appliedCols=cols;requestedRows=appliedRows=rows;
            IntPtr readIn=IntPtr.Zero, writeIn=IntPtr.Zero, readOut=IntPtr.Zero, writeOut=IntPtr.Zero, attributes=IntPtr.Zero, environment=IntPtr.Zero;
            Win32.PROCESS_INFORMATION pi = new Win32.PROCESS_INFORMATION();
            bool attrInitialized=false;
            try {
                Win32.Check(Win32.CreatePipe(out readIn,out writeIn,IntPtr.Zero,0));
                Win32.Check(Win32.CreatePipe(out readOut,out writeOut,IntPtr.Zero,0));
                int hr=Win32.CreatePseudoConsole(new Win32.COORD(cols,rows),readIn,writeOut,0,out console);
                Marshal.ThrowExceptionForHR(hr);
                Win32.CloseHandle(readIn); readIn=IntPtr.Zero;
                Win32.CloseHandle(writeOut); writeOut=IntPtr.Zero;
                IntPtr size=IntPtr.Zero;
                Win32.InitializeProcThreadAttributeList(IntPtr.Zero,1,0,ref size);
                attributes=Marshal.AllocHGlobal(size);
                Win32.Check(Win32.InitializeProcThreadAttributeList(attributes,1,0,ref size)); attrInitialized=true;
                Win32.Check(Win32.UpdateProcThreadAttribute(attributes,0,new IntPtr(0x00020016),console,new IntPtr(IntPtr.Size),IntPtr.Zero,IntPtr.Zero));
                var si=new Win32.STARTUPINFOEX(); si.StartupInfo.cb=Marshal.SizeOf(si); si.AttributeList=attributes;
                job=Win32.CreateJobObject(IntPtr.Zero,null); Win32.Check(job!=IntPtr.Zero);
                var limit=new Win32.EXTENDED_LIMIT(); limit.basic.flags=0x2000;
                Win32.Check(Win32.SetInformationJobObject(job,9,ref limit,Marshal.SizeOf(limit)));
                uint flags=0x00080004; // EXTENDED_STARTUPINFO_PRESENT | CREATE_SUSPENDED
                if(env!=null&&env.Count>0) { environment=Win32.AllocEnvironment(env); flags|=0x00000400; } // CREATE_UNICODE_ENVIRONMENT
                Win32.Check(Win32.CreateProcess(null,new StringBuilder(command),IntPtr.Zero,IntPtr.Zero,false,flags,environment,cwd,ref si,out pi));
                process=pi.process; ProcessId=pi.processId;
                Win32.Check(Win32.AssignProcessToJobObject(job,process));
                writer=new FileStream(new SafeFileHandle(writeIn,true),FileAccess.Write,4096,false); writeIn=IntPtr.Zero;
                reader=new StreamReader(new FileStream(new SafeFileHandle(readOut,true),FileAccess.Read,4096,false),new UTF8Encoding(false),false,4096); readOut=IntPtr.Zero;
                if (Win32.ResumeThread(pi.thread)==uint.MaxValue) throw new Win32Exception();
            } catch { if (process!=IntPtr.Zero) Win32.TerminateProcess(process,1); Dispose(); throw; }
            finally {
                if (attrInitialized) Win32.DeleteProcThreadAttributeList(attributes);
                if (attributes!=IntPtr.Zero) Marshal.FreeHGlobal(attributes);
                if (environment!=IntPtr.Zero) Marshal.FreeHGlobal(environment);
                foreach(var h in new []{readIn,writeIn,readOut,writeOut,pi.thread}) if(h!=IntPtr.Zero) Win32.CloseHandle(h);
            }
        }
        public void Start() {
            new Thread(ReadLoop) { IsBackground=true,Name="Orbit output" }.Start();
            new Thread(WriteLoop) { IsBackground=true,Name="Orbit input" }.Start();
            new Thread(delegate() {
                // A failed wait is not an exit. Closing the pseudoconsole in that case makes
                // ReadLoop see EOF, and GetExitCodeProcess then reports STILL_ACTIVE (259);
                // the old path treated that value as an exit and killed the live job.
                if(Win32.WaitForSingleObject(process,uint.MaxValue)!=Win32.WAIT_OBJECT_0)return;
                lock(lifecycle) { if(job!=IntPtr.Zero) Win32.TerminateJobObject(job,0); }
                // ClosePseudoConsole can wait until the output pipe is drained. This
                // waiter is never the UI thread; ReadLoop remains active until EOF.
                CloseConsole();
                // Normally ReadLoop observes the EOF caused above and owns completion. If
                // its pipe ended early while the process was still alive, it has already
                // returned, so complete the session here after the real process signal.
                if(Volatile.Read(ref readerEnded)!=0)Finish(ExitCode());
            }) { IsBackground=true,Name="Orbit process" }.Start();
        }
        private void ReadLoop() {
            try {
                char[] buffer=new char[8192]; int count;
                while((count=reader.Read(buffer,0,buffer.Length))>0) {
                    if(disposed!=0) continue; // User closed it: drain without renderer ack.
                    // Keep exactly one chunk until its own ACK arrives. Retry the same
                    // sequence after an ACK/post failure; never read ahead into an unbounded
                    // renderer backlog. The host coalesces posts while its UI is stalled.
                    string text=new string(buffer,0,count);
                    long sequence=Interlocked.Increment(ref outputSequence);bool retry=false;
                    do {
                        try {onData(Id,text,sequence,retry);}catch(Exception){}
                        retry=true;
                        if(disposed!=0||Interlocked.Read(ref acknowledgedSequence)>=sequence)break;
                        ack.WaitOne(1000);
                    } while(disposed==0&&Interlocked.Read(ref acknowledgedSequence)<sequence);
                }
            } catch (Exception error) { ReportFault("read",error); }
            finally {
                Volatile.Write(ref readerEnded,1);
                // EOF alone does not prove that the terminal process ended. In particular,
                // 259 from GetExitCodeProcess means STILL_ACTIVE unless the handle is signaled.
                if(ProcessExited())Finish(ExitCode());
                else if(disposed==0)ReportFault("output-eof",null);
            }
        }
        internal static bool IsExitWait(uint result) { return result==Win32.WAIT_OBJECT_0; }
        private bool ProcessExited() {IntPtr value=process;return value!=IntPtr.Zero&&IsExitWait(Win32.WaitForSingleObject(value,0));}
        private int ExitCode() {uint code;IntPtr value=process;return value!=IntPtr.Zero&&Win32.GetExitCodeProcess(value,out code)?unchecked((int)code):-1;}
        private void WriteLoop() {
            try {
                while(disposed==0) {
                    inputReady.WaitOne(); byte[] bytes;
                    while(disposed==0 && input.TryDequeue(out bytes)) {
                        Interlocked.Add(ref queuedBytes,-bytes.Length); writer.Write(bytes,0,bytes.Length); writer.Flush();
                    }
                }
            } catch(Exception error) {
                // A pipe failure is not process exit. Keep the job alive for diagnosis or
                // deliberate close, and stop accepting bytes into an abandoned queue.
                lock(inputGate) { inputFailed=1;byte[] discarded;while(input.TryDequeue(out discarded)){}Interlocked.Exchange(ref queuedBytes,0); }
                ReportFault("write",error);
            }
        }
        public void Write(string text) {
            lock(inputGate) {
            if(inputFailed!=0)throw new InvalidOperationException("터미널 입력 연결이 끊겼습니다. 실행 중인 프로세스는 자동으로 종료하지 않았습니다.");
            if(disposed!=0) throw new InvalidOperationException("종료된 터미널입니다.");
            byte[] bytes=Encoding.UTF8.GetBytes(text);
            if(Interlocked.Add(ref queuedBytes,bytes.Length)>1024*1024) { Interlocked.Add(ref queuedBytes,-bytes.Length); throw new InvalidOperationException("입력이 너무 큽니다. 나누어 붙여넣어 주세요."); }
            input.Enqueue(bytes); inputReady.Set();
            }
        }
        public void Acknowledge() { Acknowledge(Interlocked.Read(ref outputSequence)); }
        public void Acknowledge(long sequence) {
            if(sequence<=0||sequence!=Interlocked.Read(ref outputSequence))return;
            Interlocked.Exchange(ref acknowledgedSequence,sequence);ack.Set();
        }
        internal static int ClampCols(int cols) { return Math.Max(10,Math.Min(500,cols)); }
        internal static int ClampRows(int rows) { return Math.Max(3,Math.Min(300,rows)); }
        public void Resize(int cols,int rows) {
            lock(resizeGate) {
                if(disposed!=0||consoleClosed!=0)return;
                requestedCols=ClampCols(cols);requestedRows=ClampRows(rows);lastResizeUtc=DateTime.UtcNow;resizePending=true;
                if(resizeWorkerRunning)return;
                resizeWorkerRunning=true;resizeDone.Reset();
                try {if(!ThreadPool.QueueUserWorkItem(_=>ResizeLoop()))throw new InvalidOperationException("Resize scheduling failed");}
                catch(Exception error) {resizeWorkerRunning=false;resizeDone.Set();ReportFault("resize",error);}
            }
        }
        private void ResizeLoop() {
            bool released=false;
            try {
                while(true) {
                    int cols,rows;
                    lock(resizeGate) {
                        if(disposed!=0||consoleClosed!=0||!resizePending) {resizeWorkerRunning=false;resizeDone.Set();released=true;return;}
                        cols=requestedCols;rows=requestedRows;resizePending=false;if(cols==appliedCols&&rows==appliedRows)continue;
                    }
                    IntPtr value;lock(lifecycle)value=console;
                    if(disposed!=0||consoleClosed!=0||value==IntPtr.Zero)break;
                    // The control pipe can block while conhost waits for output to drain.
                    // Never hold the UI thread or lifecycle lock: its ACK/close must run.
                    var trace=new TerminalResizeTrace {Utc=DateTime.UtcNow.ToString("o"),Cols=cols,Rows=rows};
                    lock(resizeGate) {recentResizes.Enqueue(trace);while(recentResizes.Count>12)recentResizes.Dequeue();}
                    int hr=Win32.ResizePseudoConsole(value,new Win32.COORD(cols,rows));
                    lock(resizeGate)trace.HResult=hr.ToString("X8");
                    if(hr<0) { ReportFault("resize",Marshal.GetExceptionForHR(hr));continue; }
                    lock(resizeGate) {appliedCols=cols;appliedRows=rows;}
                }
            } catch(Exception error) {ReportFault("resize",error);}
            finally {if(!released)lock(resizeGate){resizeWorkerRunning=false;resizeDone.Set();}}
        }
        private void ReportFault(string operation,Exception error) {
            if(disposed!=0)return;
            var value=process;uint wait=value==IntPtr.Zero?Win32.WAIT_FAILED:Win32.WaitForSingleObject(value,0);
            int cols,rows,actualCols,actualRows;DateTime resized;lock(resizeGate){cols=requestedCols;rows=requestedRows;actualCols=appliedCols;actualRows=appliedRows;resized=lastResizeUtc;}
            var fault=new TerminalFault { Session=Id,Pid=ProcessId,Operation=operation,HResult=error==null?0:error.HResult,ProcessState=wait==Win32.WAIT_OBJECT_0?"exited":wait==Win32.WAIT_TIMEOUT?"running":"unknown",Cols=cols,Rows=rows,AppliedCols=actualCols,AppliedRows=actualRows,InputDisconnected=Volatile.Read(ref inputFailed)!=0,LastResizeUtc=resized==default(DateTime)?null:resized.ToString("o"),ExitCode=wait==Win32.WAIT_OBJECT_0?(int?)ExitCode():null };
            lock(resizeGate)fault.RecentResizes=recentResizes.ToArray();
            lock(reportedFaults) {if(!reportedFaults.Add(operation+":"+fault.HResult+":"+fault.ProcessState))return;}
            try {if(OnFault!=null)OnFault(fault);}catch(Exception){}
        }
        public void Dispose() {
            if(Interlocked.Exchange(ref disposed,1)!=0) return;
            ack.Set(); inputReady.Set();
            lock(lifecycle) { if(job!=IntPtr.Zero) { Win32.TerminateJobObject(job,0); Win32.CloseHandle(job); job=IntPtr.Zero; } }
            // ClosePseudoConsole may wait for its output to drain; keep the UI thread free.
            ThreadPool.QueueUserWorkItem(delegate {
                try {CloseConsole();}catch(Exception){}
                // Dispose can flush a broken FileStream. Cleanup faults must never escape
                // the pool callback or prevent the remaining resources from closing.
                try {if(writer!=null)writer.Dispose();}catch(Exception){}
                try {if(reader!=null)reader.Dispose();}catch(Exception){}
                lock(lifecycle) {if(process!=IntPtr.Zero) {Win32.CloseHandle(process);process=IntPtr.Zero;}}
            });
        }
        private void Finish(int code) {
            if(Interlocked.Exchange(ref exitRaised,1)!=0) return;
            ReportFault("process-exit",null);
            Dispose();
            try { onExit(Id,code); } catch(Exception) { }
        }
        private void CloseConsole() {
            if(Interlocked.Exchange(ref consoleClosed,1)!=0) {consoleCloseDone.WaitOne();return;}
            try {
            // Only called by background teardown/waiter. Keep the HPCON valid until its
            // sole resize worker returns; job termination above releases a blocked host.
            resizeDone.WaitOne();
            IntPtr value;
            lock(lifecycle) { value=console; console=IntPtr.Zero; }
            if(value!=IntPtr.Zero) Win32.ClosePseudoConsole(value);
            }finally{consoleCloseDone.Set();}
        }
    }
}
