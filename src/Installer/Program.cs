using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.IO.Compression;using System.Reflection;using System.Security.Cryptography;using System.Web.Script.Serialization;using System.Diagnostics;using System.Windows.Forms;using System.Drawing;using Microsoft.Win32;using System.Threading.Tasks;
public class BundleFile {public string path;public long size;public string sha256;public long ticks;}
public class BundleMod {public string id,name,version;public string[] dependencies;public bool light,special;}
public class BundleManifest {public string version,edition,gameVersion,originalGameSha256,existingPatchedSha256;public BundleFile[] files;public BundleMod[] mods;}
public class SavedRoot {public string name,hash,originalHash;public bool existed;}
public class InstallState {public string backup,phase,gameHash,preset,version;public bool hadMods;public List<SavedRoot> roots=new List<SavedRoot>();}
internal static class Engine {
 internal static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=16*1024*1024};
 internal static readonly string[] Roots={"Terraria.exe","TerrariaBundleStart.exe","TerrariaBundleStart.exe.config"};
 internal const string Recovery=".TerrariaBundle-recovery.json";
 internal static string Hash(string p){using(var f=File.OpenRead(p))using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
 internal static string Safe(string root,string rel){
  if(string.IsNullOrEmpty(rel)||Path.IsPathRooted(rel)||rel.Contains(":")||rel.Split('/','\\').Any(x=>x==".."||x=="."||x.Length==0||x.EndsWith(".")||x.EndsWith(" ")))throw new InvalidDataException("安装包路径无效："+rel);
  string prefix=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
  string p=Path.GetFullPath(Path.Combine(prefix,rel.Replace('/',Path.DirectorySeparatorChar)));
  if(!p.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("安装包路径越界");
  CheckLinks(p);return p;
 }
 internal static void CheckLinks(string path){for(var p=Path.GetFullPath(path);!string.IsNullOrEmpty(p);p=Path.GetDirectoryName(p)){if((Directory.Exists(p)||File.Exists(p))&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new IOException("不支持符号链接或目录联接："+p);}}
 internal static void TreeLinks(string path){CheckLinks(path);if(!Directory.Exists(path))return;foreach(var p in Directory.EnumerateFileSystemEntries(path)){CheckLinks(p);if(Directory.Exists(p))TreeLinks(p);}}
 internal static void WriteJson(string p,object o){Directory.CreateDirectory(Path.GetDirectoryName(p));var temp=p+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllText(temp,Json.Serialize(o),new System.Text.UTF8Encoding(false));if(File.Exists(p))File.Replace(temp,p,null);else File.Move(temp,p);}
 internal static void CheckRunning(string game){int own=Process.GetCurrentProcess().Id;foreach(var n in new[]{"Terraria","TerrariaInjector","TerrariaAllPlugins","TerrariaAllPluginsV2","TerrariaPluginStart","TerrariaBundleStart","TerrariaServer"})foreach(var p in Process.GetProcessesByName(n)){using(p){if(p.Id==own)continue;string folder;try{folder=Path.GetDirectoryName(p.MainModule.FileName);}catch{throw new IOException("无法确认正在运行的游戏位置，请先保存并关闭游戏，再安装或恢复。");}if(string.Equals(Path.GetFullPath(folder),Path.GetFullPath(game),StringComparison.OrdinalIgnoreCase))throw new IOException("请先保存并关闭这个目录的游戏、服务器及补丁启动器，再安装或恢复。");}}}
 internal static BundleManifest Extract(string dir,Action<string> log){
  using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))using(var zip=new ZipArchive(stream,ZipArchiveMode.Read)){
   var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0;
   foreach(var e in zip.Entries){Safe(dir,e.FullName);if(!names.Add(e.FullName)||e.Length>600*1024*1024||e.Length<0)throw new InvalidDataException("安装包条目无效");total+=e.Length;}
   if(total>1024L*1024*1024)throw new InvalidDataException("安装包体积超限");
   var me=zip.GetEntry("bundle-manifest.json");if(me==null||me.Length>16*1024*1024)throw new InvalidDataException("缺少安装清单");
   BundleManifest m;using(var r=new StreamReader(me.Open()))m=Json.Deserialize<BundleManifest>(r.ReadToEnd());
   if(m.files==null||m.mods==null||m.gameVersion!="1.4.5.8")throw new InvalidDataException("安装清单无效");
   if(m.files.Length+1!=zip.Entries.Count||m.files.Select(x=>x.path).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=m.files.Length)throw new InvalidDataException("安装包文件清单不一致");
   foreach(var f in m.files){var e=zip.GetEntry(f.path);if(e==null||e.Length!=f.size)throw new InvalidDataException("缺少文件："+f.path);string p=Safe(dir,f.path);Directory.CreateDirectory(Path.GetDirectoryName(p));using(var input=e.Open())using(var output=new FileStream(p,FileMode.CreateNew,FileAccess.Write))input.CopyTo(output);if(Hash(p)!=f.sha256)throw new InvalidDataException("文件校验失败："+f.path);File.SetLastWriteTimeUtc(p,new DateTime(f.ticks,DateTimeKind.Utc));}
   var ids=new HashSet<string>(m.mods.Select(x=>x.id),StringComparer.OrdinalIgnoreCase);
   foreach(var mod in m.mods){if(!System.Text.RegularExpressions.Regex.IsMatch(mod.id,"^[a-z0-9-]+$"))throw new InvalidDataException("插件编号无效");foreach(var dep in mod.dependencies??new string[0])if(!ids.Contains(dep))throw new InvalidDataException("插件依赖缺失："+dep);}
   log("全部文件的 SHA-256 校验通过。");return m;
  }
 }
 internal static void Profile(string stage,BundleManifest m,string preset,bool special,bool fishing,bool highFps){
  var enabled=new HashSet<string>(m.mods.Where(x=>x.special?special:(preset=="full"||x.light)).Select(x=>x.id));
  bool added;do{added=false;foreach(var mod in m.mods.Where(x=>enabled.Contains(x.id)))foreach(var dep in mod.dependencies??new string[0])if(enabled.Add(dep))added=true;}while(added);
  foreach(var mod in m.mods.Where(x=>!enabled.Contains(x.id))){string src=Safe(stage,"TerrariaModder/mods/"+mod.id),dst=Safe(stage,"TerrariaModder/bundle-disabled/"+mod.id);Directory.CreateDirectory(Path.GetDirectoryName(dst));Directory.Move(src,dst);}
  if(fishing)File.WriteAllText(Safe(stage,"TerrariaModder/enable-native-fishing.flag"),"Enabled by installer option\n");
  if(highFps)WriteJson(Safe(stage,"TerrariaModder/core/configs/fps-unlocked.client.json"),new{_version=1,Enabled=true,Mode="Capped",MaxFps=120,Interpolation=true,MouseEveryFrame=true});
  WriteJson(Safe(stage,"TerrariaModder/bundle-profile.json"),new{preset,enabled=enabled.OrderBy(x=>x).ToArray(),special,fishing,highFps,m.version,m.edition});
 }
 static void PreserveData(string game,string stage,BundleManifest m,bool keepConfig){
  string old=Safe(game,"TerrariaModder");if(!Directory.Exists(old))return;
  var shipped=new HashSet<string>(m.files.Select(x=>x.path),StringComparer.OrdinalIgnoreCase);
  foreach(var mod in m.mods){string src=Safe(old,"mods/"+mod.id);if(!Directory.Exists(src))src=Safe(old,"bundle-disabled/"+mod.id);if(!Directory.Exists(src))continue;
   string dest=Directory.Exists(Safe(stage,"TerrariaModder/mods/"+mod.id))?"TerrariaModder/mods/":"TerrariaModder/bundle-disabled/";
   foreach(var p in Directory.EnumerateFiles(src,"*",SearchOption.AllDirectories)){
    string rel=p.Substring(src.Length+1),first=rel.Split('\\','/')[0].ToLowerInvariant();
    bool data=new[]{"worlds","players","blueprints","recyclebin","map-backups","data"}.Contains(first);
    if(!data)continue;string t=Safe(stage,dest+mod.id+"/"+rel.Replace('\\','/'));Directory.CreateDirectory(Path.GetDirectoryName(t));File.Copy(p,t,false);
   }
  }
  if(keepConfig){string src=Safe(old,"core/configs");if(Directory.Exists(src))foreach(var p in Directory.GetFiles(src,"*.json")){string id=Path.GetFileName(p).Replace(".client.json","").Replace(".server.json","");if(!m.mods.Any(x=>x.id==id))continue;string t=Safe(stage,"TerrariaModder/core/configs/"+Path.GetFileName(p));Directory.CreateDirectory(Path.GetDirectoryName(t));File.Copy(p,t,true);}}
  foreach(var rel in new[]{"core/identity.json","core/keybinds.json"}){string p=Safe(old,rel);if(File.Exists(p))File.Copy(p,Safe(stage,"TerrariaModder/"+rel),true);}
 }
 internal static void Install(string game,string preset,bool special,bool fishing,bool highFps,bool keepConfig,Action<string> log,int failAfter=0){
  game=Path.GetFullPath(game);CheckLinks(game);if(!File.Exists(Safe(game,"Terraria.exe")))throw new IOException("请选择包含 Terraria.exe 的游戏目录。");CheckRunning(game);
  if(File.Exists(Safe(game,Recovery)))throw new IOException("检测到上次未完成的安装，请先点击恢复上次版本。");
  TreeLinks(Safe(game,"TerrariaModder"));TreeLinks(Safe(game,"TerrariaBundleBackups"));
  using(var gate=new FileStream(Safe(game,".TerrariaBundle-install.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
   string stage=Safe(game,".TerrariaBundle-stage-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);InstallState state=null;bool committed=false;
   try{
    log("正在解包并校验文件……");var m=Extract(stage,log);PreserveData(game,stage,m,keepConfig);Profile(stage,m,preset,special,fishing,highFps);
    log("正在校验并应用视距与宽屏补丁……");GamePatch.Apply(Safe(game,"Terraria.exe"),Safe(stage,"Terraria.exe"),m.originalGameSha256,m.existingPatchedSha256);
    string backup="TerrariaBundleBackups/"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,8);Directory.CreateDirectory(Safe(game,backup));
    state=new InstallState{backup=backup,phase="prepared",gameHash=Hash(Safe(stage,"Terraria.exe")),preset=preset,version=m.version,hadMods=Directory.Exists(Safe(game,"TerrariaModder"))};
    foreach(string name in Roots){string p=Safe(game,name);state.roots.Add(new SavedRoot{name=name,existed=File.Exists(p),originalHash=File.Exists(p)?Hash(p):null,hash=Hash(Safe(stage,name))});if(File.Exists(p))File.Copy(p,Safe(game,backup+"/"+name));}
    WriteJson(Safe(game,Recovery),state);
    if(state.hadMods)Directory.Move(Safe(game,"TerrariaModder"),Safe(game,backup+"/TerrariaModder"));
    state.phase="old-moved";WriteJson(Safe(game,Recovery),state);
    Directory.Move(Safe(stage,"TerrariaModder"),Safe(game,"TerrariaModder"));
    state.phase="new-moved";WriteJson(Safe(game,Recovery),state);
    int n=0;foreach(string name in Roots){AtomicCopy(Safe(stage,name),Safe(game,name));if(++n==failAfter)throw new IOException("故障注入：验证安装中断回退。");}
    WriteJson(Safe(game,"TerrariaModder/bundle-install.json"),state);File.Delete(Safe(game,Recovery));committed=true;
    log("安装完成。请双击游戏目录内 TerrariaBundleStart.exe 启动，F6 调整补丁设置。");
    log("上次版本备份："+Safe(game,backup));
   }catch{if(state!=null&&File.Exists(Safe(game,Recovery)))try{RestoreCore(game,state,false,log);}catch(Exception e){log("自动回退未完成，请保留恢复记录并再次点击恢复："+e.Message);}throw;}
   finally{if(Directory.Exists(stage)){TreeLinks(stage);Directory.Delete(stage,true);}if(!committed)log("安装未提交，原文件已回退或保留恢复记录。");}
  }
 }
 static void AtomicCopy(string src,string dst){string temp=dst+".bundle-"+Guid.NewGuid().ToString("N")+".tmp";File.Copy(src,temp);try{if(File.Exists(dst))File.Replace(temp,dst,null);else File.Move(temp,dst);}finally{if(File.Exists(temp))File.Delete(temp);}}
 static void CopyTree(string src,string dst){TreeLinks(src);Directory.CreateDirectory(dst);foreach(var p in Directory.EnumerateFiles(src,"*",SearchOption.AllDirectories)){string rel=p.Substring(src.Length+1),target=Safe(dst,rel);Directory.CreateDirectory(Path.GetDirectoryName(target));File.Copy(p,target);File.SetLastWriteTimeUtc(target,File.GetLastWriteTimeUtc(p));}}
 internal static void Restore(string game,Action<string> log){
  game=Path.GetFullPath(game);CheckLinks(game);CheckRunning(game);using(var gate=new FileStream(Safe(game,".TerrariaBundle-install.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
   string p=Safe(game,Recovery);bool pending=File.Exists(p);if(!pending)p=Safe(game,"TerrariaModder/bundle-install.json");if(!File.Exists(p))throw new IOException("没有找到本安装器的恢复记录。");
   var s=Json.Deserialize<InstallState>(File.ReadAllText(p));RestoreCore(game,s,!pending,log);
  }
 }
 static void RestoreCore(string game,InstallState s,bool verify,Action<string> log){
  if(s==null||!System.Text.RegularExpressions.Regex.IsMatch(s.backup??"","^TerrariaBundleBackups/[0-9]{8}-[0-9]{6}-[0-9a-f]{8}$")||s.roots==null||s.roots.Count!=Roots.Length||!s.roots.Select(x=>x.name).OrderBy(x=>x).SequenceEqual(Roots.OrderBy(x=>x)))throw new InvalidDataException("恢复记录无效。");
  string backup=Safe(game,s.backup);TreeLinks(backup);TreeLinks(Safe(game,"TerrariaModder"));
  foreach(var r in s.roots){if(r.existed&&(!File.Exists(Safe(backup,r.name))||Hash(Safe(backup,r.name))!=r.originalHash))throw new IOException("备份文件缺失或校验失败："+r.name);string p=Safe(game,r.name);if(verify&&(!File.Exists(p)||Hash(p)!=r.hash))throw new IOException("安装后文件已被其他程序更改，已保留备份，请先人工核对："+r.name);}
  if(s.hadMods&&!Directory.Exists(Safe(backup,"TerrariaModder"))&&s.phase!="prepared")throw new IOException("原补丁目录备份缺失。");
  string current=Safe(game,"TerrariaModder"),restoreStage=null;
  if(s.hadMods&&Directory.Exists(Safe(backup,"TerrariaModder"))){restoreStage=Safe(game,".TerrariaBundle-restore-"+Guid.NewGuid().ToString("N"));CopyTree(Safe(backup,"TerrariaModder"),restoreStage);}
  if(Directory.Exists(current)&&(!s.hadMods||restoreStage!=null))Directory.Move(current,Safe(backup,"removed-TerrariaModder-"+Guid.NewGuid().ToString("N")));
  if(restoreStage!=null)Directory.Move(restoreStage,current);
  foreach(var r in s.roots){string p=Safe(game,r.name);if(r.existed)AtomicCopy(Safe(backup,r.name),p);else if(File.Exists(p))File.Move(p,Safe(backup,"removed-"+Guid.NewGuid().ToString("N")+"-"+r.name));}
  if(File.Exists(Safe(game,Recovery)))File.Delete(Safe(game,Recovery));log("已恢复安装前版本；本次整合版文件保留在备份目录，角色和世界存档未改动。");
 }
 internal static string FindGame(){
  var steam=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(string key in new[]{@"HKEY_CURRENT_USER\Software\Valve\Steam",@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam"}){foreach(string val in new[]{"SteamPath","InstallPath"}){var s=Registry.GetValue(key,val,null) as string;if(!string.IsNullOrEmpty(s))steam.Add(s);}}
  steam.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam"));
  foreach(var root in steam.ToArray()){string vdf=Path.Combine(root,"steamapps","libraryfolders.vdf");if(File.Exists(vdf))foreach(System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(vdf),"\"path\"\\s*\"([^\"]+)\""))steam.Add(m.Groups[1].Value.Replace("\\\\","\\"));}
  foreach(var root in steam){string game=Path.Combine(root,"steamapps","common","Terraria");if(File.Exists(Path.Combine(game,"Terraria.exe")))return game;}return "";
 }
}
internal static class Program {
 [STAThread]static int Main(string[] args){
  AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{if(new AssemblyName(e.Name).Name!="Mono.Cecil")return null;using(var input=Assembly.GetExecutingAssembly().GetManifestResourceStream("Mono.Cecil.dll"))using(var ms=new MemoryStream()){input.CopyTo(ms);return Assembly.Load(ms.ToArray());}};
  if(args.Length>0){try{string Get(string key,string fallback=""){int i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:fallback;}Action<string> log=x=>{string path=Get("--log");if(path.Length>0)File.AppendAllText(path,x+Environment.NewLine);};string game=Get("--game");
    if(args.Contains("--install"))Engine.Install(game,Get("--preset","full"),args.Contains("--special"),args.Contains("--fishing"),args.Contains("--high-fps"),args.Contains("--keep-config"),log,int.Parse(Get("--fail-after","0")));
    else if(args.Contains("--restore"))Engine.Restore(game,log);
    else if(args.Contains("--selftest")){SelfTest.Run();log("PASS: path validation, package checksums, dependency and light preset checks.");}
    else if(args.Contains("--preview")){Application.EnableVisualStyles();using(var form=new SetupForm()){form.ShowInTaskbar=false;form.Opacity=0;form.Show();Application.DoEvents();using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height));bitmap.Save(Get("--output"),System.Drawing.Imaging.ImageFormat.Png);}form.Hide();}}
    else throw new ArgumentException("未知操作");return 0;
   }catch(Exception e){string path=args.SkipWhile(x=>x!="--log").Skip(1).FirstOrDefault();if(path!=null)File.AppendAllText(path,e.ToString()+Environment.NewLine);return 1;}}
  Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new SetupForm());return 0;
 }
}
internal sealed class SetupForm:Form {
 readonly TextBox path=new TextBox(),output=new TextBox();readonly ComboBox preset=new ComboBox();readonly CheckBox fishing=new CheckBox(),special=new CheckBox(),fps=new CheckBox(),keep=new CheckBox();readonly Button install=new Button(),restore=new Button(),browse=new Button();
 internal SetupForm(){
  Text="泰拉瑞亚补丁整合安装器 · 2026.11.09";ClientSize=new Size(760,590);Font=new Font("Microsoft YaHei UI",10);StartPosition=FormStartPosition.CenterScreen;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;
  Controls.Add(new Label{Text="原版 Terraria 1.4.5.8 · Windows · 单人",Left=22,Top=18,Width=700,Height=28,Font=new Font(Font,FontStyle.Bold)});
  Controls.Add(new Label{Text="自动识别游戏目录；可选择完整玩法或轻量便利。安装前请保存并退出游戏。",Left=22,Top=52,Width=710,Height=25});
  path.SetBounds(22,86,605,30);path.Text=Engine.FindGame();Controls.Add(path);browse.Text="选择目录";browse.SetBounds(637,84,100,33);browse.Click+=(s,e)=>{using(var d=new FolderBrowserDialog{Description="选择包含 Terraria.exe 的文件夹",SelectedPath=path.Text})if(d.ShowDialog()==DialogResult.OK)path.Text=d.SelectedPath;};Controls.Add(browse);
  preset.SetBounds(22,134,715,30);preset.DropDownStyle=ComboBoxStyle.DropDownList;preset.Items.AddRange(new object[]{"完整玩法：便利 + 自制装备 + 战斗扩展（具体内容见随包清单）","轻量便利：仓储 + 建造 + 查询 + 日常辅助"});preset.SelectedIndex=0;Controls.Add(preset);
  var boxes=new[]{fishing,special,fps,keep};var labels=new[]{"启用原生自动钓鱼（可选，默认关闭）","新世界默认特殊种子选项（可选）","启用最高 120 帧渲染（默认使用原版节奏）","保留已有补丁设置（升级时使用）"};for(int i=0;i<boxes.Length;i++){boxes[i].Text=labels[i];boxes[i].SetBounds(22+(i%2)*360,180+(i/2)*36,355,30);Controls.Add(boxes[i]);}
  Controls.Add(new Label{Text="安装会校验文件并备份原补丁与主程序。不会修改角色、世界和 Steam 账号。\n分享候选版会剔除许可未确认的第三方移植；完整本机测试版不宜公开上传。",Left=22,Top=262,Width=715,Height=54});
  install.Text="一键安装 / 应用预设";install.SetBounds(22,328,345,40);restore.Text="恢复上次安装前版本";restore.SetBounds(387,328,350,40);Controls.Add(install);Controls.Add(restore);
  output.Multiline=true;output.ReadOnly=true;output.ScrollBars=ScrollBars.Vertical;output.SetBounds(22,385,715,180);Controls.Add(output);
  install.Click+=async(s,e)=>await Work(false);restore.Click+=async(s,e)=>await Work(true);
 }
 async Task Work(bool undo){string game=path.Text,profile=preset.SelectedIndex==0?"full":"light";bool a=special.Checked,b=fishing.Checked,c=fps.Checked,d=keep.Checked;foreach(var ctl in new Control[]{install,restore,browse,path,preset,fishing,special,fps,keep})ctl.Enabled=false;output.Clear();Action<string> log=x=>BeginInvoke((Action)(()=>output.AppendText(x+Environment.NewLine)));
  try{await Task.Run(()=>{if(undo)Engine.Restore(game,log);else Engine.Install(game,profile,a,b,c,d,log);});MessageBox.Show(this,undo?"已恢复上次版本。":"安装完成！请打开游戏目录内 TerrariaBundleStart.exe。","完成",MessageBoxButtons.OK,MessageBoxIcon.Information);}catch(Exception ex){output.AppendText(ex.Message+Environment.NewLine);MessageBox.Show(this,ex.Message,"未完成",MessageBoxButtons.OK,MessageBoxIcon.Warning);}finally{foreach(var ctl in new Control[]{install,restore,browse,path,preset,fishing,special,fps,keep})ctl.Enabled=true;}}
}
internal static class SelfTest {
 internal static void Run(){string root=Path.Combine(Path.GetTempPath(),"TerrariaBundleSelfTest-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);try{foreach(string bad in new[]{"../escape","/absolute","C:/escape","a/../b","a//b","file:stream","a./b","a /b"}){bool rejected=false;try{Engine.Safe(root,bad);}catch(InvalidDataException){rejected=true;}if(!rejected)throw new Exception("Path traversal accepted: "+bad);}if(!Engine.Safe(root,"safe/file.txt").StartsWith(root))throw new Exception("Safe path failed");var m=Engine.Extract(root,x=>{});if(m.mods.Length<30)throw new Exception("Missing plugins");Engine.Profile(root,m,"light",false,false,false);if(File.Exists(Path.Combine(root,"TerrariaModder","enable-native-fishing.flag")))throw new Exception("Fishing enabled unexpectedly");foreach(var mod in m.mods){string disabled=Path.Combine(root,"TerrariaModder","bundle-disabled",mod.id);if(Directory.Exists(disabled))Directory.Move(disabled,Path.Combine(root,"TerrariaModder","mods",mod.id));}Engine.Profile(root,m,"light",true,true,true);foreach(var mod in m.mods.Where(x=>x.special))if(!Directory.Exists(Path.Combine(root,"TerrariaModder","mods",mod.id)))throw new Exception("Explicit special option ignored by light preset");if(!File.Exists(Path.Combine(root,"TerrariaModder","enable-native-fishing.flag")))throw new Exception("Explicit fishing option ignored");var fps=Engine.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(root,"TerrariaModder","core","configs","fps-unlocked.client.json")));if(!(bool)fps["Enabled"]||(int)fps["MaxFps"]!=120)throw new Exception("Explicit FPS option ignored");}finally{Engine.TreeLinks(root);Directory.Delete(root,true);}}
}
