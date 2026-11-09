using System;using System.IO;using System.Diagnostics;using System.Runtime.InteropServices;using System.Security.Cryptography;using System.Text;using System.Windows.Forms;using System.Drawing;using Microsoft.Win32;
namespace TerrariaInjector {
 internal static class SteamBootstrap {
  [DllImport("steam_api.dll",CallingConvention=CallingConvention.Cdecl)][return:MarshalAs(UnmanagedType.I1)]static extern bool SteamAPI_IsSteamRunning();
  [DllImport("steam_api.dll",CallingConvention=CallingConvention.Cdecl)]static extern int SteamAPI_InitFlat(StringBuilder error);
  [DllImport("steam_api.dll",CallingConvention=CallingConvention.Cdecl)]static extern void SteamAPI_Shutdown();
  internal static bool Ready(){try{if(!SteamAPI_IsSteamRunning())return false;bool ok=SteamAPI_InitFlat(new StringBuilder(1024))==0;if(ok)SteamAPI_Shutdown();return ok;}catch{return false;}}
  internal static bool ValidateGame(){try{string p=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Terraria.exe");string hash;using(var f=File.OpenRead(p))using(var s=SHA256.Create())hash=BitConverter.ToString(s.ComputeHash(f)).Replace("-","");if(hash=="3C36B82919838FE2D13AA0F0DF574FC7DC8AB0DFFD3E10F7E2E894F422AD1E77")return true;MessageBox.Show("游戏主程序已经更新或与整合包不匹配，请重新使用安装器校验版本。未启动游戏。","整合包版本检查",MessageBoxButtons.OK,MessageBoxIcon.Information);}catch(Exception e){MessageBox.Show("无法读取游戏文件："+e.Message,"无法启动");}return false;}
  internal static bool Ensure(){
   if(Ready())return true;
   try{using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")){string path=key?.GetValue("SteamExe") as string;if(!string.IsNullOrEmpty(path)&&File.Exists(path))Process.Start(new ProcessStartInfo(path,"-silent"){UseShellExecute=true,WindowStyle=ProcessWindowStyle.Hidden});}}catch{}
   bool ready=false;Application.EnableVisualStyles();
   using(var form=new Form{Text="等待 Steam",ClientSize=new Size(460,130),StartPosition=FormStartPosition.CenterScreen,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,Font=new Font("Microsoft YaHei UI",10)})
   using(var timer=new Timer{Interval=1000}){
    form.Controls.Add(new Label{Text="正在等待 Steam 准备完成……\r\n请在 Steam 中完成登录，准备好后会自动启动游戏。",Dock=DockStyle.Fill,Padding=new Padding(20)});
    var watch=Stopwatch.StartNew();timer.Tick+=(s,e)=>{if(Ready()){ready=true;timer.Stop();form.Close();}else if(watch.Elapsed.TotalSeconds>=90){timer.Stop();form.Close();}};form.Shown+=(s,e)=>timer.Start();form.ShowDialog();
   }
   if(!ready)MessageBox.Show("Steam 尚未准备完成。请打开并登录 Steam，然后再次启动整合版。","尚未启动游戏",MessageBoxButtons.OK,MessageBoxIcon.Information);return ready;
  }
 }
}
