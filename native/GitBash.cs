using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace Orbit {
    internal static class GitBash {
        internal static string ResolveExecutable() {
            return FindExecutable(InstallRoots(),File.Exists);
        }

        internal static string FindExecutable(IEnumerable<string> roots,Func<string,bool> fileExists) {
            foreach(string value in roots) {
                if(String.IsNullOrWhiteSpace(value))continue;
                string root;
                try { root=Path.GetFullPath(value); } catch { continue; }
                foreach(string relative in new[]{Path.Combine("bin","bash.exe"),Path.Combine("usr","bin","bash.exe")}) {
                    string candidate=Path.Combine(root,relative);
                    if(fileExists(candidate))return candidate;
                }
            }
            throw new FileNotFoundException("Git Bash를 찾을 수 없습니다. Git for Windows를 설치해 주세요. https://git-scm.com/download/win");
        }

        internal static string Command(string bashPath) {
            if(String.IsNullOrWhiteSpace(bashPath))throw new ArgumentException("Git Bash 경로가 비어 있습니다.","bashPath");
            return "\""+bashPath.Replace("\"","\\\"")+"\" --login -i";
        }

        internal static Dictionary<string,string> TerminalEnvironment() {
            return new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase) {
                {"CHERE_INVOKING","1"},{"MSYS_CHERE_INVOKING","1"},{"TERM","xterm-256color"},{"COLORTERM","truecolor"}
            };
        }

        internal static IEnumerable<string> InstallRoots() {
            var roots=new List<string>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Action<string> add=value=>{if(String.IsNullOrWhiteSpace(value))return;try{string full=Path.GetFullPath(value);if(seen.Add(full))roots.Add(full);}catch{}};
            add(Environment.GetEnvironmentVariable("GIT_INSTALL_ROOT"));
            add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Git"));
            add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Git"));
            string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if(!String.IsNullOrEmpty(local))add(Path.Combine(local,"Programs","Git"));
            foreach(RegistryHive hive in new[]{RegistryHive.LocalMachine,RegistryHive.CurrentUser})
                foreach(RegistryView view in new[]{RegistryView.Registry64,RegistryView.Registry32})
                    try {
                        using(RegistryKey baseKey=RegistryKey.OpenBaseKey(hive,view))
                        using(RegistryKey key=baseKey.OpenSubKey(@"SOFTWARE\GitForWindows"))
                            if(key!=null)add(Convert.ToString(key.GetValue("InstallPath")));
                    } catch { }
            string path=Environment.GetEnvironmentVariable("PATH")??"";
            foreach(string entry in path.Split(Path.PathSeparator)) {
                if(String.IsNullOrWhiteSpace(entry))continue;
                string directory=entry.Trim().Trim('"');
                try {
                    string candidate=Path.GetFullPath(directory);
                    for(int depth=0;depth<4&&!String.IsNullOrEmpty(candidate);depth++) {
                        add(candidate);
                        DirectoryInfo parent=Directory.GetParent(candidate);
                        candidate=parent==null?null:parent.FullName;
                    }
                } catch { }
            }
            return roots;
        }
    }
}
