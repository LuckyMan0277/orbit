using System;
using System.IO;
using System.Text;

namespace Orbit {
    // Uses the installed CLI only with a disposable CODEX_HOME fixture.
    internal static class CodexDeleteIntegrationTest {
        internal static void Run(string root) {
            string previousHome=Environment.GetEnvironmentVariable("CODEX_HOME"),previousPath=Environment.GetEnvironmentVariable("Path");
            string shortRoot=Path.Combine(Environment.CurrentDirectory,"artifacts","delete-fixture-"+Guid.NewGuid().ToString("N").Substring(0,8));
            string home=Path.Combine(shortRoot,"codex"),work=Path.Combine(shortRoot,"work"),id="99999999-9999-4999-8999-999999999999";
            string rollout=Path.Combine(home,"sessions","2026","09","17","rollout-2026-09-17T00-00-00-"+id+".jsonl");bool passed=false;
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(rollout));Directory.CreateDirectory(work);
                string cwd=work.Replace("\\","\\\\");
                File.WriteAllText(rollout,"{\"timestamp\":\"2026-09-17T00:00:00.000Z\",\"type\":\"session_meta\",\"payload\":{\"id\":\""+id+"\",\"timestamp\":\"2026-09-17T00:00:00.000Z\",\"cwd\":\""+cwd+"\",\"originator\":\"codex-cli\",\"cli_version\":\"0.154.0\",\"source\":\"cli\",\"thread_source\":\"user\",\"model_provider\":\"openai\"}}\n",new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(home,"session_index.jsonl"),"{\"id\":\""+id+"\",\"thread_name\":\"fixture\",\"updated_at\":\"2026-09-17T00:00:00.000Z\"}\n",new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(home,"history.jsonl"),"{\"session_id\":\""+id+"\",\"ts\":1789603200,\"text\":\"remove\"}\n{\"session_id\":\"aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa\",\"ts\":1789603201,\"text\":\"keep\"}\n{bad}\n",new UTF8Encoding(false));
                Environment.SetEnvironmentVariable("CODEX_HOME",home);Environment.SetEnvironmentVariable("Path",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","OpenAI","Codex","bin")+";"+previousPath);
                if(SessionHistory.List(work,false).Length!=1)throw new InvalidOperationException("Codex deletion fixture was not listed");
                SessionHistory.Delete("codex",id,work,false);
                if(File.Exists(rollout)||File.ReadAllText(Path.Combine(home,"session_index.jsonl")).IndexOf(id,StringComparison.OrdinalIgnoreCase)>=0||SessionHistory.List(work,false).Length!=0)throw new InvalidOperationException("official Codex delete did not remove fixture session");
                string history=File.ReadAllText(Path.Combine(home,"history.jsonl"));if(history.IndexOf(id,StringComparison.OrdinalIgnoreCase)>=0||history.IndexOf("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",StringComparison.OrdinalIgnoreCase)<0||history.IndexOf("{bad}",StringComparison.Ordinal)<0)throw new InvalidOperationException("Codex history cleanup lost unrelated rows");passed=true;
            } finally { Environment.SetEnvironmentVariable("CODEX_HOME",previousHome);Environment.SetEnvironmentVariable("Path",previousPath);if(passed&&Directory.Exists(shortRoot))try{Directory.Delete(shortRoot,true);}catch{} }
        }
    }
}
