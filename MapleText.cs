using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MapleCombatText
{
    internal enum Style { Normal = 0, Crit = 1, Taken = 2 }

    internal sealed class StyleDef
    {
        public Color Top, Bottom, Outline;
        public float Size;          // font size in 1080p canvas units
        public Material Mat;        // TMP material (outline/underlay baked in)
        public readonly SpriteSet Custom = new SpriteSet(); // from the Skin setting
        public readonly SpriteSet Maple = new SpriteSet();  // built-in bubbly digits (skins/Maple)

        // Bubbly digits win when MapleDigits is on; otherwise a custom Skin; otherwise null = font.
        private SpriteSet Cur => MapleCombatTextPlugin.Cfg.MapleDigits && Maple.Digits != null ? Maple : Custom;
        public Sprite[] CurDigits => Cur.CurDigits;
        public Sprite CurMiss => Cur.CurMiss;
    }

    internal sealed class SpriteSet
    {
        public Sprite[] Digits, DigitsBlack; // 0-9, plus black-outline variant from "<folder>Black"
        public Sprite Miss, MissBlack;

        public Sprite[] CurDigits => MapleCombatTextPlugin.Cfg.BlackOutline && DigitsBlack != null ? DigitsBlack : Digits;
        public Sprite CurMiss => MapleCombatTextPlugin.Cfg.BlackOutline && MissBlack != null ? MissBlack : Miss;
    }

    internal sealed class Popup
    {
        public RectTransform Root;
        public CanvasGroup Group;
        public TextMeshProUGUI Text;
        public RectTransform ImageRow;
        public readonly List<Image> Images = new List<Image>();
        public Image Burst;

        public bool Active;
        public float Age;
        public Vector3 World;
        public float StackOffset;
        public float PopScale;
        public float BaseAlpha = 1f;
        public int TargetId;
        public int Slot;
    }

    internal static class MapleText
    {
        private static MonoBehaviour _host;
        private static Canvas _canvas;
        private static RectTransform _canvasRT;
        private static bool _assetsReady;
        private static bool _appliedBlackOutline;
        private static TMP_FontAsset _font;
        private static Sprite _burstSprite;
        private static readonly StyleDef[] Styles = new StyleDef[3];
        private static readonly List<Popup> Pool = new List<Popup>();
        private static readonly StringBuilder Sb = new StringBuilder(128);

        private static MapleConfig Cfg => MapleCombatTextPlugin.Cfg;

        public static void Init(MonoBehaviour host)
        {
            _host = host;

            Styles[(int)Style.Normal] = new StyleDef
            {
                Top = new Color(1.00f, 0.94f, 0.38f), Bottom = new Color(1.00f, 0.50f, 0.00f),
                Outline = new Color(0.32f, 0.11f, 0.00f), Size = 30f
            };
            Styles[(int)Style.Crit] = new StyleDef
            {
                Top = new Color(1.00f, 0.66f, 0.74f), Bottom = new Color(0.96f, 0.04f, 0.24f),
                Outline = new Color(0.30f, 0.00f, 0.06f), Size = 36f
            };
            Styles[(int)Style.Taken] = new StyleDef
            {
                Top = new Color(0.90f, 0.75f, 1.00f), Bottom = new Color(0.56f, 0.22f, 0.96f),
                Outline = new Color(0.16f, 0.00f, 0.32f), Size = 30f
            };
        }

        // ---------------------------------------------------------------- setup

        private static void EnsureCanvas()
        {
            if (_canvas != null) return;
            var go = new GameObject("MapleCombatTextCanvas");
            Object.DontDestroyOnLoad(go);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = Mathf.RoundToInt(Cfg.SortOrder);
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;
            _canvasRT = (RectTransform)go.transform;
        }

        private static void EnsureAssets(DmgPop source)
        {
            if (_assetsReady) return;
            _assetsReady = true;

            if (source != null && source.Num != null) _font = source.Num.font;
            if (_font == null) _font = TMP_Settings.defaultFontAsset;

            foreach (var s in Styles) s.Mat = BuildMaterial(s);
            ApplyOutlineColors();
            _burstSprite = BuildBurstSprite();
            LoadSkin("Maple", true);
            LoadSkin(Cfg.Skin, false);
        }

        private static Material BuildMaterial(StyleDef s)
        {
            if (_font == null || _font.material == null) return null;
            var m = new Material(_font.material) { name = "MapleCombatText " + s.Size };
            SetF(m, "_FaceDilate", 0.18f);
            SetF(m, "_OutlineWidth", 0.30f);
            SetF(m, "_OutlineSoftness", 0.02f);
            if (m.HasProperty("_OutlineColor")) m.SetColor("_OutlineColor", s.Outline);
            m.EnableKeyword("OUTLINE_ON");
            m.EnableKeyword("UNDERLAY_ON");
            if (m.HasProperty("_UnderlayColor")) m.SetColor("_UnderlayColor", new Color(0, 0, 0, 0.55f));
            SetF(m, "_UnderlayOffsetX", 0.55f);
            SetF(m, "_UnderlayOffsetY", -0.65f);
            SetF(m, "_UnderlayDilate", 0.35f);
            SetF(m, "_UnderlaySoftness", 0.15f);
            return m;
        }

        // Re-tints every style's outline when the BlackOutline option is toggled (no restart needed).
        private static void ApplyOutlineColors()
        {
            _appliedBlackOutline = Cfg.BlackOutline;
            foreach (var s in Styles)
            {
                if (s.Mat == null || !s.Mat.HasProperty("_OutlineColor")) continue;
                s.Mat.SetColor("_OutlineColor", _appliedBlackOutline ? Color.black : s.Outline);
            }
        }

        private static void SetF(Material m, string prop, float v) { if (m.HasProperty(prop)) m.SetFloat(prop, v); }

        // A spiky orange "critical" starburst, drawn procedurally.
        private static Sprite BuildBurstSprite()
        {
            const int N = 160;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[N * N];
            const int spikes = 11;
            float c = (N - 1) * 0.5f, outer = N * 0.49f, inner = N * 0.30f, border = 4.5f;
            var core = new Color(1f, 1f, 0.72f);
            var mid = new Color(1f, 0.62f, 0.06f);
            var edge = new Color(0.92f, 0.14f, 0.04f);
            var line = new Color(0.30f, 0.03f, 0.00f);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = x - c, dy = y - c;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = (Mathf.Atan2(dy, dx) / (2 * Mathf.PI) + 1f) * spikes % 1f;
                    float tri = 1f - Mathf.Abs(a * 2f - 1f);
                    float R = Mathf.Lerp(inner, outer, tri * tri);
                    float alpha = Mathf.Clamp01(R - r + 0.5f);
                    Color col;
                    if (r > R - border) col = line;
                    else
                    {
                        float t = r / (R - border);
                        col = t < 0.45f ? Color.Lerp(core, mid, t / 0.45f) : Color.Lerp(mid, edge, (t - 0.45f) / 0.55f);
                    }
                    col.a = alpha;
                    px[y * N + x] = col;
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
        }

        // ---------------------------------------------------------------- custom skins

        // plugins/MapleCombatText/skins/<Skin>/normal_0..9.png, crit_0..9.png, taken_0..9.png,
        // normal_miss.png, taken_miss.png, crit_effect.png  (any subset)
        // If a sibling folder "<Skin>Black" exists it is used while BlackOutline is on.
        // Built-in "Maple" digits are embedded in the DLL (resources skins/Maple/*.png and skins/MapleBlack/*.png),
        // so the mod is a single file. A custom Skin is read from plugins/MapleCombatText/skins/<Skin>/.
        // If a sibling "<name>Black" set exists it is used while BlackOutline is on.
        private static void LoadSkin(string skin, bool builtIn)
        {
            if (string.IsNullOrWhiteSpace(skin)) return;
            skin = skin.Trim();
            Func<string, string, byte[]> read;
            string where;
            if (builtIn)
            {
                read = ReadResource;
                where = "embedded resources";
            }
            else
            {
                string root = Path.Combine(Path.Combine(Path.Combine(Path.GetDirectoryName(Application.dataPath), "plugins"), "MapleCombatText"), "skins");
                if (!Directory.Exists(Path.Combine(root, skin))) { MapleCombatTextPlugin.Warn("Skin folder not found: " + Path.Combine(root, skin)); return; }
                read = (folder, file) => { var f = Path.Combine(Path.Combine(root, folder), file); return File.Exists(f) ? File.ReadAllBytes(f) : null; };
                where = root;
            }

            string black = skin + "Black";
            bool hasBlack = false;
            string[] prefixes = { "normal", "crit", "taken" };
            for (int s = 0; s < 3; s++)
            {
                var set = builtIn ? Styles[s].Maple : Styles[s].Custom;
                set.Digits = LoadDigits(read, skin, prefixes[s]);
                set.Miss = LoadSprite(read(skin, $"{prefixes[s]}_miss.png"), skin + "/" + prefixes[s] + "_miss.png");
                set.DigitsBlack = LoadDigits(read, black, prefixes[s]);
                set.MissBlack = LoadSprite(read(black, $"{prefixes[s]}_miss.png"), black + "/" + prefixes[s] + "_miss.png");
                hasBlack |= set.DigitsBlack != null;
            }
            if (Styles[0].Maple.Digits == null && builtIn) MapleCombatTextPlugin.Warn("Built-in Maple digits missing from the DLL.");
            if (!builtIn)
            {
                var fx = LoadSprite(read(skin, "crit_effect.png"), skin + "/crit_effect.png");
                if (fx != null) _burstSprite = fx;
            }
            MapleCombatTextPlugin.Info("Loaded skin '" + skin + "' from " + where + (hasBlack ? " (+ black outline variant)" : ""));
        }

        private static byte[] ReadResource(string folder, string file)
        {
            using (var st = typeof(MapleText).Assembly.GetManifestResourceStream("skins/" + folder + "/" + file))
            {
                if (st == null) return null;
                var ms = new MemoryStream();
                st.CopyTo(ms);
                return ms.ToArray();
            }
        }

        private static Sprite[] LoadDigits(Func<string, string, byte[]> read, string folder, string prefix)
        {
            var digits = new Sprite[10];
            for (int d = 0; d < 10; d++)
                if ((digits[d] = LoadSprite(read(folder, $"{prefix}_{d}.png"), $"{folder}/{prefix}_{d}.png")) == null) return null;
            return digits;
        }

        private static Sprite LoadSprite(byte[] bytes, string name)
        {
            if (bytes == null) return null;
            try
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                if (!tex.LoadImage(bytes)) return null;
                return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0f), 100f);
            }
            catch (Exception e) { MapleCombatTextPlugin.Warn($"Bad skin image {name}: {e.Message}"); return null; }
        }

        // ---------------------------------------------------------------- spawning

        public static bool Spawn(int dmg, bool crit, Transform target, bool mine, DmgPop source = null)
        {
            if (dmg <= 0 && !Cfg.ShowMiss) return false;
            EnsureCanvas();
            EnsureAssets(source);
            if (_font == null) return false;

            bool taken = target.name == "Player" ||
                         (Cfg.AlliesUseTakenColor && target.GetComponent<SimPlayer>() != null);
            var style = taken ? Style.Taken : (crit && dmg > 0 ? Style.Crit : Style.Normal);
            var def = Styles[(int)style];

            float size = def.Size * Cfg.Scale;
            if (Cfg.UseGameSizeOption) size *= Mathf.Clamp(GameData.DmgPopSize, 0.25f, 4f);
            bool others = !mine && target.name != "Player";
            if (others) size *= Cfg.OthersScale;

            var p = Get();
            p.Active = true;
            p.Age = 0f;
            p.World = HeadPosition(target);
            p.PopScale = style == Style.Crit ? 1.75f : 1.45f;
            p.BaseAlpha = others ? Cfg.OthersOpacity : 1f;
            p.TargetId = target.GetInstanceID();
            p.Slot = NextSlot(p, p.TargetId);
            p.StackOffset = p.Slot * size * 1.05f;
            p.Root.SetAsLastSibling();

            string digits = dmg > 0 ? dmg.ToString() : null;
            bool useImages = digits != null ? def.CurDigits != null : def.CurMiss != null;
            float firstDigitCenterX, firstDigitHeight;

            if (useImages)
            {
                p.Text.gameObject.SetActive(false);
                p.ImageRow.gameObject.SetActive(true);
                LayoutImages(p, def, digits, size, out firstDigitCenterX, out firstDigitHeight);
            }
            else
            {
                p.ImageRow.gameObject.SetActive(false);
                p.Text.gameObject.SetActive(true);
                LayoutText(p, def, digits, size, out firstDigitCenterX, out firstDigitHeight);
            }

            bool burst = style == Style.Crit && _burstSprite != null && Cfg.CritBurst;
            p.Burst.gameObject.SetActive(burst);
            if (burst)
            {
                float bs = firstDigitHeight * 2.1f;
                p.Burst.sprite = _burstSprite;
                p.Burst.rectTransform.sizeDelta = new Vector2(bs, bs);
                p.Burst.rectTransform.anchoredPosition = new Vector2(firstDigitCenterX - size * 0.15f, firstDigitHeight * 0.55f);
                p.Burst.rectTransform.localEulerAngles = new Vector3(0, 0, UnityEngine.Random.Range(-15f, 15f));
            }

            Place(p, Cam());
            return true;
        }

        private static void LayoutText(Popup p, StyleDef def, string digits, float size, out float firstX, out float firstH)
        {
            var t = p.Text;
            t.font = _font;
            if (def.Mat != null) t.fontSharedMaterial = def.Mat;
            t.fontSize = size;
            t.enableVertexGradient = true;
            t.colorGradient = new VertexGradient(def.Top, def.Top, def.Bottom, def.Bottom);

            Sb.Length = 0;
            Sb.Append("<cspace=-0.07em>");
            if (digits == null)
            {
                Sb.Append("MISS");
            }
            else
            {
                // MapleStory look: first digit bigger, the rest zig-zag slightly up/down.
                for (int i = 0; i < digits.Length; i++)
                {
                    if (i == 0) Sb.Append("<size=122%>").Append(digits[0]).Append("</size>");
                    else if ((i & 1) == 1) Sb.Append("<voffset=0.07em>").Append(digits[i]).Append("</voffset>");
                    else Sb.Append(digits[i]);
                }
            }
            t.text = Sb.ToString();
            t.ForceMeshUpdate();

            firstX = 0f;
            firstH = size;
            var info = t.textInfo;
            if (info != null && info.characterCount > 0)
            {
                var ci = info.characterInfo[0];
                firstX = (ci.bottomLeft.x + ci.topRight.x) * 0.5f;
                firstH = Mathf.Max(1f, ci.topRight.y - ci.bottomLeft.y);
            }
        }

        private const float Overlap = 0.80f; // fraction of a digit's width before the next starts

        private static void LayoutImages(Popup p, StyleDef def, string digits, float size, out float firstX, out float firstH)
        {
            int count = digits == null ? 1 : digits.Length;
            while (p.Images.Count < count)
            {
                var go = new GameObject("d", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(p.ImageRow, false);
                var img = go.GetComponent<Image>();
                img.raycastTarget = false;
                img.rectTransform.pivot = new Vector2(0.5f, 0f);
                p.Images.Add(img);
            }

            float h = size * 1.05f;
            var widths = new float[count];
            var heights = new float[count];
            float total = 0f;
            for (int i = 0; i < count; i++)
            {
                var sp = digits == null ? def.CurMiss : def.CurDigits[digits[i] - '0'];
                var img = p.Images[i];
                img.sprite = sp;
                heights[i] = h * (i == 0 && digits != null && count > 1 ? 1.2f : 1f);
                widths[i] = heights[i] * sp.rect.width / Mathf.Max(1f, sp.rect.height);
                total += widths[i] * (i == count - 1 ? 1f : Overlap);
            }

            float x = -total * 0.5f;
            firstX = 0f; firstH = heights[0];
            for (int i = 0; i < p.Images.Count; i++)
            {
                var img = p.Images[i];
                bool on = i < count;
                img.gameObject.SetActive(on);
                if (!on) continue;
                var rt = img.rectTransform;
                rt.sizeDelta = new Vector2(widths[i], heights[i]);
                float cx = x + widths[i] * 0.5f;
                rt.anchoredPosition = new Vector2(cx, (i & 1) == 1 ? h * 0.09f : 0f);
                // MapleStory digits lean back and forth a little.
                rt.localEulerAngles = new Vector3(0f, 0f, digits == null ? 0f : ((i & 1) == 1 ? -5f : 4f));
                if (i == 0) firstX = cx;
                x += widths[i] * Overlap;
            }
        }

        // MapleStory-style stacking: a new line goes into the lowest slot on this target that isn't
        // taken by a number that is still holding still. Once older numbers float away their slot frees up.
        private static int NextSlot(Popup self, int targetId)
        {
            int max = Mathf.Clamp(Mathf.RoundToInt(Cfg.MaxStack), 1, 15);
            int used = 0;
            Popup oldest = null;
            foreach (var o in Pool)
            {
                if (o == self || !o.Active || o.TargetId != targetId || o.Age >= Cfg.HoldTime) continue;
                if (o.Slot < 32) used |= 1 << o.Slot;
                if (oldest == null || o.Age > oldest.Age) oldest = o;
            }
            for (int i = 0; i < max; i++) if ((used & (1 << i)) == 0) return i;
            if (oldest != null) { oldest.Age = Mathf.Max(oldest.Age, Cfg.HoldTime); return oldest.Slot; } // full: push oldest out
            return 0;
        }

        private static Vector3 HeadPosition(Transform t)
        {
            var col = t.GetComponentInChildren<Collider>();
            if (col != null && col.enabled)
            {
                var b = col.bounds;
                return new Vector3(b.center.x, b.max.y + 0.2f, b.center.z);
            }
            return t.position + Vector3.up * 2.0f;
        }

        private static Popup Get()
        {
            foreach (var p in Pool) if (!p.Active) return p;

            var root = new GameObject("MaplePopup", typeof(RectTransform), typeof(CanvasGroup));
            root.transform.SetParent(_canvasRT, false);
            var popup = new Popup
            {
                Root = (RectTransform)root.transform,
                Group = root.GetComponent<CanvasGroup>()
            };
            popup.Group.blocksRaycasts = false;
            popup.Group.interactable = false;
            popup.Root.sizeDelta = Vector2.zero;
            popup.Root.pivot = new Vector2(0.5f, 0f);

            var burstGo = new GameObject("Burst", typeof(RectTransform), typeof(Image));
            burstGo.transform.SetParent(popup.Root, false);
            popup.Burst = burstGo.GetComponent<Image>();
            popup.Burst.raycastTarget = false;

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(popup.Root, false);
            var t = textGo.AddComponent<TextMeshProUGUI>();
            t.raycastTarget = false;
            t.richText = true;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Overflow;
            t.alignment = TextAlignmentOptions.Bottom;
            t.color = Color.white;
            t.rectTransform.pivot = new Vector2(0.5f, 0f);
            t.rectTransform.sizeDelta = new Vector2(800f, 200f);
            t.rectTransform.anchoredPosition = Vector2.zero;
            popup.Text = t;

            var rowGo = new GameObject("Images", typeof(RectTransform));
            rowGo.transform.SetParent(popup.Root, false);
            popup.ImageRow = (RectTransform)rowGo.transform;

            Pool.Add(popup);
            return popup;
        }

        // ---------------------------------------------------------------- animation

        private static Camera Cam()
        {
            var cam = Camera.main;
            if (cam != null) return cam;
            if (GameData.CamControl != null) cam = GameData.CamControl.GetComponentInChildren<Camera>();
            return cam;
        }

        public static void Tick()
        {
            if (_canvas == null) return;
            _canvas.sortingOrder = Mathf.RoundToInt(Cfg.SortOrder);
            if (_assetsReady && Cfg.BlackOutline != _appliedBlackOutline) ApplyOutlineColors();
            var cam = Cam();
            float dt = Time.unscaledDeltaTime;
            float hold = Cfg.HoldTime, fade = Mathf.Max(0.05f, Cfg.FadeTime);

            for (int i = 0; i < Pool.Count; i++)
            {
                var p = Pool[i];
                if (!p.Active) continue;
                p.Age += dt;
                if (p.Age >= hold + fade) { Recycle(p); continue; }
                Place(p, cam);
            }
        }

        private static void Place(Popup p, Camera cam)
        {
            if (!p.Root.gameObject.activeSelf) p.Root.gameObject.SetActive(true);
            if (cam == null) { p.Group.alpha = 0f; return; }

            Vector3 sp = cam.WorldToScreenPoint(p.World);
            if (sp.z <= 0.05f) { p.Group.alpha = 0f; return; }
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRT, sp, null, out Vector2 local);

            const float popTime = 0.08f;
            float hold = Cfg.HoldTime, fade = Mathf.Max(0.05f, Cfg.FadeTime);
            float t = p.Age, scale, alpha, rise;

            if (t < popTime)
            {
                float u = t / popTime;
                scale = Mathf.Lerp(p.PopScale, 1f, u * (2f - u));
                alpha = Mathf.Clamp01(u * 2.5f);
                rise = 0f;
            }
            else if (t < hold)
            {
                scale = 1f; alpha = 1f; rise = 0f;
            }
            else
            {
                float u = Mathf.Clamp01((t - hold) / fade);
                scale = 1f;
                alpha = 1f - u * u;
                rise = Cfg.RiseDistance * (1f - (1f - u) * (1f - u));
            }

            p.Group.alpha = alpha * p.BaseAlpha;
            p.Root.localScale = new Vector3(scale, scale, 1f);
            p.Root.anchoredPosition = local + new Vector2(0f, p.StackOffset + rise);
        }

        private static void Recycle(Popup p)
        {
            p.Active = false;
            p.Group.alpha = 0f;
            p.Root.gameObject.SetActive(false);
        }

        public static void Shutdown()
        {
            if (_canvas != null) Object.Destroy(_canvas.gameObject);
            _canvas = null;
            Pool.Clear();
        }
    }
}
