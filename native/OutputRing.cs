using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Orbit {
    internal sealed class OutputRing {
        sealed class Item { public long Seq; public string Data; }
        readonly object gate=new object(); readonly List<Item> items=new List<Item>();
        long next,generation; int bytes; bool closed;
        public long Advance(string data,bool record) {
            lock(gate) { long seq=++next;if(!record){items.Clear();bytes=0;Monitor.PulseAll(gate);return seq;}var item=new Item { Seq=seq,Data=data };items.Add(item);bytes+=data.Length;
                while(items.Count>256||bytes>1024*1024){bytes-=items[0].Data.Length;items.RemoveAt(0);}
                Monitor.PulseAll(gate);return item.Seq;
            }
        }
        public void Clear() { lock(gate){generation++;items.Clear();bytes=0;Monitor.PulseAll(gate);} }
        public void Close() { lock(gate){generation++;closed=true;items.Clear();bytes=0;Monitor.PulseAll(gate);} }
        // The recent output as one text, for viewers that cannot get a screen snapshot from a UI terminal.
        public string Replay(out long seq) { lock(gate) { seq=next;return String.Concat(items.Select(x=>x.Data)); } }
        public object Snapshot() { lock(gate) { return new { seq=next,items=items.Select(x=>new {seq=x.Seq,data=x.Data}).ToArray() }; } }
        public object After(long after,int timeout) {
            DateTime end=DateTime.UtcNow.AddMilliseconds(Math.Max(0,Math.Min(25000,timeout)));
            lock(gate) { long entered=generation; while(true) {
                long first=items.Count==0?next+1:items[0].Seq;
                if(closed)throw new InvalidOperationException("terminal not found");
                if(entered!=generation)return new { reset=true,seq=next,items=new object[0] };
                if(after<first-1)return new { reset=true,seq=next,items=items.Select(x=>new {seq=x.Seq,data=x.Data}).ToArray() };
                var result=items.Where(x=>x.Seq>after).Select(x=>new {seq=x.Seq,data=x.Data}).ToArray();
                if(result.Length>0)return new { reset=false,seq=next,items=result };
                int wait=(int)Math.Max(0,(end-DateTime.UtcNow).TotalMilliseconds);if(wait==0)return new { reset=false,seq=after,items=new object[0] };
                Monitor.Wait(gate,wait);
            }}
        }
    }
}
