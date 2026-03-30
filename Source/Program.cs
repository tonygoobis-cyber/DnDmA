using ImGuiNET;
using System.Diagnostics;
using System.Runtime.InteropServices;
using static System.Net.Mime.MediaTypeNames;
using Vortice.Mathematics;

namespace DMAW_DND
{
    internal class Program
    {
        static void Main(string[] args)
        {
            ActivityLog.InitializeEarly();
            EnsureNativeGraphicsDlls();
            if (args is { Length: > 0 })
                ActivityLog.Info("Main", "Command-line args: " + string.Join(" ", args));
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                ActivityLog.Error("Runtime", $"UnhandledException isTerminating={e.IsTerminating}: {e.ExceptionObject}");
                ActivityLog.Shutdown();
            };
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                ActivityLog.Exception("Runtime", e.Exception, "UnobservedTaskException");
                e.SetObserved();
            };
            AppDomain.CurrentDomain.ProcessExit += (_, _) => ActivityLog.Shutdown();

            //Security.Authentication.Authenticate();
            //while (Security.Authentication._isAuthenticating)
            //{
            //    // infinite loop - waiting for authentication to complete
            //}
            //Thread.Sleep(100);
            //if (!Security.Authentication.IsAuthenticated())
            //{
            //    Program.Log($"[INIT] Authentication failed. Shutting down.");
            //    Shutdown();
            //}

            while (!Config.Ready) // Calling this will initialise the static class
            {
                Thread.Sleep(500);
            }

            ActivityLog.ConfigureFrom(Config.ActiveConfig);

            while (!Memory.Ready) // Calling this will initialise the static class
            {
                Thread.Sleep(5);
            };

            ActivityLog.Info("Main", "Creating radar window and entering run loop");
            RadarWindow window = new RadarWindow();
            //window.UpdateFrequency = 240f; // Frame limiter
            window.Run();
            while (true)
            {
                Thread.SpinWait(0);
            }
        }

        private static void EnsureNativeGraphicsDlls()
        {
            try
            {
                string baseDir = AppContext.BaseDirectory;
                string glfwDllPath = Path.Combine(baseDir, "glfw.dll");
                if (File.Exists(glfwDllPath))
                    return;

                string ridRuntimePath = Path.Combine(baseDir, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", "glfw3.dll");
                string winRuntimePath = Path.Combine(baseDir, "runtimes", "win-x64", "native", "glfw3.dll");
                string sourcePath = File.Exists(ridRuntimePath) ? ridRuntimePath : winRuntimePath;

                if (!File.Exists(sourcePath))
                {
                    ActivityLog.Warn("Main", $"GLFW runtime dependency not found in '{ridRuntimePath}' or '{winRuntimePath}'.");
                    return;
                }

                File.Copy(sourcePath, glfwDllPath, overwrite: true);
                ActivityLog.Info("Main", $"Copied GLFW dependency to '{glfwDllPath}'.");
            }
            catch (Exception ex)
            {
                ActivityLog.Exception("Main", ex, "EnsureNativeGraphicsDlls");
            }
        }

        /// <summary>Same as <see cref="LogInfo(string)"/> — writes at Info severity, category App.</summary>
        public static void Log(string message) => ActivityLog.Info("App", message);

        public static void Log(string category, string message) => ActivityLog.Info(category, message);

        public static void LogTrace(string message) => ActivityLog.Trace("App", message);
        public static void LogTrace(string category, string message) => ActivityLog.Trace(category, message);

        public static void LogDebug(string message) => ActivityLog.Debug("App", message);
        public static void LogDebug(string category, string message) => ActivityLog.Debug(category, message);

        public static void LogInfo(string message) => ActivityLog.Info("App", message);
        public static void LogInfo(string category, string message) => ActivityLog.Info(category, message);

        public static void LogWarning(string message) => ActivityLog.Warn("App", message);
        public static void LogWarning(string category, string message) => ActivityLog.Warn(category, message);

        public static void LogError(string message) => ActivityLog.Error("App", message);
        public static void LogError(string category, string message) => ActivityLog.Error(category, message);

        public static void LogCritical(string message) => ActivityLog.Critical("App", message);
        public static void LogCritical(string category, string message) => ActivityLog.Critical(category, message);

        public static void LogException(Exception ex, string? context = null) => ActivityLog.Exception("App", ex, context);
        public static void LogException(string category, Exception ex, string? context = null) => ActivityLog.Exception(category, ex, context);

        public static void Shutdown()
        {
            //if (Memory.Shutdown()) Console.WriteLine("[SHUTDOWN] Memory Shutdown");

            //KMBox.DisposeComPort();
            //Console.WriteLine("[SHUTDOWN] KMBox Shutdown");
            //if (_isEspRunning)
            //{
            //    Console.WriteLine("[SHUTDOWN] ESP Shutdown");
            //    renderClass.Close();
            //}

            //if(LootManager.Shutdown()) Console.WriteLine("[SHUTDOWN] Loot Manager Shutdown");
            Process.GetCurrentProcess().Kill();
        }
    }
}