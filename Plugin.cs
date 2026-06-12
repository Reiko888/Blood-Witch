using BepInEx;
using BepInEx.Logging;
using Dawn;
using Dawn.Utils;
using Dusk;
using HarmonyLib;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace BloodWitch
{
    [BepInPlugin(modGUID, modName, modVersion)]
    [BepInDependency(DawnLib.PLUGIN_GUID)]
    internal class Plugin : BaseUnityPlugin
    {
        public const string modGUID = "reiko888.BloodWitch";
        public const string modName = "Blood Witch";
        public const string modVersion = "1.0.0";

        public static Plugin Instance = null!;
        internal static new ManualLogSource Logger = null!;
        internal static readonly Harmony harmony = new Harmony(modGUID);
        internal static DuskMod mod = null!;

        private void Awake()
        {
            if (Instance == null)
                Instance = this;
            Logger = base.Logger;
            AssetBundle mainBundle = AssetBundleUtils.LoadBundle(Assembly.GetExecutingAssembly(), "bw_contentcontainer");
            //if (mainBundle != null)
            //{
            //    Logger.LogWarning("=== LIST OF ALL ASSETS IN BUNDLE ===");
            //    foreach (string assetName in mainBundle.GetAllAssetNames())
            //    {
            //        Logger.LogInfo(assetName);
            //    }
            //    Logger.LogWarning("====================================");
            //}
            mod = DuskMod.RegisterMod(this, mainBundle);
            mod.RegisterContentHandlers();
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Logger.LogInfo($"Plugin {modName} is loaded!");
        }
    }
}