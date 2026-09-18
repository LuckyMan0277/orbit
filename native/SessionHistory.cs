using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Orbit {
    internal sealed class SavedSession {
        internal string Provider, Id, Title, Cwd;
        internal DateTime Updated;
        internal int TitleRank;
    }

    // Reads only the small, local metadata needed to resume a conversation.  It
    // intentionally avoids watching or indexing the agent history directories.
    internal static class SessionHistory {
        const int MaxFiles=80, MaxBytes=256*1024;
        static readonly JavaScriptSerializer Json=new JavaScriptSerializer { MaxJsonLength=MaxBytes };
        static readonly JavaScriptSerializer DeleteJson=new JavaScriptSerializer { MaxJsonLength=16*1024*1024 };
        internal static bool IsId(string value) { Guid ignored; return Guid.TryParseExact(value,"D",out ignored); }
        internal static bool IsCanonicalId(string value) { Guid id; return Guid.TryParseExact(value,"D",out id)&&String.Equals(value,id.ToString("D"),StringComparison.Ordinal); }
        // Deletion deliberately resolves its own metadata immediately before acting.
        // The browser never supplies a path, and an ID cannot be used to select a file.
        internal static void Delete(string provider,string id,string workspace,bool allWorkspaces) {
            if((provider!="codex"&&provider!="claude")||!IsCanonicalId(id))throw new InvalidOperationException("Invalid saved session.");
            SavedSession saved=List(workspace,allWorkspaces).FirstOrDefault(x=>x.Provider==provider&&x.Id==id);
            if(saved==null)throw new InvalidOperationException("Saved session is unavailable.");
            string codex=Home("CODEX_HOME",".codex"),claude=Home("CLAUDE_CONFIG_DIR",".claude");
            if(provider=="codex") { RunCodexDelete(id);using(var history=PrepareJsonFilter(Path.Combine(codex,"history.jsonl"),"session_id",id))history.Commit(); }
            else DeleteClaude(claude,id);
        }
        static string Home(string variable,string fallback) { string value=Environment.GetEnvironmentVariable(variable);return String.IsNullOrEmpty(value)?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),fallback):Path.GetFullPath(value); }
        static void RunCodexDelete(string id) {
            var info=new ProcessStartInfo("codex","delete "+id+" --force") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
            using(var process=Process.Start(info)) {
                if(process==null)throw new InvalidOperationException("Codex CLI could not be started.");
                var output=Task.Factory.StartNew(()=>ReadLimited(process.StandardOutput,8192));var error=Task.Factory.StartNew(()=>ReadLimited(process.StandardError,8192));
                if(!process.WaitForExit(12000)) { try {process.Kill();}catch{} throw new TimeoutException("Codex deletion timed out."); }
                Task.WaitAll(output,error);
                if(process.ExitCode!=0)throw new InvalidOperationException("Codex deletion failed: "+Limit((error.Result+" "+output.Result).Trim()));
            }
        }
        static string ReadLimited(StreamReader reader,int limit) { char[] buffer=new char[1024];var text=new System.Text.StringBuilder();int read;while((read=reader.Read(buffer,0,buffer.Length))>0)if(text.Length<limit)text.Append(buffer,0,Math.Min(read,limit-text.Length));return text.ToString(); }
        static void DeleteClaude(string home,string id) {
            string projects=Path.Combine(home,"projects");
            if(!Directory.Exists(projects))throw new InvalidOperationException("Claude session is unavailable.");
            var files=new List<string>();
            foreach(var project in new DirectoryInfo(projects).GetDirectories()) {
                RejectReparse(project.FullName); string file=Path.Combine(project.FullName,id+".jsonl"); if(File.Exists(file))files.Add(file);
            }
            if(files.Count!=1)throw new InvalidOperationException("Claude session is unavailable.");
            string sessionFile=files[0],projectRoot=Path.GetDirectoryName(sessionFile);
            RejectTree(home);RejectTree(projects);RejectTree(projectRoot);
            var transcripts=new List<string>();foreach(var transcript in new DirectoryInfo(projectRoot).GetFiles("*",SearchOption.TopDirectoryOnly))if(String.Equals(transcript.Name,id+".jsonl",StringComparison.OrdinalIgnoreCase)||(transcript.Name.StartsWith(id+".orphaned-",StringComparison.OrdinalIgnoreCase)&&transcript.Name.EndsWith(".jsonl",StringComparison.OrdinalIgnoreCase))||transcript.Name.StartsWith(id+".jsonl.superseded-",StringComparison.OrdinalIgnoreCase))transcripts.Add(transcript.FullName);
            var directories=new[]{Path.Combine(projectRoot,id),Path.Combine(home,"file-history",id),Path.Combine(home,"image-cache",id),Path.Combine(home,"uploads",id)};
            foreach(string transcript in transcripts){RejectTree(projectRoot);RejectReparse(transcript);}foreach(string directory in directories)if(Directory.Exists(directory)){RejectTree(Path.GetDirectoryName(directory));RejectTree(directory);foreach(var item in new DirectoryInfo(directory).GetFileSystemInfos("*",SearchOption.AllDirectories))RejectReparse(item.FullName);}
            using(var history=PrepareJsonFilter(Path.Combine(home,"history.jsonl"),"sessionId",id))
            using(var index=PrepareIndex(Path.Combine(projectRoot,"sessions-index.json"),id)) {
            foreach(string transcript in transcripts)DeleteFile(transcript,projectRoot);
            // Claude application data documented as session-specific; never touch
            // projects other than the exact UUID file or unrelated memory/settings/worktrees.
            foreach(string directory in directories)DeleteDirectory(directory,Path.GetDirectoryName(directory));
            history.Commit();index.Commit();
            }
        }
        static void DeleteFile(string path,string root) { string full=Inside(path,root); if(File.Exists(full)) { RejectReparse(full);File.Delete(full); } }
        static void DeleteDirectory(string path,string root) { string full=Inside(path,root);if(!Directory.Exists(full))return;RejectTree(root);RejectTree(full);foreach(var item in new DirectoryInfo(full).GetFileSystemInfos("*",SearchOption.AllDirectories))RejectReparse(item.FullName);Directory.Delete(full,true); }
        static string Inside(string path,string root) { string full=Path.GetFullPath(path),basePath=Path.GetFullPath(root).TrimEnd('\\')+"\\";if(!full.StartsWith(basePath,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Unsafe history path.");return full; }
        static void RejectReparse(string path) { if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Refusing reparse-point history path."); }
        static void RejectTree(string path) { string full=Path.GetFullPath(path);for(DirectoryInfo part=new DirectoryInfo(full);part!=null;part=part.Parent) { if(part.Exists)RejectReparse(part.FullName); } }
        sealed class PendingRewrite:IDisposable {
            readonly string path,replacement;readonly FileStream held;public PendingRewrite(string value,string next,FileStream lockHandle){path=value;replacement=next;held=lockHandle;}
            internal void Commit(){if(replacement==null)return;string temp=path+".orbit-delete-"+Guid.NewGuid().ToString("N")+".tmp";try {File.WriteAllText(temp,replacement,new System.Text.UTF8Encoding(false));File.Replace(temp,path,null);} finally {if(File.Exists(temp))try{File.Delete(temp);}catch{}}}
            public void Dispose(){if(held!=null)held.Dispose();}
        }
        // Keep the source handle open with writers denied through File.Replace.
        // A busy history aborts before the transcript is removed.
        static PendingRewrite PrepareJsonFilter(string path,string key,string id) {
            if(!File.Exists(path))return new PendingRewrite(path,null,null);RejectReparse(path);RejectTree(Path.GetDirectoryName(path));
            FileStream stream;try{stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);}catch(IOException){throw new IOException("History is busy; deletion was not started.");}
            try {string source;using(var reader=new StreamReader(stream,System.Text.Encoding.UTF8,true,4096,true))source=reader.ReadToEnd();string newline=source.Contains("\r\n")?"\r\n":"\n";bool ended=source.EndsWith("\n");var kept=new List<string>();foreach(string line in source.Replace("\r\n","\n").Split(new[]{'\n'})){Dictionary<string,object> row=null;try{row=DeleteJson.Deserialize<Dictionary<string,object>>(line);}catch{}if(row==null||!String.Equals(Text(row,key),id,StringComparison.Ordinal))kept.Add(line);}if(ended&&kept.Count>0&&kept[kept.Count-1]=="")kept.RemoveAt(kept.Count-1);return new PendingRewrite(path,String.Join(newline,kept)+(ended?newline:""),stream);}catch{stream.Dispose();throw;}
        }
        static PendingRewrite PrepareIndex(string path,string id) {
            if(!File.Exists(path))return new PendingRewrite(path,null,null);RejectReparse(path);RejectTree(Path.GetDirectoryName(path));FileStream stream;try{stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);}catch(IOException){throw new IOException("History is busy; deletion was not started.");}
            try {Dictionary<string,object> map;using(var reader=new StreamReader(stream,System.Text.Encoding.UTF8,true,4096,true))map=DeleteJson.Deserialize<Dictionary<string,object>>(reader.ReadToEnd());if(map==null)throw new InvalidOperationException("Malformed Claude sessions index.");object value;var entries=map.TryGetValue("entries",out value)?value as System.Collections.IList:null;if(entries==null)throw new InvalidOperationException("Malformed Claude sessions index.");var kept=new List<object>();foreach(object entry in entries){var row=entry as Dictionary<string,object>;if(row==null||!String.Equals(Text(row,"sessionId"),id,StringComparison.Ordinal))kept.Add(entry);}map["entries"]=kept.ToArray();return new PendingRewrite(path,DeleteJson.Serialize(map),stream);}catch{stream.Dispose();throw;}
        }
        internal static SavedSession[] List(string workspace,bool allWorkspaces) {
            var result=new List<SavedSession>();
            string codex=Environment.GetEnvironmentVariable("CODEX_HOME");
            if(String.IsNullOrEmpty(codex)) codex=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex");
            string claude=Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            if(String.IsNullOrEmpty(claude)) claude=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".claude");
            try { result.AddRange(ReadCodex(codex,workspace,allWorkspaces)); } catch { }
            try { result.AddRange(ReadClaude(claude,workspace,allWorkspaces)); } catch { }
            return result.OrderByDescending(x=>x.Updated).Take(MaxFiles*2).ToArray();
        }
        static IEnumerable<SavedSession> ReadCodex(string home,string workspace,bool all) {
            var titles=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            string index=Path.Combine(home,"session_index.jsonl");
            foreach(var row in Lines(index,true)) { string id=Text(row,"id"); string title=Title(row); if(IsId(id)&&!String.IsNullOrWhiteSpace(title)) titles[id]=title; }
            var sessions=new Dictionary<string,SavedSession>(StringComparer.OrdinalIgnoreCase);
            string root=Path.Combine(home,"sessions");
            foreach(var file in CodexFiles(root)) foreach(var row in Lines(file.FullName,false)) {
                if(Text(row,"type")!="session_meta") continue;
                var payload=Map(row,"payload"); string id=Text(payload,"id"),cwd=Text(payload,"cwd");
                if(!IsId(id)||!UserCodexSession(payload)||!Allowed(cwd,workspace,all)) continue;
                SavedSession item; if(!sessions.TryGetValue(id,out item)) { item=new SavedSession { Provider="codex",Id=id,Cwd=cwd,Updated=file.LastWriteTimeUtc,Title="" }; sessions[id]=item; }
                string title=Title(payload); int rank=TitleRank(payload); if(!String.IsNullOrWhiteSpace(title)&&rank>=item.TitleRank) { item.Title=title;item.TitleRank=rank; }
            }
            foreach(var item in sessions.Values) { string title; if(titles.TryGetValue(item.Id,out title)&&!String.IsNullOrWhiteSpace(title)) item.Title=title; if(String.IsNullOrWhiteSpace(item.Title)) item.Title="Codex "+item.Id.Substring(0,8); }
            return sessions.Values;
        }
        static IEnumerable<SavedSession> ReadClaude(string home,string workspace,bool all) {
            var sessions=new Dictionary<string,SavedSession>(StringComparer.OrdinalIgnoreCase);
            foreach(var file in ClaudeFiles(Path.Combine(home,"projects"))) foreach(var row in Lines(file.FullName,false)) {
                // Claude records have both a message id and a sessionId: never use
                // the message id for --resume.
                string id=Text(row,"sessionId"),cwd=Text(row,"cwd"); if(!IsId(id)||Text(row,"isSidechain").Equals("True",StringComparison.OrdinalIgnoreCase)) continue;
                SavedSession item; if(!sessions.TryGetValue(id,out item)) { if(!Allowed(cwd,workspace,all)) continue; item=new SavedSession { Provider="claude",Id=id,Cwd=cwd,Updated=file.LastWriteTimeUtc,Title="" }; sessions[id]=item; }
                string title=Title(row); int rank=TitleRank(row); if(!String.IsNullOrWhiteSpace(title)&&(rank>item.TitleRank || (rank>1&&rank==item.TitleRank))) { item.Title=title;item.TitleRank=rank; }
            }
            foreach(var item in sessions.Values) if(String.IsNullOrWhiteSpace(item.Title)) item.Title="Claude "+item.Id.Substring(0,8);
            return sessions.Values;
        }
        static IEnumerable<FileInfo> CodexFiles(string root) { return DatedFiles(root); }
        static IEnumerable<FileInfo> ClaudeFiles(string root) {
            if(!Directory.Exists(root)) return new FileInfo[0]; try { return new DirectoryInfo(root).GetDirectories().OrderByDescending(x=>x.LastWriteTimeUtc).SelectMany(x=>x.GetFiles("*.jsonl",SearchOption.TopDirectoryOnly)).OrderByDescending(x=>x.LastWriteTimeUtc).Take(MaxFiles).ToArray(); } catch { return new FileInfo[0]; }
        }
        static IEnumerable<FileInfo> DatedFiles(string root) {
            if(!Directory.Exists(root)) return new FileInfo[0]; var files=new List<FileInfo>(); try { foreach(var year in new DirectoryInfo(root).GetDirectories().OrderByDescending(x=>x.Name)) foreach(var month in year.GetDirectories().OrderByDescending(x=>x.Name)) foreach(var day in month.GetDirectories().OrderByDescending(x=>x.Name)) { files.AddRange(day.GetFiles("*.jsonl",SearchOption.TopDirectoryOnly)); if(files.Count>=MaxFiles) return files.OrderByDescending(x=>x.LastWriteTimeUtc).Take(MaxFiles).ToArray(); } } catch { } return files.OrderByDescending(x=>x.LastWriteTimeUtc).Take(MaxFiles).ToArray();
        }
        static IEnumerable<Dictionary<string,object>> Lines(string path,bool tail) {
            if(!File.Exists(path)) yield break; byte[] bytes;
            try { using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)) { long start=tail?Math.Max(0,stream.Length-MaxBytes):0;stream.Position=start;int wanted=(int)Math.Min(MaxBytes,stream.Length-start);bytes=new byte[wanted];int read=0,n;while(read<wanted&&(n=stream.Read(bytes,read,wanted-read))>0)read+=n;if(read!=bytes.Length)Array.Resize(ref bytes,read); } } catch { yield break; }
            string text=System.Text.Encoding.UTF8.GetString(bytes); foreach(string line in text.Split(new[]{'\n'})) { Dictionary<string,object> row=null; try { row=Json.Deserialize<Dictionary<string,object>>(line); } catch { } if(row!=null) yield return row; }
        }
        static Dictionary<string,object> Map(Dictionary<string,object> row,string key) { object value; return row!=null&&row.TryGetValue(key,out value) ? value as Dictionary<string,object> : null; }
        static string Text(Dictionary<string,object> row,string key) { object value; return row!=null&&row.TryGetValue(key,out value)&&value!=null ? Convert.ToString(value) : ""; }
        static bool UserCodexSession(Dictionary<string,object> payload) {
            string source=Text(payload,"thread_source");
            if(source.Length>0&&!source.Equals("user",StringComparison.OrdinalIgnoreCase)) return false;
            var detail=Map(payload,"source");
            return detail==null||!detail.ContainsKey("subagent");
        }
        static string Title(Dictionary<string,object> row) {
            foreach(string key in new[]{"customTitle","custom_title","custom-title","summary","thread_name","title"}) { string value=Text(row,key).Trim(); if(value.Length>0) return Limit(value); }
            string user=UserText(row); return user.Length>0 ? Limit(user) : "";
        }
        static int TitleRank(Dictionary<string,object> row) { if(!String.IsNullOrWhiteSpace(Text(row,"customTitle"))||!String.IsNullOrWhiteSpace(Text(row,"custom_title"))||!String.IsNullOrWhiteSpace(Text(row,"custom-title"))) return 4; if(!String.IsNullOrWhiteSpace(Text(row,"summary"))) return 3; if(!String.IsNullOrWhiteSpace(Text(row,"thread_name"))||!String.IsNullOrWhiteSpace(Text(row,"title"))) return 2; return UserText(row).Length>0?1:0; }
        static string UserText(Dictionary<string,object> row) {
            string role=Text(row,"role"); var message=Map(row,"message"); if(role!="user"&&Text(message,"role")!="user"&&Text(row,"type")!="user") return "";
            string text=Text(row,"text"); if(text.Length==0) text=Text(row,"content"); if(text.Length==0) text=Text(message,"content"); if(text.Length==0) text=BlockText(row,"content"); if(text.Length==0) text=BlockText(message,"content"); return text.Trim();
        }
        static string BlockText(Dictionary<string,object> row,string key) { object value; if(row==null||!row.TryGetValue(key,out value)) return ""; var list=value as System.Collections.IEnumerable; if(list==null||value is string) return ""; foreach(var part in list) { var map=part as Dictionary<string,object>;string text=Text(map,"text");if(text.Length>0)return text; } return ""; }
        static string Limit(string value) { return value.Replace('\r',' ').Replace('\n',' ').Trim().Substring(0,Math.Min(value.Trim().Length,120)); }
        static bool Allowed(string cwd,string workspace,bool all) { if(String.IsNullOrWhiteSpace(cwd)||!Directory.Exists(cwd)) return false; if(all) return true; try { return String.Equals(Path.GetFullPath(cwd).TrimEnd('\\'),Path.GetFullPath(workspace).TrimEnd('\\'),StringComparison.OrdinalIgnoreCase); } catch { return false; } }
    }
}
