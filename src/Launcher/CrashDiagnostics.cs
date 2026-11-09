using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using HarmonyLib;

namespace TerrariaInjector
{
    // Keep this in the launcher: mod shutdown runs before XNA reports device failures.
    internal static class CrashDiagnostics
    {
        private static string logPath;
        private static int firstChanceCount;
        [ThreadStatic] private static bool writing;

        internal static void Register(Assembly game, Harmony harmony)
        {
            logPath = Path.Combine(GM.CoreDir, "logs", "original-failure-" +
                DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Process.GetCurrentProcess().Id + ".log");
            var type = game.GetType("Terraria.Main");
            while (type != null && type.FullName != "Microsoft.Xna.Framework.Game") type = type.BaseType;
            var target = type?.GetMethod("ShowMissingRequirementMessage", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(Exception) }, null);
            if (target == null) throw new MissingMethodException("XNA Game.ShowMissingRequirementMessage");
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(CrashDiagnostics), nameof(ForwardOriginalFailure)));
            AppDomain.CurrentDomain.FirstChanceException += OnFirstChance;
            GM.Logger.Info("Original device/audio failure diagnostics enabled; preserve Terraria error reporting.");
        }

        private static void OnFirstChance(object sender, FirstChanceExceptionEventArgs args)
        {
            if (writing || !IsDeviceFailure(args.Exception)) return;
            if (Interlocked.Increment(ref firstChanceCount) <= 16) Record("first throw", args.Exception);
        }

        private static bool IsDeviceFailure(Exception error)
        {
            var name = error?.GetType().FullName;
            return name == "Microsoft.Xna.Framework.Graphics.NoSuitableGraphicsDeviceException" ||
                name == "Microsoft.Xna.Framework.Audio.NoAudioHardwareException" ||
                error is OutOfMemoryException;
        }

        private static bool ForwardOriginalFailure(Exception __0, ref bool __result)
        {
            Record("XNA fatal failure forwarded to Terraria", __0);
            // false makes XNA rethrow the ORIGINAL exception into Terraria.Program's
            // crash log and ownerless error dialog. Never reuse its disposed game Form.
            __result = false;
            return false;
        }

        private static void Record(string phase, Exception error)
        {
            if (writing) return;
            writing = true;
            try
            {
                using (var process = Process.GetCurrentProcess())
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(logPath));
                    File.AppendAllText(logPath, DateTime.Now.ToString("O") + " " + phase +
                        " privateBytes=" + process.PrivateMemorySize64 + " virtualBytes=" + process.VirtualMemorySize64 +
                        Environment.NewLine + error + Environment.NewLine + Environment.StackTrace + Environment.NewLine);
                }
            }
            catch { /* Error reporting must never replace the original failure. */ }
            finally { writing = false; }
        }
    }
}
