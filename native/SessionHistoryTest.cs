using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
namespace Orbit {
    internal static class SessionHistoryTest {
        internal static void Run(string root) {
            string oldCodex=Environment.GetEnvironmentVariable("CODEX_HOME"),oldClaude=Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            try {
                string work=Path.Combine(root,"작업 공간"),gone=Path.Combine(root,"gone"),codex=Path.Combine(root,"codex"),claude=Path.Combine(root,"claude"); Directory.CreateDirectory(work);Directory.CreateDirectory(Path.Combine(codex,"sessions","2026","09","17"));Directory.CreateDirectory(Path.Combine(claude,"projects","fixture"));
                string id="11111111-1111-4111-8111-111111111111",sub="22222222-2222-4222-8222-222222222222",cid="33333333-3333-4333-8333-333333333333",messageId="44444444-4444-4444-8444-444444444444",guardian="66666666-6666-4666-8666-666666666666",nestedSubagent="77777777-7777-4777-8777-777777777777";
                File.WriteAllText(Path.Combine(codex,"session_index.jsonl"),"{\"id\":\""+id+"\",\"thread_name\":\"Joined title\"}\n",new UTF8Encoding(false));
                string escaped=work.Replace("\\","\\\\");
                File.WriteAllText(Path.Combine(codex,"sessions","2026","09","17","rollout.jsonl"),"{\"type\":\"session_meta\",\"payload\":{\"id\":\""+id+"\",\"cwd\":\""+escaped+"\",\"thread_source\":\"user\",\"title\":\"\"}}\n{\"type\":\"session_meta\",\"payload\":{\"id\":\""+sub+"\",\"cwd\":\""+escaped+"\",\"thread_source\":\"subagent\"}}\n{\"type\":\"session_meta\",\"payload\":{\"id\":\""+guardian+"\",\"cwd\":\""+escaped+"\",\"thread_source\":\"guardian_review\"}}\n{\"type\":\"session_meta\",\"payload\":{\"id\":\""+nestedSubagent+"\",\"cwd\":\""+escaped+"\",\"source\":{\"subagent\":\"review\"}}}\n{bad}\n",new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(claude,"projects","fixture",cid+".jsonl"),"{\"id\":\""+messageId+"\",\"sessionId\":\""+cid+"\",\"cwd\":\""+escaped+"\",\"summary\":\"Claude title\"}\n{\"id\":\""+messageId+"\",\"sessionId\":\""+cid+"\",\"cwd\":\""+escaped+"\",\"type\":\"assistant\"}\n",new UTF8Encoding(false));
                Environment.SetEnvironmentVariable("CODEX_HOME",codex);Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR",claude);
                var local=SessionHistory.List(work,false); if(local.Length!=2||local.Any(x=>x.Id==sub)||local.Any(x=>x.Id==guardian)||local.Any(x=>x.Id==nestedSubagent)||local.Any(x=>x.Id==messageId)||local.First(x=>x.Id==id).Title!="Joined title"||local.First(x=>x.Id==cid).Title!="Claude title") throw new InvalidOperationException("saved session fixture parsing failed");
                File.AppendAllText(Path.Combine(claude,"projects","fixture",cid+".jsonl"),"{\"type\":\"custom-title\",\"sessionId\":\""+cid+"\",\"customTitle\":\"Claude custom title\"}\n",new UTF8Encoding(false));
                if(SessionHistory.List(work,false).First(x=>x.Id==cid).Title!="Claude custom title")throw new InvalidOperationException("Claude custom title without cwd was not merged");
                if(SessionHistory.List(gone,false).Length!=0||!SessionHistory.IsId(id)||SessionHistory.IsId("x;calc"))throw new InvalidOperationException("saved session validation failed");
                string large=Path.Combine(codex,"sessions","2026","09","17","large.jsonl"); File.WriteAllText(large,"{\"type\":\"session_meta\",\"payload\":{\"id\":\"55555555-5555-4555-8555-555555555555\",\"cwd\":\""+escaped+"\"}}\n"+new string('x',300*1024),new UTF8Encoding(false));
                using(var writer=new FileStream(large,FileMode.Open,FileAccess.Write,FileShare.ReadWrite)) if(!SessionHistory.List(work,false).Any(x=>x.Id=="55555555-5555-4555-8555-555555555555"))throw new InvalidOperationException("large or actively-written history was skipped");
                var simultaneous=Task.WhenAll(Enumerable.Range(0,24).Select(_=>Task.Run(()=>SessionHistory.List(work,false)))).Result; if(simultaneous.Any(x=>x.Length!=3||x.First(y=>y.Id==id).Title!="Joined title")) throw new InvalidOperationException("concurrent saved-session reads were unstable");
                File.AppendAllText(Path.Combine(claude,"projects","fixture",cid+".jsonl"),"{\"sessionId\":\""+cid+"\",\"cwd\":\""+escaped+"\",\"type\":\"user\",\"content\":\"later titleless user record\"}\n",new UTF8Encoding(false));
                if(SessionHistory.List(work,false).First(x=>x.Id==cid).Title!="Claude custom title")throw new InvalidOperationException("later titleless record erased Claude title");
                string other="88888888-8888-4888-8888-888888888888";
                File.WriteAllText(Path.Combine(claude,"history.jsonl"),"{\"sessionId\":\""+cid+"\",\"text\":\"delete me\"}\n{bad}\n{\"sessionId\":\""+other+"\",\"text\":\"keep me\"}\n",new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(claude,"projects","fixture","sessions-index.json"),"{\"version\":1,\"entries\":[{\"sessionId\":\""+cid+"\"},{\"sessionId\":\""+other+"\"}]}",new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(claude,"projects","fixture",cid+".orphaned-1.jsonl"),"orphan",new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(claude,"projects","fixture",cid+".jsonl.superseded-1"),"superseded",new UTF8Encoding(false));
                Directory.CreateDirectory(Path.Combine(claude,"file-history",cid));File.WriteAllText(Path.Combine(claude,"file-history",cid,"old.txt"),"old");
                SessionHistory.Delete("claude",cid,work,false);
                if(SessionHistory.List(work,false).Any(x=>x.Id==cid)||!SessionHistory.List(work,false).Any(x=>x.Id==id))throw new InvalidOperationException("deleted Claude session remained listed or unrelated session disappeared");
                string history=File.ReadAllText(Path.Combine(claude,"history.jsonl"));if(history.IndexOf(cid,StringComparison.OrdinalIgnoreCase)>=0||history.IndexOf(other,StringComparison.OrdinalIgnoreCase)<0||history.IndexOf("{bad}",StringComparison.Ordinal)<0)throw new InvalidOperationException("Claude history filtering lost unrelated rows");
                string index=File.ReadAllText(Path.Combine(claude,"projects","fixture","sessions-index.json"));if(index.IndexOf(cid,StringComparison.OrdinalIgnoreCase)>=0||index.IndexOf(other,StringComparison.OrdinalIgnoreCase)<0)throw new InvalidOperationException("Claude sessions index filtering failed");
                if(File.Exists(Path.Combine(claude,"projects","fixture",cid+".jsonl"))||File.Exists(Path.Combine(claude,"projects","fixture",cid+".orphaned-1.jsonl"))||File.Exists(Path.Combine(claude,"projects","fixture",cid+".jsonl.superseded-1"))||Directory.Exists(Path.Combine(claude,"file-history",cid)))throw new InvalidOperationException("Claude scoped artifacts remained");
                bool invalid=false;try{SessionHistory.Delete("claude","x;calc",work,false);}catch(InvalidOperationException){invalid=true;}if(!invalid)throw new InvalidOperationException("invalid deletion ID was accepted");
            } finally { Environment.SetEnvironmentVariable("CODEX_HOME",oldCodex);Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR",oldClaude); }
        }
    }
}
