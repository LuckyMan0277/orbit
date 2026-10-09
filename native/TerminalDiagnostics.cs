using System;
using System.IO;
using System.Web.Script.Serialization;

namespace Orbit {
    internal sealed class TerminalResizeTrace {public string Utc,HResult;public int Cols,Rows;}
    internal sealed class TerminalFault {
        public string Session,Operation,ProcessState;
        public int Pid,HResult,Cols,Rows,AppliedCols,AppliedRows;
        public bool InputDisconnected;
        public string LastResizeUtc;
        public int? ExitCode;
        public TerminalResizeTrace[] RecentResizes;
    }
    // Fault/exit metadata only: never command lines, terminal text, paths or exception messages.
    internal static class TerminalDiagnostics {
        private static readonly object gate=new object();
        internal static void Record(string root,TerminalFault fault) {
            try {lock(gate) {
                Directory.CreateDirectory(root);
                string path=Path.Combine(root,"terminal-diagnostics.jsonl"),previous=path+".previous";
                if(File.Exists(path)&&new FileInfo(path).Length>=256*1024) {if(File.Exists(previous))File.Delete(previous);File.Move(path,previous);}
                var entry=new {utc=DateTime.UtcNow.ToString("o"),backend="Microsoft.ConPTY/1.25.260930003",pid=fault.Pid,operation=fault.Operation,hresult=fault.HResult.ToString("X8"),process=fault.ProcessState,exitCode=fault.ExitCode,cols=fault.Cols,rows=fault.Rows,appliedCols=fault.AppliedCols,appliedRows=fault.AppliedRows,lastResizeUtc=fault.LastResizeUtc,recentResizes=fault.RecentResizes,inputDisconnected=fault.InputDisconnected};
                File.AppendAllText(path,new JavaScriptSerializer().Serialize(entry)+Environment.NewLine);
            }}catch(Exception){}
        }
    }
}
