using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

// orbit-secret: the command an AI (or the user) runs inside an Orbit terminal to use API keys without the key ever appearing in a
// conversation. It talks to the Orbit window that started this terminal through a per-terminal token (ORBIT_PIPE / ORBIT_TERMINAL_AUTH).
//   orbit-secret list                          names of stored secrets
//   orbit-secret request NAME [reason...]      ask the user to enter NAME in a private Orbit prompt (the value is never shown here)
//   orbit-secret run [--only A,B] -- CMD ...   run CMD with the secrets in its environment
static class OrbitSecret {
    const string Usage="usage:\n  orbit-secret list\n  orbit-secret request NAME [reason]\n  orbit-secret run [--only A,B] -- COMMAND [ARGS...]";
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
    static int Main(string[] args) {
        try {
            if(args.Length==0||args[0]=="help"||args[0]=="--help"||args[0]=="-h") { Console.Error.WriteLine(Usage); return args.Length==0?1:0; }
            switch(args[0]) {
                case "list": return List();
                case "request": return Request(args);
                case "run": return RunCommand(args.Skip(1).ToArray());
                default: Console.Error.WriteLine("orbit-secret: unknown command '"+args[0]+"'\n"+Usage); return 1;
            }
        } catch(Exception ex) { Console.Error.WriteLine("orbit-secret: "+ex.Message); return 1; }
    }
    static int List() {
        using(var link=new Link(new Dictionary<string,object>{{"op","list"}})) {
            // What this terminal's project can use: keys for all projects and keys of this project only.
            var keys=(link.Next()["keys"] as IEnumerable ?? new object[0]).Cast<object>().OfType<IDictionary>().ToArray();
            if(keys.Length==0) { Console.WriteLine("No secrets are stored for this project yet. Ask the user for one with: orbit-secret request NAME \"why you need it\""); return 0; }
            Console.WriteLine("Stored secret names for this project:");
            foreach(var k in keys) Console.WriteLine("  "+Convert.ToString(k["name"])+(Convert.ToString(k["scope"])=="project"?"  (this project only)":""));
            return 0;
        }
    }
    static int Request(string[] args) {
        if(args.Length<2) { Console.Error.WriteLine(Usage); return 1; }
        string name=args[1],reason=String.Join(" ",args.Skip(2));
        using(var link=new Link(new Dictionary<string,object>{{"op","request"},{"name",name},{"reason",reason}})) {
            var first=link.Next();
            if(Str(first,"status")=="exists") { Console.WriteLine(name+" is already stored. Terminals opened after it was saved have it in their environment; otherwise run your command as: orbit-secret run -- <command>"); return 0; }
            // Printed only after the request exists in Orbit, so this line reaching a phone also tells it to look for the prompt.
            Console.Error.WriteLine("Waiting for the user to enter "+name+" in Orbit (up to 10 minutes)...");
            string status=Str(link.Next(),"status");
            if(status=="saved") { Console.WriteLine("Saved. "+name+" is stored; its value is not shown to you. Use it in code as the environment variable "+name+" and run commands that need it with: orbit-secret run -- <command>"); return 0; }
            if(status=="denied") { Console.WriteLine("The user declined to provide "+name+". Do not ask again unless they tell you to."); return 2; }
            Console.WriteLine("No answer was given for "+name+" ("+status+")."); return 3;
        }
    }
    static int RunCommand(string[] args) {
        List<string> only=null;int i=0;
        if(args.Length>1&&args[0]=="--only") { only=args[1].Split(new[]{','},StringSplitOptions.RemoveEmptyEntries).Select(x=>x.Trim()).ToList();i=2; }
        if(i<args.Length&&args[i]=="--") i++;
        string[] command=args.Skip(i).ToArray();
        if(command.Length==0) { Console.Error.WriteLine(Usage); return 1; }
        var request=new Dictionary<string,object>{{"op","env"}};if(only!=null)request["names"]=only;
        var secrets=new Dictionary<string,string>();
        using(var link=new Link(request)) { var env=link.Next()["env"] as IDictionary;if(env!=null)foreach(DictionaryEntry e in env)secrets[Convert.ToString(e.Key)]=Convert.ToString(e.Value); }
        return Spawn(command,secrets);
    }
    static string Str(Dictionary<string,object> map,string key) { object v;return map.TryGetValue(key,out v)&&v!=null?Convert.ToString(v):""; }

