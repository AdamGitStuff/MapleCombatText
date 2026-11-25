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
        [ConfigDescription("Show damage numbers (dealt and taken). Turn off to hide them completely.")]
        public bool ShowDamage = true;

        [ConfigSection("General")]
        [ConfigDescription("Hide the damage your party and raid members (sims) take, so the only damage-taken numbers are your own.")]
        public bool HideAllyDamageTaken = false;

        [ConfigSection("General")]
        [ConfigDescription("Show healing numbers when anyone is healed (spells, lifesteal, heal-over-time).")]
        public bool ShowHealing = true;

        [ConfigSection("General")]
        [ConfigDescription("Only show healing you did yourself (your heal spells, heal-over-time and lifesteal). Hides heals from your party, raid and anyone else, including heals they cast on you.")]
        public bool OnlyMyHealing = false;

        [ConfigSection("General")]
        [ConfigDescription("Put a '+' in front of healing numbers.")]
        public bool HealPlusSign = true;

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
        [ConfigDescription("Use a black outline around the numbers instead of a dark shade of each number's colour.")]
        public bool BlackOutline = false;

        [ConfigSection("Look")]
        [ConfigDescription("Draw the spiky starburst behind critical hits (MapleStory skill hits do not have one).")]
        public bool CritBurst = false;

        [ConfigSection("Look")]
        [ConfigDescription("Your own skin folder inside plugins/MapleCombatText/skins/ (used when MapleDigits is off). Leave blank for the font-based look. Takes effect after restart.")]
        public string Skin = "";

        // Each number is a top-to-bottom gradient between two colours.
        [ConfigSection("Colors")]
        [ConfigDescription("Your damage: colour at the top of the digits.")]
        public Color DamageTop = new Color(1.00f, 0.95f, 0.50f);

        [ConfigSection("Colors")]
        [ConfigDescription("Your damage: colour at the bottom of the digits.")]
        public Color DamageBottom = new Color(0.96f, 0.36f, 0.05f);

        [ConfigSection("Colors")]
        [ConfigDescription("Critical hits: colour at the top of the digits.")]
        public Color CritTop = new Color(1.00f, 0.76f, 0.89f);

        [ConfigSection("Colors")]
        [ConfigDescription("Critical hits: colour at the bottom of the digits.")]
        public Color CritBottom = new Color(0.86f, 0.08f, 0.45f);

        [ConfigSection("Colors")]
        [ConfigDescription("Damage taken: colour at the top of the digits.")]
        public Color TakenTop = new Color(0.91f, 0.80f, 1.00f);

        [ConfigSection("Colors")]
        [ConfigDescription("Damage taken: colour at the bottom of the digits.")]
        public Color TakenBottom = new Color(0.45f, 0.18f, 0.86f);

        [ConfigSection("Colors")]
        [ConfigDescription("Healing: colour at the top of the digits.")]
        public Color HealTop = new Color(0.80f, 1.00f, 0.66f);

        [ConfigSection("Colors")]
        [ConfigDescription("Healing: colour at the bottom of the digits.")]
        public Color HealBottom = new Color(0.12f, 0.70f, 0.20f);

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

    [LunarisPlugin("MapleCombatText", "1.1.0", "Adam", "MapleStory-style damage numbers")]
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
                PatchHeals();
                _started = true;
                Info("MapleCombatText loaded.");
            }
            catch (Exception e) { Error("Failed to start: " + e); }
        }

        // ---- healing. Erenshor has no heal popups, so we watch Stats.CurrentHP around its heal paths:
        // HealMe(int) / HealMe(Spell,int,bool,bool,Character) for direct heals and lifesteal, and
        // TickEffects for heal-over-time (which writes CurrentHP itself). Natural regen is not shown.
        private void PatchHeals()
        {
            var pre = new HarmonyMethod(typeof(MapleCombatTextPlugin), nameof(HealPrefix));
            int n = 0;
            foreach (var m in typeof(Stats).GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
            {
                if (m.Name == "HealMe")
                {
                    var post = m.GetParameters().Length == 5 ? nameof(HealSpellPostfix) : nameof(HealPostfix);
                    _harmony.Patch(m, prefix: pre, postfix: new HarmonyMethod(typeof(MapleCombatTextPlugin), post));
                    n++;
                }
                else if (m.Name == "TickEffects" && m.GetParameters().Length == 0)
                {
                    _harmony.Patch(m, prefix: new HarmonyMethod(typeof(MapleCombatTextPlugin), nameof(TickPrefix)),
                        postfix: new HarmonyMethod(typeof(MapleCombatTextPlugin), nameof(TickPostfix)));
                    n++;
                }
            }
            if (n == 0) Warn("No heal methods found; healing numbers disabled.");
        }

        private static int _tickDepth; // inside TickEffects: let its postfix report the net heal once

        private static void HealPrefix(Stats __instance, out int __state) { __state = __instance != null ? __instance.CurrentHP : 0; }

        // HealMe(int): lifesteal / caster self-heals, so the healed character is the one who did it.
        private static void HealPostfix(Stats __instance, int __state)
        {
            if (_tickDepth == 0 && __instance != null) ShowHeal(__instance, __state, false, IsPlayer(__instance.Myself) || __instance == GameData.PlayerStats);
        }

        // HealMe(Spell spell, int amt, bool isCrit, bool isMana, Character source)
        private static void HealSpellPostfix(Stats __instance, int __state, bool __2, Character __4)
        {
            if (_tickDepth == 0 && __instance != null)
                ShowHeal(__instance, __state, __2, __4 != null ? IsPlayer(__4) : __instance == GameData.PlayerStats);
        }

        private static void TickPrefix(Stats __instance, out int __state) { _tickDepth++; __state = __instance != null ? __instance.CurrentHP : 0; }

        private static void TickPostfix(Stats __instance, int __state)
        {
            _tickDepth = Mathf.Max(0, _tickDepth - 1);
            if (__instance != null) ShowHeal(__instance, __state, false, HasPlayerEffect(__instance));
        }

        private static bool IsPlayer(Character c)
        {
            if (c == null) return false;
            var ps = GameData.PlayerStats;
            return ps != null && (c == ps.Myself || c.gameObject == ps.gameObject);
        }

        // A heal-over-time tick counts as yours if one of the target's effects was cast by you.
        private static bool HasPlayerEffect(Stats st)
        {
            try
            {
                var fx = st.StatusEffects;
                if (fx == null) return false;
                foreach (var e in fx)
                    if (e != null && e.Effect != null && (e.fromPlayer || IsPlayer(e.Owner))) return true;
            }
            catch { }
            return false;
        }

        private static void ShowHeal(Stats st, int before, bool crit, bool byMe)
        {
            if (!Cfg.Enabled || !Cfg.ShowHealing || st == null) return;
            if (Cfg.OnlyMyHealing && !byMe) return;
            try
            {
                int healed = st.CurrentHP - before;
                if (healed <= 0) return;
                var target = st.transform;
                bool mine = byMe || st == GameData.PlayerStats; // others' heals use the smaller OthersScale look
                MapleText.SpawnHeal(healed, crit, target, mine);
            }
            catch (Exception e) { Error("Heal popup failed: " + e.Message); }
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
                if (!Cfg.ShowDamage || (Cfg.HideAllyDamageTaken && __3.name != "Player" && __3.GetComponent<SimPlayer>() != null))
                {
                    if (__instance != null) __instance.gameObject.SetActive(false);
                    return false;
                }
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
