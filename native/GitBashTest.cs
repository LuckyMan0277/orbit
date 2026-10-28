using System;
using System.IO;

namespace Orbit {
    internal static class GitBashTest {
        internal static void Run(string root) {
            string standard=Path.Combine(root,"Git With Spaces");
            string bin=Path.Combine(standard,"bin","bash.exe");Directory.CreateDirectory(Path.GetDirectoryName(bin));File.WriteAllBytes(bin,new byte[0]);
            string found=GitBash.FindExecutable(new[]{standard},File.Exists);
            if(found!=bin)throw new Exception("resolver did not prefer the Git for Windows bin/bash.exe");
            string fallback=Path.Combine(root,"usr-layout","usr","bin","bash.exe");Directory.CreateDirectory(Path.GetDirectoryName(fallback));File.WriteAllBytes(fallback,new byte[0]);
            found=GitBash.FindExecutable(new[]{Path.Combine(root,"usr-layout")},File.Exists);
            if(found!=fallback)throw new Exception("resolver did not accept the Git for Windows usr/bin/bash.exe layout");
            if(GitBash.Command(bin)!="\""+bin+"\" --login -i")throw new Exception("Git Bash command line did not quote its installation path");
            bool missing=false;try{GitBash.FindExecutable(new[]{Path.Combine(root,"missing")},delegate(string unused){return false;});}catch(FileNotFoundException ex){missing=ex.Message.Contains("Git for Windows");}
            if(!missing)throw new Exception("missing Git Bash did not give an installation hint");
        }
    }
}
