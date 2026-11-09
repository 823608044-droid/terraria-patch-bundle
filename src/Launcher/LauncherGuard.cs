using System;using System.Diagnostics;using System.Runtime.InteropServices;using System.Threading;
namespace TerrariaInjector {
 internal static class LauncherGuard {
  private static Mutex mutex;
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern int MessageBoxW(IntPtr o,string t,string c,uint f);
  internal static bool CanStart(){
   bool created;mutex=new Mutex(true,"Local\\TerrariaAllPluginsLauncher",out created);bool running=!created;
   int own=Process.GetCurrentProcess().Id;
   foreach(string name in new[]{"Terraria","TerrariaInjector","TerrariaAllPlugins","TerrariaAllPluginsV2","TerrariaPluginStart","TerrariaBundleStart"})
    foreach(var p in Process.GetProcessesByName(name)){using(p){if(p.Id!=own)running=true;}}
   if(!running)return true;
   MessageBoxW(IntPtr.Zero,"游戏或启动器已经在运行，请先保存并退出，再启动整合版。","泰拉瑞亚补丁整合版",64);return false;
  }
 }
}