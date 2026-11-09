using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Linq;
namespace TerrariaInjector
{
    internal static class FishingLoader
    {
        private static int worldFrames;
        private static bool attempted;
        private static FieldInfo gameMenu;
        internal static void Tick()
        {
            if (attempted) return;
            // Public bundle: native fishing is opt-in and absence is not an error.
            if (!File.Exists(Path.Combine(GM.AssemblyFolder, "TerrariaModder", "enable-native-fishing.flag"))) { attempted = true; return; }
            var game = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Terraria");
            if (game == null) return;
            if (gameMenu == null) gameMenu = game.GetType("Terraria.Main").GetField("gameMenu");
            if (gameMenu == null || (bool)gameMenu.GetValue(null)) { worldFrames = 0; return; }
            if (++worldFrames < 120) return;
            attempted = true;
            try
            {
                foreach (var method in game.GetType("Terraria.Player").GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).Where(m => m.Name == "ItemCheck" && !m.ContainsGenericParameters))
                    RuntimeHelpers.PrepareMethod(method.MethodHandle);
                Load();
            }
            catch (Exception ex) { GM.Logger.Error("Fishing plugin preparation failed", ex); }
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string path);
        internal static void Load()
        {
            try
            {
                string path = Path.Combine(GM.AssemblyFolder, "TerrariaModder", "native", "piscatio", "piscatio.dll");
                if (!File.Exists(path)) { GM.Logger.Info("Optional fishing component is not installed; skipped."); return; }
                var handle = LoadLibraryW(path);
                if (handle == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                GM.Logger.Info("Fishing plugin loaded: " + path);
            }
            catch (Exception ex) { GM.Logger.Error("Fishing plugin failed to load", ex); }
        }
    }
}
