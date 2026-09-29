using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
namespace Orbit {
 // Follows a project folder after it is renamed or moved. A path is remembered together with the folder's NTFS file ID
 // ("volume serial:file index"), which survives renames and moves on the same drive; when the path is gone the ID is opened
 // directly and its current path read back. Nothing watches the disk: this only runs when the UI asks.
 internal static class ProjectLocator {
  [StructLayout(LayoutKind.Sequential)] struct FileInfo { public uint Attributes,C1,C2,A1,A2,W1,W2,Serial,SizeHigh,SizeLow,Links,IndexHigh,IndexLow; }
  [StructLayout(LayoutKind.Explicit,Size=24)] struct FileIdDescriptor { [FieldOffset(0)] public int Size; [FieldOffset(4)] public int Type; [FieldOffset(8)] public long FileId; }
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern SafeFileHandle CreateFile(string name,uint access,uint share,IntPtr security,uint mode,uint flags,IntPtr template);
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetFileInformationByHandle(SafeFileHandle file,out FileInfo info);
  [DllImport("kernel32.dll",SetLastError=true)] static extern SafeFileHandle OpenFileById(SafeFileHandle volumeHint,ref FileIdDescriptor id,uint access,uint share,IntPtr security,uint flags);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern uint GetFinalPathNameByHandle(SafeFileHandle file,StringBuilder path,uint length,uint flags);
  const uint ReadAttributes=0x80,ShareAll=7,OpenExisting=3,BackupSemantics=0x02000000;

  // items: {path, id}. Returns each with its current path and ID; found=false when neither the path nor the ID leads to a folder.
  public static object[] Resolve(IEnumerable<KeyValuePair<string,string>> items){
   var result=new List<object>();
   foreach(var item in items){
    string path=item.Key??"",id=item.Value??"",now=null;
    if(path.Length>0&&Directory.Exists(path)){
     // A new folder made under the old name after a move is not the project; the moved one wins if it can still be found.
     string there=id.Length>0?Id(path):null;
     now=there==null||there==id?path:(Locate(id)??path);
    }
    else if(id.Length>0)now=Locate(id);
    string current=now!=null?Id(now):null;
    result.Add(new {path=path,current=now??path,id=current??id,found=now!=null,moved=now!=null&&!String.Equals(now,path,StringComparison.OrdinalIgnoreCase)});
   }
   return result.ToArray();
  }
  internal static string Id(string folder){
   try{
    using(var h=Open(folder)){FileInfo info;if(h.IsInvalid||!GetFileInformationByHandle(h,out info))return null;return info.Serial.ToString("x8")+":"+(((ulong)info.IndexHigh<<32)|info.IndexLow).ToString("x16");}
   }catch{return null;}
  }
  internal static string Locate(string id){
   try{
    string[] parts=id.Split(':');uint serial;ulong index;
    if(parts.Length!=2||!UInt32.TryParse(parts[0],System.Globalization.NumberStyles.HexNumber,null,out serial)||!UInt64.TryParse(parts[1],System.Globalization.NumberStyles.HexNumber,null,out index))return null;
    foreach(var drive in DriveInfo.GetDrives()){
     if(drive.DriveType!=DriveType.Fixed&&drive.DriveType!=DriveType.Removable)continue;
     using(var volume=Open(drive.RootDirectory.FullName)){
      FileInfo info;if(volume.IsInvalid||!GetFileInformationByHandle(volume,out info)||info.Serial!=serial)continue;
      var descriptor=new FileIdDescriptor{Size=24,Type=0,FileId=unchecked((long)index)};
      using(var h=OpenFileById(volume,ref descriptor,ReadAttributes,ShareAll,IntPtr.Zero,BackupSemantics)){
       if(h.IsInvalid)return null;
       var text=new StringBuilder(1024);uint length=GetFinalPathNameByHandle(h,text,(uint)text.Capacity,0);
       if(length==0||length>=text.Capacity)return null;
       string path=text.ToString();
       if(path.StartsWith(@"\\?\UNC\",StringComparison.Ordinal))path=@"\\"+path.Substring(8);else if(path.StartsWith(@"\\?\",StringComparison.Ordinal))path=path.Substring(4);
       // A folder sent to the Recycle Bin keeps its ID; that is a deleted project, not a moved one.
       if(path.IndexOf(@"\$Recycle.Bin\",StringComparison.OrdinalIgnoreCase)>=0||!Directory.Exists(path))return null;
       return path.TrimEnd('\\').Length<3?path:path.TrimEnd('\\');
      }
     }
    }
   }catch{}
   return null;
  }
  static SafeFileHandle Open(string folder){return CreateFile(folder,ReadAttributes,ShareAll,IntPtr.Zero,OpenExisting,BackupSemantics,IntPtr.Zero);}
 }
}
