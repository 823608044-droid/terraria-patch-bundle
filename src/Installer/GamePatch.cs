using System;using System.IO;using System.Linq;using Mono.Cecil;using Mono.Cecil.Cil;using System.Collections.Generic;
internal static class GamePatch {
 internal static void Apply(string source,string target,string originalHash,string knownPatched){
  string hash=Engine.Hash(source);
  if(hash==knownPatched){File.Copy(source,target);return;}
  if(hash!=originalHash)throw new InvalidOperationException("游戏主程序与已核验的 1.4.5.8 版本不同。请先用 Steam 验证游戏文件，或恢复本包上次安装；不会覆盖未知版本。");
  using(var a=AssemblyDefinition.ReadAssembly(source)){
   var main=a.MainModule.Types.Single(t=>t.FullName=="Terraria.Main");
   Action<string,int,float,float> f=(n,i,old,value)=>{var m=main.Methods.Single(x=>x.Name==n);var ins=m.Body.Instructions[i];if(ins.OpCode!=OpCodes.Ldc_R4||(float)ins.Operand!=old)throw new InvalidDataException("缩放补丁校验失败："+n);ins.Operand=value;};
   f("get_RenderTargetsRequired",1,1,.5f);f("get_RenderTargetsRequired",6,1,.5f);
   f("UpdateViewZoomKeys",11,1,.6f);f("UpdateViewZoomKeys",12,2,3);f("UpdateViewZoomKeys",22,1,.6f);f("UpdateViewZoomKeys",23,2,3);
   f("DoDraw",638,1,.6f);f("DoDraw",639,2,3);
   var i1=main.Methods.Single(m=>m.Name=="SetGraphicsProfileInternal").Body.Instructions[16];
   if(i1.OpCode!=OpCodes.Ldc_I4||(int)i1.Operand!=4096)throw new InvalidDataException("图形补丁校验失败");i1.Operand=8192;
   var i2=main.Methods.Single(m=>m.Name=="InitTargets").Body.Instructions[32];
   if(i2.OpCode!=OpCodes.Ldc_I4||(int)i2.Operand!=192)throw new InvalidDataException("视野边界补丁校验失败");i2.Operand=1024;
   var cctor=main.Methods.Single(m=>m.Name==".cctor");var wide=cctor.Body.Instructions[1619];
   if(wide.OpCode!=OpCodes.Ldc_I4_0||!cctor.Body.Instructions[1620].Operand.ToString().EndsWith("::SupportWideScreen"))throw new InvalidDataException("宽屏补丁校验失败");wide.OpCode=OpCodes.Ldc_I4_1;
   var draw=a.MainModule.Types.Single(t=>t.FullName=="Terraria.IngameOptions").Methods.Single(m=>m.Name=="Draw");var il=draw.Body.GetILProcessor();
   var lo=draw.Body.Instructions[1705];var hi=draw.Body.Instructions[1726];
   if(lo.OpCode!=OpCodes.Ldc_R4||(float)lo.Operand!=1||hi.OpCode!=OpCodes.Ldc_R4||(float)hi.Operand!=1)throw new InvalidDataException("缩放滑条补丁校验失败");
   lo.Operand=.6f;hi.Operand=2.4f;
   il.InsertAfter(hi,Instruction.Create(OpCodes.Mul));il.InsertAfter(hi.Next,Instruction.Create(OpCodes.Ldc_R4,.6f));
   var sub=lo.Next;il.InsertAfter(sub,Instruction.Create(OpCodes.Ldc_R4,2.4f));il.InsertAfter(sub.Next,Instruction.Create(OpCodes.Div));
   var ops=typeof(OpCodes).GetFields().Where(x=>x.FieldType==typeof(OpCode)).Select(x=>(OpCode)x.GetValue(null)).ToDictionary(x=>x.Name);
   foreach(var ins in draw.Body.Instructions)if(ins.OpCode.OperandType==OperandType.ShortInlineBrTarget)ins.OpCode=ops[ins.OpCode.Name.Substring(0,ins.OpCode.Name.Length-2)];
   a.Write(target);
  }
  var bytes=File.ReadAllBytes(target);int pe=BitConverter.ToInt32(bytes,0x3c);bytes[pe+22]|=0x20;File.WriteAllBytes(target,bytes);
 }
}
