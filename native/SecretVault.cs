using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
namespace Orbit {
 // What the remote API and the desktop bridge may do with secrets. Kept apart from IRemoteTerminals so a host without a vault still works.
 // project is a folder path: keys are stored for "all projects" or for one project, and a terminal sees both (its project wins on a clash).
 // In Create, secrets == null means "the default for this profile", an array is taken literally.
 internal interface IRemoteSecrets { object SecretNames(string project); object SetSecret(string name,string value,string scope,string project); object DeleteSecret(string name,string scope,string project); object Create(string profile,string resumeId,string project,string[] secrets); object PendingSecretRequests(string session); bool AnswerSecretRequest(string request,string status); }
 // API keys and tokens that must not appear in an AI conversation. Values are encrypted with the Windows user's DPAPI key and only
 // leave this class as environment variables of a new terminal process; every UI and API gets names, never values.
 internal sealed class SecretVault {
  const string Entropy="Orbit.Secrets.v1";
  public const int MaxCount=100,MaxValue=8192,MaxPerTerminal=16;
  static readonly Regex NamePattern=new Regex("^[A-Za-z_][A-Za-z0-9_]{0,63}$",RegexOptions.Compiled);
  // Variables the shell and Windows depend on: a secret must not be able to replace them.
  static readonly HashSet<string> Reserved=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"PATH","PATHEXT","COMSPEC","SYSTEMROOT","SYSTEMDRIVE","WINDIR","PSMODULEPATH","USERPROFILE","APPDATA","LOCALAPPDATA","TEMP","TMP","TERM","COLORTERM"};
  readonly string file; readonly object gate=new object(); bool loaded;
  Dictionary<string,string> global; Dictionary<string,Dictionary<string,string>> projects;
  public SecretVault(string path){file=path;}
  // "All projects" keys plus the keys of the project that contains this folder, as (name, scope) pairs.
  public object[] List(string project){
   lock(gate){
    Load();var list=new List<KeyValuePair<string,string>>();
    foreach(string n in global.Keys)list.Add(new KeyValuePair<string,string>(n,"global"));
    var own=Match(project);if(own!=null)foreach(string n in own.Keys)list.Add(new KeyValuePair<string,string>(n,"project"));
    return list.OrderBy(x=>x.Key,StringComparer.OrdinalIgnoreCase).ThenBy(x=>x.Value).Select(x=>(object)new {name=x.Key,scope=x.Value}).ToArray();
   }
  }
  public string[] Names(string project){lock(gate){return Merged(project).Keys.OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray();}}
  public bool Has(string name,string project){lock(gate){return Merged(project).ContainsKey((name??"").Trim());}}
  public void Set(string name,string value,string scope,string project){
   name=CheckName(name);value=(value??"").Trim();
   if(value.Length==0)throw new InvalidOperationException("값을 입력해 주세요.");
   if(value.Length>MaxValue||value.IndexOf('\0')>=0)throw new InvalidOperationException("값이 너무 길거나 사용할 수 없는 문자가 있습니다.");
   lock(gate){
    Load();string key=null;Dictionary<string,string> map;bool created=false;
    if(scope=="project"){
     key=NormalizeProject(project);if(key==null)throw new InvalidOperationException("프로젝트 폴더를 확인할 수 없습니다.");
     if(!projects.TryGetValue(key,out map)){map=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);projects[key]=map;created=true;}
    } else if(String.IsNullOrEmpty(scope)||scope=="global")map=global;
    else throw new InvalidOperationException("알 수 없는 범위입니다.");
    string previous;bool had=map.TryGetValue(name,out previous);
    if(!had&&Total()>=MaxCount){if(created)projects.Remove(key);throw new InvalidOperationException("비밀 값은 "+MaxCount+"개까지 저장할 수 있습니다.");}
    map[name]=value;
    if(!Save()){if(had)map[name]=previous;else map.Remove(name);if(created)projects.Remove(key);throw new IOException("비밀 값을 저장하지 못했습니다.");}
   }
  }
  public bool Delete(string name,string scope,string project){
   name=CheckName(name);
   lock(gate){
    Load();string key=null;Dictionary<string,string> map;
    if(scope=="project"){key=NormalizeProject(project);if(key==null||!projects.TryGetValue(key,out map))return false;}
    else map=global;
    string previous;if(!map.TryGetValue(name,out previous))return false;
    map.Remove(name);bool dropped=false;if(key!=null&&map.Count==0){projects.Remove(key);dropped=true;}
    if(!Save()){map[name]=previous;if(dropped)projects[key]=map;throw new IOException("비밀 값을 삭제하지 못했습니다.");}
    return true;
   }
  }
  // Name -> value for a terminal working in this folder. names == null means everything it may see; an unknown name fails loudly,
  // because a terminal silently missing its key is worse than none.
  public Dictionary<string,string> Env(string project,IEnumerable<string> names){
   Dictionary<string,string> merged;lock(gate)merged=Merged(project);
   if(names==null)return merged;
   var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
   foreach(string raw in names){
    string name=CheckName(raw),value;
    if(!merged.TryGetValue(name,out value))throw new InvalidOperationException("등록되지 않은 비밀 값입니다: "+name);
    result[name]=value;
   }
   if(result.Count>MaxPerTerminal)throw new InvalidOperationException("터미널 하나에는 비밀 값을 "+MaxPerTerminal+"개까지 넣을 수 있습니다.");
   return result;
  }
  internal static string CheckName(string name){
   name=(name??"").Trim();
   if(!NamePattern.IsMatch(name))throw new InvalidOperationException("이름은 영문·숫자·밑줄만 쓸 수 있고 숫자로 시작할 수 없습니다. (예: OPENAI_API_KEY)");
   if(Reserved.Contains(name)||name.StartsWith("ORBIT_",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("시스템이 쓰는 이름이라 사용할 수 없습니다: "+name);
   return name;
  }
  // A folder key: full path, no trailing slash, lower case. Terminals started in a sub-folder of a project belong to that project.
  internal static string NormalizeProject(string path){
   path=(path??"").Trim();if(path.Length==0||path.Length>1024)return null;
   try{return Path.GetFullPath(path).TrimEnd('\\','/').ToLowerInvariant();}catch{return null;}
  }
  int Total(){return global.Count+projects.Values.Sum(x=>x.Count);}
  Dictionary<string,string> Merged(string project){
   lock(gate){
    Load();var merged=new Dictionary<string,string>(global,StringComparer.OrdinalIgnoreCase);
    var own=Match(project);if(own!=null)foreach(var pair in own)merged[pair.Key]=pair.Value;
    return merged;
   }
  }
  // The stored project whose folder is this one or the nearest parent of it.
  Dictionary<string,string> Match(string project){
   string key=NormalizeProject(project);if(key==null)return null;string best=null;
   foreach(string candidate in projects.Keys)if((key==candidate||key.StartsWith(candidate+"\\",StringComparison.Ordinal))&&(best==null||candidate.Length>best.Length))best=candidate;
   return best==null?null:projects[best];
  }
  void Load(){
   if(loaded)return;loaded=true;
   global=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);projects=new Dictionary<string,Dictionary<string,string>>(StringComparer.OrdinalIgnoreCase);
   try{
    if(!File.Exists(file))return;
    byte[] plain=ProtectedData.Unprotect(File.ReadAllBytes(file),Encoding.UTF8.GetBytes(Entropy),DataProtectionScope.CurrentUser);
    var root=new JavaScriptSerializer{MaxJsonLength=4*1024*1024}.Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(plain));
    if(root==null)return;
    var g=root.ContainsKey("global")?root["global"] as IDictionary:null;var p=root.ContainsKey("projects")?root["projects"] as IDictionary:null;
    if(g==null&&p==null){foreach(var pair in root)if(pair.Value is string)global[pair.Key]=(string)pair.Value;return;} // the first version stored one flat list: every key was for all projects
    if(g!=null)foreach(DictionaryEntry e in g)global[Convert.ToString(e.Key)]=Convert.ToString(e.Value);
    if(p!=null)foreach(DictionaryEntry e in p){
     var inner=e.Value as IDictionary;if(inner==null)continue;
     var map=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);foreach(DictionaryEntry i in inner)map[Convert.ToString(i.Key)]=Convert.ToString(i.Value);
     if(map.Count>0)projects[Convert.ToString(e.Key)]=map;
    }
   }catch{}
  }
  bool Save(){
   try{
    string text=new JavaScriptSerializer{MaxJsonLength=4*1024*1024}.Serialize(new Dictionary<string,object>{{"global",global},{"projects",projects}});
    byte[] data=ProtectedData.Protect(Encoding.UTF8.GetBytes(text),Encoding.UTF8.GetBytes(Entropy),DataProtectionScope.CurrentUser);
    Directory.CreateDirectory(Path.GetDirectoryName(file));
    string tmp=file+".tmp";File.WriteAllBytes(tmp,data);
    if(File.Exists(file))File.Replace(tmp,file,null);else File.Move(tmp,file);
    return true;
   }catch{return false;}
  }
 }
}
