using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Orbit {
    // One outstanding chunk per local terminal. A receipt permits another retry
    // only after JS received the previous post; renderer/UI stalls cannot pile up
    // either WinForms callbacks or WebView messages.
    internal sealed class TerminalOutputDelivery {
        public readonly long Sequence;
        public readonly object Message;
        private int posted;
        public TerminalOutputDelivery(long sequence,object message) { Sequence=sequence;Message=message; }
        public bool TryPost() { return Interlocked.CompareExchange(ref posted,1,0)==0; }
        public void Received(long sequence) { if(sequence==Sequence)Volatile.Write(ref posted,0); }
    }
    internal sealed partial class MainWindow {
        private readonly ConcurrentDictionary<string,TerminalOutputDelivery> terminalDeliveries=new ConcurrentDictionary<string,TerminalOutputDelivery>();
        private void DeliverTerminalOutput(string key,string chunk,long sequence,bool retry) {
            if(quitting||!sessions.ContainsKey(key))return;
            TerminalOutputDelivery delivery;
            if(!retry||!terminalDeliveries.TryGetValue(key,out delivery)) {
                RecordOutput(key,chunk,sequence);
                delivery=new TerminalOutputDelivery(sequence,new {type="output",session=key,data=chunk,seq=sequence,recoverable=true});
                terminalDeliveries[key]=delivery;
                if(!sessions.ContainsKey(key)) { ForgetTerminalOutput(key);return; }
                // Rings and remote viewers receive each chunk once, including when the
                // local renderer needs a retry. Remote-owned terminals keep their existing
                // queue-based flow control and ACK themselves when that queue drains.
                EmitOutput(key,delivery.Message,chunk.Length);
                return;
            }
            if(sessionOwners.ContainsKey(key))return;
            if(terminalDeliveries.TryGetValue(key,out delivery)&&delivery.Sequence==sequence)PostTerminalOutput(key,delivery);
        }
        private void PostTerminalOutput(string key,TerminalOutputDelivery delivery) {
            if(quitting||IsDisposed||!IsHandleCreated||!delivery.TryPost())return;
            Action post=delegate {
                TerminalOutputDelivery current;
                if(quitting||IsDisposed||!sessions.ContainsKey(key)||!terminalDeliveries.TryGetValue(key,out current)||!Object.ReferenceEquals(current,delivery))return;
                try {
                    if(web.CoreWebView2==null) { delivery.Received(delivery.Sequence);return; }
                    web.CoreWebView2.PostWebMessageAsJson(json.Serialize(delivery.Message));
                } catch(Exception) { delivery.Received(delivery.Sequence); }
            };
            try { if(InvokeRequired)BeginInvoke(post);else post(); }
            catch(InvalidOperationException) { delivery.Received(delivery.Sequence); }
        }
        private void ReceiveTerminalOutput(string key,long sequence) {
            TerminalOutputDelivery delivery;
            if(terminalDeliveries.TryGetValue(key,out delivery))delivery.Received(sequence);
        }
        private void AcknowledgeTerminalOutput(string key,long sequence) {
            ConPty terminal;TerminalOutputDelivery delivery;
            if(!sessions.TryGetValue(key,out terminal))return;
            if(terminalDeliveries.TryGetValue(key,out delivery)&&delivery.Sequence==sequence) {
                delivery.Received(sequence);
                terminal.Acknowledge(sequence);
            }
        }
        private void ForgetTerminalOutput(string key) { TerminalOutputDelivery ignored;terminalDeliveries.TryRemove(key,out ignored); }
    }
}
