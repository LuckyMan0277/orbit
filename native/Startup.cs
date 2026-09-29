using System;
using System.Windows.Forms;
using Microsoft.Win32;
namespace Orbit {
 // "Start Orbit with Windows": one value under the current user's Run key, no service or scheduled task. Orbit then starts
 // minimized with --startup. The uninstaller removes the value (installer/orbit.iss).
 internal static class Startup {
  const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run",Name="Orbit";
  static string Command(){return "\""+Application.ExecutablePath+"\" --startup";}
  public static bool Enabled(){
   try{using(var key=Registry.CurrentUser.OpenSubKey(RunKey,false))return key!=null&&key.GetValue(Name) is string;}catch{return false;}
  }
  public static void Set(bool enabled){
   using(var key=Registry.CurrentUser.CreateSubKey(RunKey)){
    if(enabled)key.SetValue(Name,Command(),RegistryValueKind.String);else key.DeleteValue(Name,false);
   }
  }
  // After an update or reinstall to another folder, point the value at this Orbit.exe again.
  public static void Refresh(){
   try{using(var key=Registry.CurrentUser.OpenSubKey(RunKey,true)){var value=key==null?null:key.GetValue(Name) as string;if(value!=null&&value!=Command())key.SetValue(Name,Command(),RegistryValueKind.String);}}catch{}
  }
 }
}
