using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Orbit {
    internal static class TerminalOutputRecoveryTest {
        internal static void Run(string root) {
            var delivery=new TerminalOutputDelivery(7,new object());
            if(!delivery.TryPost())throw new Exception("first post refused");
            for(int i=0;i<10000;i++)if(delivery.TryPost())throw new Exception("stalled UI/renderer permits another queued post");
            delivery.Received(6);if(delivery.TryPost())throw new Exception("stale receipt unlocks a later delivery");
            delivery.Received(7);if(!delivery.TryPost())throw new Exception("receipt did not allow retry");

            ConPty terminal=null;
            var output=new StringBuilder();var first=new ManualResetEvent(false);var retrySeen=new ManualResetEvent(false);var complete=new ManualResetEvent(false);var floodDone=new ManualResetEvent(false);
            long firstSequence=0,lastSequence=0;string lastText=null;int drain=0,retries=0;Exception failure=null;
            terminal=new ConPty("recovery",root,"cmd.exe /d /q",120,40,(id,text,sequence,retry)=> {
                lock(output) {
                    if(retry) { retries++;retrySeen.Set();if(sequence!=lastSequence||text!=lastText)failure=new Exception("retry changed sequence or bytes"); }
                    else { if(sequence!=lastSequence+1)failure=new Exception("output sequence skipped");lastSequence=sequence;lastText=text;output.Append(text); }
                    if(firstSequence==0) { firstSequence=sequence;first.Set(); }
                    if(WithoutOsc(output.ToString()).Contains("\r\nRECOVERY_COMPLETE"))complete.Set();
                    if(output.ToString().Contains("RECOVERY_ROW_1000"))floodDone.Set();
                }
                if(Volatile.Read(ref drain)!=0)terminal.Acknowledge(sequence);
            },(id,code)=>{});
            try {
                terminal.Start();if(!first.WaitOne(10000))throw new Exception("no initial PTY output");
                int pid=terminal.ProcessId;
                terminal.Write("for /l %i in (1,1,1000) do @echo RECOVERY_ROW_%i\r");
                terminal.Acknowledge(firstSequence-1);terminal.Acknowledge(firstSequence+1);
                if(!retrySeen.WaitOne(4000))throw new Exception("lost ACK did not retry");
                Thread.Sleep(1100);
                lock(output)if(lastSequence!=firstSequence)throw new Exception("stale/future ACK released a chunk");
                Volatile.Write(ref drain,1); // Same-sequence retry, rather than a manual ACK, resumes the stream.
                if(!floodDone.WaitOne(15000))throw new Exception("flood did not drain after ACK recovery");
                Thread.Sleep(150);
                terminal.Write("echo RECOVERY_COMPLETE\r");
                if(!complete.WaitOne(15000)) { lock(output)throw new Exception("output did not recover; sequences="+lastSequence+", retries="+retries+", characters="+output.Length+", tail="+output.ToString().Substring(Math.Max(0,output.Length-500))); }
                lock(output) {
                    if(failure!=null)throw failure;
                    var rows=Regex.Matches(output.ToString(),"RECOVERY_ROW_[0-9]+").Cast<Match>().Select(x=>x.Value).ToArray();
                    // Conhost emits VT screen redraws after backpressure, so raw bytes can
                    // legitimately repeat visible text. Exact displayed row count/order is
                    // checked by the WebView/xterm probe, not by stripping VT cursor moves.
                    if(!rows.Contains("RECOVERY_ROW_1000")||output.Length<40000)throw new Exception("flood was not drained");
                }
                if(terminal.ProcessId!=pid||Process.GetProcessById(pid).HasExited)throw new Exception("recovery replaced/exited terminal");
            } finally { terminal.Dispose(); }

            ConPty closing=null;var waiting=new ManualResetEvent(false);var readerDone=new ManualResetEvent(false);
            closing=new ConPty("close-pending",root,"cmd.exe /d /q",80,24,(id,text,seq,retry)=>waiting.Set(),(id,code)=>readerDone.Set());
            closing.Start();if(!waiting.WaitOne(10000)){closing.Dispose();throw new Exception("close fixture did not start");}
            var process=Process.GetProcessById(closing.ProcessId);closing.Dispose();
            if(!process.WaitForExit(5000))throw new Exception("disposing during ACK wait left process running");
        }
        internal static string WithoutOsc(string text) {return Regex.Replace(text,"\x1b\\][^\x07\x1b]*(?:\x07|\x1b\\\\)","");}
    }
}
