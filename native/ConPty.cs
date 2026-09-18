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
        [DllImport("kernel32.dll")] internal static extern int CreatePseudoConsole(COORD size, IntPtr input, IntPtr output, uint flags, out IntPtr console);
        [DllImport("kernel32.dll")] internal static extern int ResizePseudoConsole(IntPtr console, COORD size);
        [DllImport("kernel32.dll")] internal static extern void ClosePseudoConsole(IntPtr console);
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
        internal static void Check(bool ok) { if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error()); }
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
        private int disposed, queuedBytes, exitRaised, consoleClosed;
        private readonly Action<string,string> onData;
        private readonly Action<string,int> onExit;

        public ConPty(string id, string cwd, string command, int cols, int rows, Action<string,string> data, Action<string,int> exit) {
            Id=id; onData=data; onExit=exit;
            IntPtr readIn=IntPtr.Zero, writeIn=IntPtr.Zero, readOut=IntPtr.Zero, writeOut=IntPtr.Zero, attributes=IntPtr.Zero;
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
                Win32.Check(Win32.CreateProcess(null,new StringBuilder(command),IntPtr.Zero,IntPtr.Zero,false,0x00080004,IntPtr.Zero,cwd,ref si,out pi));
                process=pi.process; ProcessId=pi.processId;
                Win32.Check(Win32.AssignProcessToJobObject(job,process));
                writer=new FileStream(new SafeFileHandle(writeIn,true),FileAccess.Write,4096,false); writeIn=IntPtr.Zero;
                reader=new StreamReader(new FileStream(new SafeFileHandle(readOut,true),FileAccess.Read,4096,false),new UTF8Encoding(false),false,4096); readOut=IntPtr.Zero;
                if (Win32.ResumeThread(pi.thread)==uint.MaxValue) throw new Win32Exception();
            } catch { if (process!=IntPtr.Zero) Win32.TerminateProcess(process,1); Dispose(); throw; }
            finally {
                if (attrInitialized) Win32.DeleteProcThreadAttributeList(attributes);
                if (attributes!=IntPtr.Zero) Marshal.FreeHGlobal(attributes);
                foreach(var h in new []{readIn,writeIn,readOut,writeOut,pi.thread}) if(h!=IntPtr.Zero) Win32.CloseHandle(h);
            }
        }
        public void Start() {
            new Thread(ReadLoop) { IsBackground=true,Name="Orbit output" }.Start();
            new Thread(WriteLoop) { IsBackground=true,Name="Orbit input" }.Start();
            new Thread(delegate() {
                Win32.WaitForSingleObject(process,uint.MaxValue);
                lock(lifecycle) { if(job!=IntPtr.Zero) Win32.TerminateJobObject(job,0); }
                // ClosePseudoConsole can wait until the output pipe is drained. This
                // waiter is never the UI thread; ReadLoop remains active until EOF.
                CloseConsole();
            }) { IsBackground=true,Name="Orbit process" }.Start();
        }
        private void ReadLoop() {
            int code=-1;
            try {
                char[] buffer=new char[8192]; int count;
                while((count=reader.Read(buffer,0,buffer.Length))>0) {
                    if(disposed!=0) continue; // User closed it: drain without renderer ack.
                    onData(Id,new string(buffer,0,count));
                    ack.WaitOne();
                }
                uint nativeCode; if(process!=IntPtr.Zero && Win32.GetExitCodeProcess(process,out nativeCode)) code=(int)nativeCode;
            } catch (Exception) { }
            finally { Finish(code); }
        }
        private void WriteLoop() {
            try {
                while(disposed==0) {
                    inputReady.WaitOne(); byte[] bytes;
                    while(disposed==0 && input.TryDequeue(out bytes)) {
                        Interlocked.Add(ref queuedBytes,-bytes.Length); writer.Write(bytes,0,bytes.Length); writer.Flush();
                    }
                }
            } catch(Exception) { Dispose(); }
        }
        public void Write(string text) {
            if(disposed!=0) throw new InvalidOperationException("종료된 터미널입니다.");
            byte[] bytes=Encoding.UTF8.GetBytes(text);
            if(Interlocked.Add(ref queuedBytes,bytes.Length)>1024*1024) { Interlocked.Add(ref queuedBytes,-bytes.Length); throw new InvalidOperationException("입력이 너무 큽니다. 나누어 붙여넣어 주세요."); }
            input.Enqueue(bytes); inputReady.Set();
        }
        public void Acknowledge() { ack.Set(); }
        public void Resize(int cols,int rows) { lock(lifecycle) { if(disposed==0) Marshal.ThrowExceptionForHR(Win32.ResizePseudoConsole(console,new Win32.COORD(Math.Max(10,Math.Min(500,cols)),Math.Max(3,Math.Min(300,rows))))); } }
        public void Dispose() {
            if(Interlocked.Exchange(ref disposed,1)!=0) return;
            ack.Set(); inputReady.Set();
            lock(lifecycle) { if(job!=IntPtr.Zero) { Win32.TerminateJobObject(job,0); Win32.CloseHandle(job); job=IntPtr.Zero; } }
            // ClosePseudoConsole may wait for its output to drain; keep the UI thread free.
            ThreadPool.QueueUserWorkItem(delegate {
                CloseConsole();
                if(writer!=null) writer.Dispose(); if(reader!=null) reader.Dispose();
                if(process!=IntPtr.Zero) { Win32.CloseHandle(process); process=IntPtr.Zero; }
            });
        }
        private void Finish(int code) {
            if(Interlocked.Exchange(ref exitRaised,1)!=0) return;
            Dispose();
            try { onExit(Id,code); } catch(Exception) { }
        }
        private void CloseConsole() {
            if(Interlocked.Exchange(ref consoleClosed,1)!=0) return;
            IntPtr value;
            lock(lifecycle) { value=console; console=IntPtr.Zero; }
            if(value!=IntPtr.Zero) Win32.ClosePseudoConsole(value);
        }
    }
}
