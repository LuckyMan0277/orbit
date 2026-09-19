using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Orbit {
 // Checks GitHub Releases for a newer Orbit-Setup.exe and runs it. Only the release
 // found by the last Check() can be installed, and only from this repository's
 // release downloads, so the UI cannot point the updater at an arbitrary URL.
 internal static class Updater {
  const string LatestApi="https://api.github.com/repos/LuckyMan0277/orbit/releases/latest",DownloadPrefix="https://github.com/LuckyMan0277/orbit/releases/download/",AssetName="Orbit-Setup.exe";
  const long MaxBytes=500L*1024*1024;
  internal sealed class Release{public string Tag,Notes,Url,Sha256;public Version Version;}
  static Release pending;static readonly object gate=new object();
  public static Version Current(){return Assembly.GetExecutingAssembly().GetName().Version;}
  public static string CurrentText(){var v=Current();return v.Major+"."+v.Minor+"."+Math.Max(v.Build,0);}
  internal static Version ParseTag(string tag){if(String.IsNullOrEmpty(tag))return null;tag=tag.Trim();if(tag.StartsWith("v",StringComparison.OrdinalIgnoreCase))tag=tag.Substring(1);if(!Regex.IsMatch(tag,@"^\d+\.\d+\.\d+$"))return null;Version v;return Version.TryParse(tag,out v)?v:null;}
  internal static bool IsNewer(Version latest,Version current){if(latest==null||current==null)return false;return new Version(latest.Major,latest.Minor,Math.Max(latest.Build,0))>new Version(current.Major,current.Minor,Math.Max(current.Build,0));}
  static string S(Dictionary<string,object> d,string k){object v;return d!=null&&d.TryGetValue(k,out v)&&v!=null?Convert.ToString(v):"";}
  internal static Release Parse(string json) {
   var root=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(json);if(root==null)return null;
   object flag;if((root.TryGetValue("draft",out flag)&&flag is bool&&(bool)flag)||(root.TryGetValue("prerelease",out flag)&&flag is bool&&(bool)flag))return null;
   var version=ParseTag(S(root,"tag_name"));if(version==null)return null;
   string notes=S(root,"body").Trim();if(notes.Length>1000)notes=notes.Substring(0,1000);
   var release=new Release{Tag=S(root,"tag_name"),Version=version,Notes=notes};
   object assets;if(root.TryGetValue("assets",out assets)&&assets is IEnumerable&&!(assets is string)) {
    foreach(object item in (IEnumerable)assets) {
     var asset=item as Dictionary<string,object>;if(asset==null||S(asset,"name")!=AssetName)continue;
     release.Url=S(asset,"browser_download_url");string digest=S(asset,"digest");
     if(digest.StartsWith("sha256:",StringComparison.OrdinalIgnoreCase))release.Sha256=digest.Substring(7).ToLowerInvariant();
    }
   }
   if(String.IsNullOrEmpty(release.Url)||!release.Url.StartsWith(DownloadPrefix,StringComparison.Ordinal))return null;
   return release;
  }
  static HttpWebRequest Request(string url,int timeout) {
   ServicePointManager.SecurityProtocol|=(SecurityProtocolType)3072;
   var request=(HttpWebRequest)WebRequest.Create(url);request.UserAgent="Orbit-Updater";request.Accept="application/vnd.github+json";request.Timeout=timeout;request.ReadWriteTimeout=timeout;return request;
  }
  public static object Check() {
   string current=CurrentText();
   try {
    string json;using(var response=(HttpWebResponse)Request(LatestApi,8000).GetResponse())using(var reader=new StreamReader(response.GetResponseStream()))json=reader.ReadToEnd();
    var release=Parse(json);
    if(release==null||!IsNewer(release.Version,Current()))return new {available=false,current=current};
    lock(gate)pending=release;
    return new {available=true,current=current,version=release.Version.ToString(3),notes=release.Notes};
   } catch(Exception ex) { return new {available=false,current=current,error=ex.Message}; }
  }
  // Downloads the release found by Check(), verifies its digest when GitHub provides
  // one, and starts the installer. The caller must exit the app right afterwards so
  // the installer can replace the files.
  public static void DownloadAndLaunch() {
   Release release;lock(gate)release=pending;
   if(release==null)throw new InvalidOperationException("업데이트 정보가 없습니다. 다시 확인해 주세요.");
   string path=Path.Combine(Path.GetTempPath(),"Orbit-Setup-"+release.Version.ToString(3)+".exe"),part=path+".part";
   try {
    long total=0;
    using(var response=(HttpWebResponse)Request(release.Url,30000).GetResponse())using(var input=response.GetResponseStream())using(var output=File.Create(part)) {
     var buffer=new byte[81920];int read;
     while((read=input.Read(buffer,0,buffer.Length))>0){total+=read;if(total>MaxBytes)throw new InvalidOperationException("설치 파일이 예상보다 큽니다.");output.Write(buffer,0,read);}
    }
    if(!String.IsNullOrEmpty(release.Sha256)) {
     string actual;using(var stream=File.OpenRead(part))using(var sha=SHA256.Create())actual=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
     if(actual!=release.Sha256)throw new InvalidOperationException("내려받은 설치 파일의 체크섬이 맞지 않습니다.");
    }
    if(File.Exists(path))File.Delete(path);
    File.Move(part,path);
   } finally { try{if(File.Exists(part))File.Delete(part);}catch{} }
   Process.Start(new ProcessStartInfo(path,"/SILENT /NOCANCEL /CLOSEAPPLICATIONS"){UseShellExecute=false});
  }
 }
}
