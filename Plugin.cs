using System;
using HarmonyLib;
using Lunaris;
using Lunaris.Config;
using UnityEngine;

namespace MapleCombatText
{
    public class MapleConfig
    {
        [ConfigSection("General")]
        [ConfigDescription("Replace Erenshor's damage numbers with MapleStory-style combat text. Turn off to get the vanilla numbers back.")]
        public bool Enabled = true;

        [ConfigSection("General")]
        [ConfigDescription("Show 'MISS' when a hit does 0 damage.")]
        public bool ShowMiss = true;

        [ConfigSection("General")]
        [ConfigDescription("Also multiply the size by Erenshor's own 'Damage Popup Size' option.")]
        public bool UseGameSizeOption = true;

        [ConfigSection("Look")]
        [ConfigDescription("Overall size of the numbers (1 = MapleStory-like size at 1080p).")]
        [ConfigRange(0.3f, 3f)]
        public float Scale = 1f;

        [ConfigSection("Look")]
        [ConfigDescription("Size of other people's hits (sims, pets) relative to yours, so your own damage stands out.")]
        [ConfigRange(0.2f, 1.5f)]
        public float OthersScale = 0.7f;

        [ConfigSection("Look")]
        [ConfigDescription("Opacity of other people's hits.")]
        [ConfigRange(0.1f, 1f)]
        public float OthersOpacity = 0.75f;

        [ConfigSection("Look")]
        [ConfigDescription("Damage taken by your group (sims) is shown in violet like your own damage taken, instead of orange.")]
        public bool AlliesUseTakenColor = true;

        [ConfigSection("Look")]
        [ConfigDescription("Use the MapleStory-style bubbly digits. Turn off for the original font-based numbers. Switches instantly.")]
        public bool MapleDigits = true;

        [ConfigSection("Look")]
        [ConfigDescription("Use a black outline around the numbers instead of the dark brown/red/purple tint of each colour.")]
        public bool BlackOutline = false;

        [ConfigSection("Look")]
        [ConfigDescription("Draw the spiky starburst behind critical hits (MapleStory skill hits do not have one).")]
        public bool CritBurst = false;

        [ConfigSection("Look")]
        [ConfigDescription("Your own skin folder inside plugins/MapleCombatText/skins/ (used when MapleDigits is off). Leave blank for the font-based look. Takes effect after restart.")]
        public string Skin = "";

        [ConfigSection("Animation")]
        [ConfigDescription("Seconds a number stays still before it starts floating away.")]
        [ConfigRange(0f, 2f)]
        public float HoldTime = 0.45f;

        [ConfigSection("Animation")]
        [ConfigDescription("Seconds the float-up-and-fade takes.")]
        [ConfigRange(0.1f, 3f)]
        public float FadeTime = 0.65f;

        [ConfigSection("Animation")]
        [ConfigDescription("How far (in 1080p pixels) numbers rise while fading.")]
        [ConfigRange(0f, 300f)]
        public float RiseDistance = 55f;

        [ConfigSection("Animation")]
        [ConfigDescription("Most lines that stack on one target at once (multi-hits stack upward like multi-line skills).")]
        [ConfigRange(1f, 15f)]
        public float MaxStack = 5f;

        [ConfigSection("Advanced")]
        [ConfigDescription("Canvas sort order. Lower draws under more of the game's UI.")]
        [ConfigRange(-100f, 100f)]
        public float SortOrder = -5f;
    }

    [LunarisPlugin("MapleCombatText", "1.0.0", "Adam", "MapleStory-style damage numbers")]
    public class MapleCombatTextPlugin : LunarisPlugin
    {
        internal static MapleCombatTextPlugin Instance;
        internal static MapleConfig Cfg = new MapleConfig();

        private IConfigHandle<MapleConfig> _cfgHandle;
        private Harmony _harmony;
        private bool _started;

        private void Start()
        {
            Instance = this;
            try
            {
                if (Config != null)
                {
                    _cfgHandle = Config.Register<MapleConfig>();
                    Cfg = _cfgHandle.Get() ?? Cfg;
                    _cfgHandle.OnAnyChanged += c => { if (c != null) Cfg = c; };
                }
            }
            catch (Exception e) { Warn("Config registration failed, using defaults: " + e.Message); }

            try
            {
                MapleText.Init(this);
                _harmony = new Harmony("adam.erenshor.maplecombattext");
                var target = AccessTools.Method(typeof(DmgPop), nameof(DmgPop.LoadInfo),
                    new[] { typeof(int), typeof(bool), typeof(GameData.DamageType), typeof(Transform) });
                _harmony.Patch(target, prefix: new HarmonyMethod(typeof(MapleCombatTextPlugin), nameof(LoadInfoPrefix)));
                var reduce = AccessTools.Method(typeof(Stats), nameof(Stats.ReduceHP),
                    new[] { typeof(int), typeof(GameData.DamageType), typeof(bool), typeof(bool) });
                if (reduce != null)
                    _harmony.Patch(reduce, prefix: new HarmonyMethod(typeof(MapleCombatTextPlugin), nameof(ReduceHPPrefix)));
                else
                    Warn("Stats.ReduceHP not found; all hits will be styled as yours.");
                _started = true;
                Info("MapleCombatText loaded.");
            }
            catch (Exception e) { Error("Failed to start: " + e); }
        }

        // Stats.ReduceHP(int dmg, DamageType type, bool involvesPlayer, bool isCritical) is the only
        // caller of Misc.GenPopup -> DmgPop.LoadInfo, so remember whether the player is involved.
        private static bool _involvesPlayer = true;
        private static void ReduceHPPrefix(bool __2) { _involvesPlayer = __2; }

        // DmgPop.LoadInfo(int dmg, bool crit, DamageType type, Transform target)
        private static bool LoadInfoPrefix(DmgPop __instance, int __0, bool __1, Transform __3)
        {
            if (!Cfg.Enabled || __3 == null) return true;
            try
            {
                bool mine = _involvesPlayer;
                _involvesPlayer = true;
                bool shown = MapleText.Spawn(__0, __1, __3, mine, __instance);
                if (!shown && __0 > 0) return true; // couldn't draw it ourselves, let vanilla show it
                if (__instance != null) __instance.gameObject.SetActive(false);
                return false;
            }
            catch (Exception e)
            {
                Error("Spawn failed: " + e);
                return true;
            }
        }

        private void LateUpdate()
        {
            if (_started) MapleText.Tick();
        }

        private void OnDestroy()
        {
            try { _harmony?.UnpatchSelf(); } catch { }
            MapleText.Shutdown();
        }

        internal static void Info(string s) { try { Instance?.Logging?.LogInfo("[MapleCombatText] " + s); } catch { } }
        internal static void Warn(string s) { try { Instance?.Logging?.LogWarning("[MapleCombatText] " + s); } catch { } }
        internal static void Error(string s) { try { Instance?.Logging?.LogError("[MapleCombatText] " + s); } catch { } }
    }
}
