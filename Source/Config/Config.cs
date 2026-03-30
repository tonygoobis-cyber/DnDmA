using Newtonsoft.Json;
using SharpGen.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace DMAW_DND
{
    internal static class Config
    {
        private static Thread _worker;

        private static bool _running = false;

        public static bool Ready { get; private set; } = false;

        public static ConfigStructure ActiveConfig { get; set; } = new ConfigStructure();

        static Config()
        {
            if(!ReadConfig())
            {
                WriteConfig();
            }

            _worker = new Thread(() => Worker())
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };
            _running = true;
            _worker.Start();
        }

        public static bool ReadConfig()
        {
            try{
                ActiveConfig = JsonConvert.DeserializeObject<ConfigStructure>(File.ReadAllText("./config.json"));

                if(ActiveConfig == null)
                {
                    Program.Log("Error reading config file. Generating default config.");
                    return false;
                }
                else
                {
                    Ready = true;
                    return true;
                }
            }
            catch
            {
                Program.Log("Error reading config file. Generating default config.");
                return false;
            }
        }

        public static void WriteConfig()
        {
            // Write config to file "./config.json"
            try{
                File.WriteAllText("./config.json", JsonConvert.SerializeObject(ActiveConfig, Formatting.Indented));
                ActiveConfig = JsonConvert.DeserializeObject<ConfigStructure>(File.ReadAllText("./config.json"));
                Ready = true;
            }
            catch
            {
                Program.Log("Error writing config file.");
            }
        }

        public static void Worker()
        {
            // Writes the config file every 30 seconds
            while(_running){
                Thread.Sleep(30000);
                Program.Log("Writing config file.");
                WriteConfig();
            }
        }
    
        public static bool GetBoolean(string key)
        {
            // return a reference to the boolean value in the config which can be modified
            return (bool)ActiveConfig.GetType().GetProperty(key).GetValue(ActiveConfig);
        }

        public static void SetBoolean(string key, bool value)
        {
            // Set the boolean value in the config
            var property = ActiveConfig.GetType().GetProperty(key);
            if (property == null) return;
            var current = (bool)property.GetValue(ActiveConfig);
            if (current == value) return;
            property.SetValue(ActiveConfig, value);
            Program.Log("Config value " + key + " set to " + value);
        }
        
        public static float GetFloat(string key)
        {
            // return a reference to the float value in the config which can be modified
            return (float)ActiveConfig.GetType().GetProperty(key).GetValue(ActiveConfig);
        }

        public static void SetFloat(string key, float value)
        {
            // Set the float value in the config
            var property = ActiveConfig.GetType().GetProperty(key);
            if (property == null) return;
            var current = (float)property.GetValue(ActiveConfig);
            if (MathF.Abs(current - value) < 0.001f) return;
            property.SetValue(ActiveConfig, value);
            Program.Log("Config value " + key + " set to " + value);
        }    
    }

    public class ConfigStructure()
    {
        [JsonProperty("ShowPOIs")]
        public bool ShowPOIs { get; set; } = false;

        [JsonProperty("ShowShrines")]
        public bool ShowShrines { get; set; } = false;

        [JsonProperty("ShowPortals")]
        public bool ShowPortals { get; set; } = false;

        [JsonProperty("ShowChests")]
        public bool ShowChests{ get; set; } = false;

        [JsonProperty("ShowMimics")]
        public bool ShowMimics { get; set; } = false;

        [JsonProperty("ShowBosses")]
        public bool ShowBosses { get; set; } = false;

        [JsonProperty("ShowMobs")]
        public bool ShowMobs { get; set; } = true;

        [JsonProperty("ShowLevers")]
        public bool ShowLevers { get; set; } = false;

        [JsonProperty("Show Ore")]
        public bool ShowOre { get; set; } = false;

        [JsonProperty("ShowSpecialItems")]
        public bool ShowSpecialItems { get; set; } = false;
        
        [JsonProperty("ShowAimlines")]
        public bool ShowAimlines { get; set; } = false;

        [JsonProperty("AimlineLength")]
        public float AimlineLength { get; set; } = 50f;

        [JsonProperty("ShowTeamLines ")]
        public bool ShowTeamLines { get; set; } = true;

        [JsonProperty("ShowHeightIndicators")]
        public bool ShowHeightIndicators { get; set; } = true;

        [JsonProperty("UIScale")]
        public float UIScale { get; set; } = 1.5f;

        [JsonProperty("ActivityLoggingEnabled")]
        public bool ActivityLoggingEnabled { get; set; } = true;

        [JsonProperty("ActivityLogDirectory")]
        public string ActivityLogDirectory { get; set; } = "Logs";

        [JsonProperty("ActivityLogMinimumLevel")]
        public string ActivityLogMinimumLevel { get; set; } = "Trace";

        [JsonProperty("ActivityLogLogEveryFrame")]
        public bool ActivityLogLogEveryFrame { get; set; } = false;

        [JsonProperty("ActivityLogLogMouseMoves")]
        public bool ActivityLogLogMouseMoves { get; set; } = false;

        [JsonProperty("ActivityLogMirrorToConsoleInDebug")]
        public bool ActivityLogMirrorToConsoleInDebug { get; set; } = true;

        /// <summary>When true, mirror activity lines to the real console in Release builds too (Debug still uses MirrorToConsoleInDebug unless this is set).</summary>
        [JsonProperty("ActivityLogMirrorToConsole")]
        public bool ActivityLogMirrorToConsole { get; set; } = false;

        /// <summary>Periodic Info lines: player/mob counts, radar gates (InGame, ShowPOIs), and entity scan stats.</summary>
        [JsonProperty("ActivityLogRadarDiagnostics")]
        public bool ActivityLogRadarDiagnostics { get; set; } = true;
    }
}