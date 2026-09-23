using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Orbit {
    internal static class Files {
        public const int MaxBytes=4*1024*1024;
        private static readonly ConcurrentDictionary<string,object> SaveLocks=new ConcurrentDictionary<string,object>(StringComparer.OrdinalIgnoreCase);
        public static string FullPath(string path,string cwd) { return Path.GetFullPath(Path.IsPathRooted(path)?path:Path.Combine(cwd,path)); }
        public static object List(string path,bool hidden) {
            var entries=new DirectoryInfo(path).EnumerateFileSystemInfos().Where(x=>hidden || ((x.Attributes&FileAttributes.Hidden)==0 && x.Name!=".git" && x.Name!="node_modules" && x.Name!=".tools"));
            var result=entries.Take(1501).Select(x=>new { name=x.Name,path=x.FullName,directory=(x.Attributes&FileAttributes.Directory)!=0 }).OrderByDescending(x=>x.directory).ThenBy(x=>x.name,StringComparer.OrdinalIgnoreCase).ToArray();
            return new { entries=result.Take(1500).ToArray(),truncated=result.Length>1500 };
        }
        public static object Browse(string path,bool hidden,bool directoriesOnly) {
            string full=Path.GetFullPath(path);
            if(!Directory.Exists(full)) throw new DirectoryNotFoundException("폴더를 찾을 수 없습니다.");
            var entries=new DirectoryInfo(full).EnumerateFileSystemInfos()
                .Where(x=>hidden || ((x.Attributes&FileAttributes.Hidden)==0 && x.Name!=".git" && x.Name!="node_modules" && x.Name!=".tools"))
                .Where(x=>!directoriesOnly || (x.Attributes&FileAttributes.Directory)!=0)
                .Take(1501)
                .Select(x=>new { name=x.Name,path=x.FullName,directory=(x.Attributes&FileAttributes.Directory)!=0 })
                .OrderByDescending(x=>x.directory).ThenBy(x=>x.name,StringComparer.OrdinalIgnoreCase).ToArray();
            var parentInfo=Directory.GetParent(full);
            string parent=parentInfo==null?null:parentInfo.FullName;
            return new { path=full,parent=parent,entries=entries.Take(1500).ToArray(),truncated=entries.Length>1500 };
        }
        public static object PickerPlaces() {
            string home=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return new {
                home=home,
                desktop=Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                documents=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                downloads=Path.Combine(home,"Downloads"),
                drives=Environment.GetLogicalDrives().Select(x=>new { name=x,path=x }).ToArray()
            };
        }
        public static object CreateFile(string path) {
            string full=Path.GetFullPath(path);
            string parent=Path.GetDirectoryName(full);
            if(string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent)) throw new DirectoryNotFoundException("대상 폴더를 찾을 수 없습니다.");
            string name=Path.GetFileName(full);
            if(string.IsNullOrWhiteSpace(name) || name=="." || name==".." || name.IndexOfAny(Path.GetInvalidFileNameChars())>=0) throw new InvalidOperationException("유효한 새 파일 이름을 입력해 주세요.");
            using(var stream=new FileStream(full,FileMode.CreateNew,FileAccess.Write,FileShare.None)) { }
            return new { path=full };
        }
        // Drag and drop in the file tree: moves a file or folder into another folder, never over an existing entry.
        public static object Move(string path,string folder) {
            string source=Path.GetFullPath(path).TrimEnd('\\'),target=Path.GetFullPath(folder).TrimEnd('\\');
            bool directory=Directory.Exists(source);
            if(!directory && !File.Exists(source)) throw new FileNotFoundException("옮길 항목을 찾을 수 없습니다.");
            if(!Directory.Exists(target)) throw new DirectoryNotFoundException("대상 폴더를 찾을 수 없습니다.");
            if(String.Equals(Path.GetDirectoryName(source),target,StringComparison.OrdinalIgnoreCase)) return new { path=source };
            if(directory && (String.Equals(source,target,StringComparison.OrdinalIgnoreCase) || target.StartsWith(source+"\\",StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("폴더를 자기 안으로 옮길 수 없습니다.");
            string destination=Path.Combine(target,Path.GetFileName(source));
            if(File.Exists(destination) || Directory.Exists(destination)) throw new IOException("대상 폴더에 같은 이름의 항목이 있습니다.");
            if(directory) Directory.Move(source,destination); else File.Move(source,destination);
            return new { path=destination };
        }
        internal static string Hash(byte[] bytes) { using(var sha=SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(bytes)); }
        internal static Encoding Decode(byte[] bytes,out int offset,out string label) {
            offset=0; label="UTF-8";
            if(bytes.Length>=3 && bytes[0]==239 && bytes[1]==187 && bytes[2]==191) { offset=3;label="UTF-8 BOM";return new UTF8Encoding(true,true); }
            if(bytes.Length>=2 && bytes[0]==255 && bytes[1]==254) { offset=2;label="UTF-16 LE";return new UnicodeEncoding(false,true,true); }
            if(bytes.Length>=2 && bytes[0]==254 && bytes[1]==255) { offset=2;label="UTF-16 BE";return new UnicodeEncoding(true,true,true); }
            if(bytes.Take(8192).Any(x=>x==0)) throw new InvalidOperationException("바이너리 파일은 기본 앱에서 열어 주세요.");
            var utf8=new UTF8Encoding(false,true);
            try { utf8.GetString(bytes);return utf8; }
            catch(DecoderFallbackException) { label="CP949";return Encoding.GetEncoding(949,EncoderFallback.ExceptionFallback,DecoderFallback.ExceptionFallback); }
        }
        public static object Read(string path) {
            var info=new FileInfo(path);
            if(info.Length>MaxBytes) throw new InvalidOperationException("가벼운 편집을 위해 4MB 이하의 텍스트 파일만 열 수 있습니다. 기본 앱에서 열어 주세요.");
            byte[] bytes;
            using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)) {
                if(file.Length>MaxBytes) throw new InvalidOperationException("파일이 4MB를 초과합니다.");
                bytes=new byte[(int)file.Length]; int n=0,read; while(n<bytes.Length && (read=file.Read(bytes,n,bytes.Length-n))>0)n+=read;
                if(n!=bytes.Length) throw new IOException("파일이 변경되었습니다. 다시 열어 주세요.");
            }
            int offset;string label;var encoding=Decode(bytes,out offset,out label);
            string content=encoding.GetString(bytes,offset,bytes.Length-offset);
            return new { path=Path.GetFullPath(path),name=Path.GetFileName(path),content=content,encoding=label,revision=Hash(bytes),newline=content.Contains("\r\n")?"CRLF":"LF" };
        }
        public static object Save(string path,string content,string encodingName,string revision) {
            string fullPath=Path.GetFullPath(path);
            lock(SaveLocks.GetOrAdd(fullPath,_=>new object())) return SaveLocked(fullPath,content,encodingName,revision);
        }
        private static object SaveLocked(string path,string content,string encodingName,string revision) {
            Encoding encoding;
            switch(encodingName) {
                case "UTF-8 BOM":encoding=new UTF8Encoding(true,true);break;
                case "UTF-16 LE":encoding=new UnicodeEncoding(false,true,true);break;
                case "UTF-16 BE":encoding=new UnicodeEncoding(true,true,true);break;
                case "CP949":encoding=Encoding.GetEncoding(949,EncoderFallback.ExceptionFallback,DecoderFallback.ExceptionFallback);break;
                default:encoding=new UTF8Encoding(false,true);break;
            }
            byte[] body=encoding.GetBytes(content), bytes=encoding.GetPreamble().Concat(body).ToArray();
            if(bytes.Length>MaxBytes)throw new InvalidOperationException("파일이 4MB를 초과합니다.");
            bool exists=File.Exists(path);
            if(exists && (revision==null || new FileInfo(path).Length>MaxBytes || Hash(File.ReadAllBytes(path))!=revision)) throw new InvalidOperationException("다른 프로그램이 파일을 변경했습니다. 변경 내용을 복사한 뒤 파일을 다시 열어 주세요.");
            if(!exists && revision!=null)throw new InvalidOperationException("파일이 이동 또는 삭제되었습니다. 새 파일로 저장해 주세요.");
            string temp=Path.Combine(Path.GetDirectoryName(path),".orbit-"+Guid.NewGuid().ToString("N")+".tmp");
            try {
                File.WriteAllBytes(temp,bytes);
                // Serialize Orbit saves per path and check again immediately before
                // replacement, narrowing the window for an external agent edit.
                if(exists && (new FileInfo(path).Length>MaxBytes || Hash(File.ReadAllBytes(path))!=revision)) throw new InvalidOperationException("다른 프로그램이 파일을 변경했습니다. 변경 내용을 복사한 뒤 파일을 다시 열어 주세요.");
                if(exists)File.Replace(temp,path,null);else File.Move(temp,path);
            }
            finally { if(File.Exists(temp))File.Delete(temp); }
            return new { revision=Hash(bytes) };
        }
    }
}