    sealed class Link:IDisposable {
        readonly NamedPipeClientStream pipe;readonly StreamReader reader;readonly StreamWriter writer;
        public Link(Dictionary<string,object> payload) {
            string name=Environment.GetEnvironmentVariable("ORBIT_PIPE"),token=Environment.GetEnvironmentVariable("ORBIT_TERMINAL_AUTH");
            if(String.IsNullOrEmpty(name)||String.IsNullOrEmpty(token)) throw new InvalidOperationException("run this inside a terminal opened by Orbit (ORBIT_PIPE is not set)");
            pipe=new NamedPipeClientStream(".",name,PipeDirection.InOut);
            try { pipe.Connect(5000); } catch(TimeoutException) { throw new InvalidOperationException("could not reach Orbit; is the Orbit window that opened this terminal still running?"); }
            var utf8=new UTF8Encoding(false);reader=new StreamReader(pipe,utf8);writer=new StreamWriter(pipe,utf8){AutoFlush=true};
            payload["token"]=token;writer.WriteLine(Json.Serialize(payload));
        }
        public Dictionary<string,object> Next() {
            string line=reader.ReadLine();
            if(line==null) throw new InvalidOperationException("Orbit closed the connection");
            var map=Json.Deserialize<Dictionary<string,object>>(line)??new Dictionary<string,object>();
            if(map.ContainsKey("error")) throw new InvalidOperationException(Convert.ToString(map["error"]));
            return map;
        }
        public void Dispose() { try{pipe.Dispose();}catch{} }
    }

    // ---- run: start COMMAND with this process's own standard handles (console or pipes) and the secrets added to its environment.
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct STARTUPINFO { public int cb; public string reserved,desktop,title; public int x,y,xSize,ySize,xChars,yChars,fill,flags; public short show,reserved2; public IntPtr reservedPtr,input,output,error; }
    [StructLayout(LayoutKind.Sequential)] struct PROCESS_INFORMATION { public IntPtr process,thread; public int processId,threadId; }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CreateProcess(string app,StringBuilder command,IntPtr pa,IntPtr ta,bool inherit,uint flags,IntPtr env,string cwd,ref STARTUPINFO info,out PROCESS_INFORMATION process);
    [DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int which);
    [DllImport("kernel32.dll")] static extern bool SetHandleInformation(IntPtr handle,int mask,int flags);
    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr handle,uint timeout);
    [DllImport("kernel32.dll")] static extern bool GetExitCodeProcess(IntPtr process,out uint code);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    static int Spawn(string[] command,Dictionary<string,string> secrets) {
        // Through cmd so .cmd/.bat shims (npm, npx) run like they do at a prompt.
        string shell=Environment.GetEnvironmentVariable("ComSpec");if(String.IsNullOrEmpty(shell))shell=Path.Combine(Environment.SystemDirectory,"cmd.exe");
        var line=new StringBuilder("\""+shell+"\" /d /s /c \""+String.Join(" ",command.Select(Quote))+"\"");
        var merged=new SortedDictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(DictionaryEntry e in Environment.GetEnvironmentVariables())merged[(string)e.Key]=(string)e.Value;
        foreach(var pair in secrets)merged[pair.Key]=pair.Value;
        var block=new StringBuilder();foreach(var pair in merged)block.Append(pair.Key).Append('=').Append(pair.Value).Append('\0');
        IntPtr environment=Marshal.StringToHGlobalUni(block.Append('\0').ToString());
        try {
            var info=new STARTUPINFO();info.cb=Marshal.SizeOf(info);info.flags=0x100; // STARTF_USESTDHANDLES
            info.input=Inheritable(-10);info.output=Inheritable(-11);info.error=Inheritable(-12);
            PROCESS_INFORMATION pi;
            if(!CreateProcess(null,line,IntPtr.Zero,IntPtr.Zero,true,0x400,environment,null,ref info,out pi)) throw new Win32Exception(Marshal.GetLastWin32Error());
            Console.CancelKeyPress+=(s,e)=>e.Cancel=true; // Ctrl+C reaches the child too; wait for it instead of dying first
            WaitForSingleObject(pi.process,0xFFFFFFFF);
            uint code;GetExitCodeProcess(pi.process,out code);CloseHandle(pi.process);CloseHandle(pi.thread);
            return unchecked((int)code);
        } finally { Marshal.FreeHGlobal(environment); }
    }
    static IntPtr Inheritable(int which) { IntPtr h=GetStdHandle(which);if(h!=IntPtr.Zero&&h!=new IntPtr(-1))SetHandleInformation(h,1,1);return h; }
    static string Quote(string a) {
        if(a.Length>0&&a.IndexOfAny(new[]{' ','\t','"','&','|','<','>','^','(',')'})<0) return a;
        var sb=new StringBuilder("\"");int slashes=0;
        foreach(char c in a) {
            if(c=='\\') slashes++;
            else if(c=='"') { sb.Append('\\',slashes*2+1).Append('"');slashes=0; }
            else { sb.Append('\\',slashes).Append(c);slashes=0; }
        }
        return sb.Append('\\',slashes*2).Append('"').ToString();
    }
}
