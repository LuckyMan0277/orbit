using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Orbit {
    internal static class ShellProfileTest {
        internal static int Run() {
            string report=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"shell-profile-test-results.txt");
            string cwd=Path.Combine(Path.GetTempPath(),"orbit-gitbash-test-"+Guid.NewGuid().ToString("N"));
            ConPty terminal=null;var output=new StringBuilder();var gate=new object();var marker=new ManualResetEvent(false);var exited=new ManualResetEvent(false);int exitCode=-1;
            try {
                GitBashTest.Run(Path.Combine(cwd,"resolver-fixtures"));
                Directory.CreateDirectory(cwd);
                string bash=GitBash.ResolveExecutable();
                terminal=new ConPty("gitbash-smoke",cwd,GitBash.Command(bash),100,30,
                    delegate(string id,string chunk) { lock(gate) { output.Append(chunk); if(output.ToString().Contains("ORBIT_GITBASH_PROFILE_OK")&&output.ToString().Contains(Path.GetFileName(cwd)))marker.Set(); } terminal.Acknowledge(); },
                    delegate(string id,int code) { exitCode=code;exited.Set(); },GitBash.TerminalEnvironment());
                terminal.Start();
                terminal.Write("printf 'ORBIT_GITBASH_'; printf 'PROFILE_OK\\n'; pwd\r");
                if(!marker.WaitOne(20000))throw new TimeoutException("Git Bash did not print the split output marker and working directory within 20 seconds. Output: "+Tail(output,gate));
                terminal.Write("exit\r");
                if(!exited.WaitOne(10000))throw new TimeoutException("Git Bash did not exit after the smoke command. Output: "+Tail(output,gate));
                if(exitCode!=0)throw new InvalidOperationException("Git Bash exited with code "+exitCode+". Output: "+Tail(output,gate));
                File.WriteAllText(report,"PASS: Git for Windows bash.exe resolved and launched through ConPTY; split output marker received; working directory preserved; process exited 0.\r\nPath: "+bash+"\r\n");
                return 0;
            } catch(Exception ex) {
                try { File.WriteAllText(report,"FAIL: Git Bash ConPTY smoke test\r\n"+ex+"\r\n"); } catch { }
                Console.Error.WriteLine("Git Bash smoke test failed: "+ex.Message);
                return 1;
            } finally {
                if(terminal!=null)terminal.Dispose();
                exited.WaitOne(3000);
                try { marker.Dispose();exited.Dispose();Directory.Delete(cwd,true); } catch { }
            }
        }

        private static string Tail(StringBuilder output,object gate) {
            lock(gate) { string text=output.ToString();return text.Length<=2000?text:text.Substring(text.Length-2000); }
        }
    }
}
