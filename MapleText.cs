using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MapleCombatText
{
    internal enum Style { Normal = 0, Crit = 1, Taken = 2, Heal = 3 }

    internal sealed class StyleDef
    {
        public string Prefix;       // custom skin file prefix (normal/crit/taken/heal)
        public float Size;          // font size in 1080p canvas units
        public Material Mat;        // TMP material for the font look (outline/underlay baked in)
        public Color MatOutline;    // outline colour currently set on Mat
        public readonly SpriteSet Custom = new SpriteSet(); // from the Skin setting
    }

    // A user skin: one finished image per digit, drawn as-is.
    internal sealed class SpriteSet
    {
        public Sprite[] Digits, DigitsBlack; // 0-9, plus black-outline variant from "<Skin>Black"
        public Sprite Miss, MissBlack, Plus;

        public Sprite[] CurDigits => MapleCombatTextPlugin.Cfg.BlackOutline && DigitsBlack != null ? DigitsBlack : Digits;
        public Sprite CurMiss => MapleCombatTextPlugin.Cfg.BlackOutline && MissBlack != null ? MissBlack : Miss;
    }

    // The built-in bubbly glyphs: three layers each, so they can be recoloured to anything.
    internal sealed class MapleGlyphs
    {
        public const int Miss = 10, Plus = 11, Count = 12;
        public readonly Sprite[] Line = new Sprite[Count];  // white outline + black shadow, tinted with the outline colour
        public readonly Sprite[] Face = new Sprite[Count];  // light grey face, tinted with the colour gradient
        public readonly Sprite[] Gloss = new Sprite[Count]; // highlight/edge, untinted
        public bool Complete;
    }

    // One character in the image row: up to three stacked Images.
    internal sealed class Glyph
    {
        public RectTransform Rt;
        public Image Line, Face, Gloss;
        public VGradient Grad;
    }

    // Vertical colour gradient for a UI Image (multiplies the vertex colours).
    internal sealed class VGradient : BaseMeshEffect
    {
        public Color Top = Color.white, Bottom = Color.white;

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0) return;
            var v = new UIVertex();
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                min = Mathf.Min(min, v.position.y);
                max = Mathf.Max(max, v.position.y);
            }
            float span = Mathf.Max(0.0001f, max - min);
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                Color c = Color.Lerp(Bottom, Top, (v.position.y - min) / span) * (Color)v.color;
                v.color = c;
                vh.SetUIVertex(v, i);
            }
        }
    }

    internal sealed class Popup
    {
        public RectTransform Root;
        public CanvasGroup Group;
        public TextMeshProUGUI Text;
        public RectTransform ImageRow;
        public readonly List<Glyph> Glyphs = new List<Glyph>();
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
        private static Canvas _canvas;
        private static RectTransform _canvasRT;
        private static bool _assetsReady;
        private static TMP_FontAsset _font;
        private static Sprite _burstSprite;
        private static readonly MapleGlyphs Maple = new MapleGlyphs();
        private static readonly StyleDef[] Styles = new StyleDef[4];
        private static readonly List<Popup> Pool = new List<Popup>();
        private static readonly StringBuilder Sb = new StringBuilder(128);

        private static MapleConfig Cfg => MapleCombatTextPlugin.Cfg;

        public static void Init(MonoBehaviour host)
        {
            Styles[(int)Style.Normal] = new StyleDef { Prefix = "normal", Size = 30f };
            Styles[(int)Style.Crit] = new StyleDef { Prefix = "crit", Size = 36f };
            Styles[(int)Style.Taken] = new StyleDef { Prefix = "taken", Size = 30f };
            Styles[(int)Style.Heal] = new StyleDef { Prefix = "heal", Size = 30f };
        }

        // ---------------------------------------------------------------- colours

        private static void GetColors(Style s, out Color top, out Color bottom)
        {
            switch (s)
            {
                case Style.Crit: top = Cfg.CritTop; bottom = Cfg.CritBottom; break;
                case Style.Taken: top = Cfg.TakenTop; bottom = Cfg.TakenBottom; break;
                case Style.Heal: top = Cfg.HealTop; bottom = Cfg.HealBottom; break;
                default: top = Cfg.DamageTop; bottom = Cfg.DamageBottom; break;
            }
        }

        // Outline: black, or a very dark shade of the number's bottom colour.
        private static Color OutlineFor(Style s)
        {
            if (Cfg.BlackOutline) return Color.black;
            GetColors(s, out _, out var bottom);
            var c = Color.Lerp(bottom, Color.black, 0.72f);
            c.a = 1f;
            return c;
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

            // The game's damage-number font. A heal can come before any hit, so also look for the prefab.
            if (source == null)
                foreach (var d in Resources.FindObjectsOfTypeAll<DmgPop>())
                    if (d != null && d.Num != null && d.Num.font != null) { source = d; break; }
            if (source != null && source.Num != null) _font = source.Num.font;
            if (_font == null) _font = TMP_Settings.defaultFontAsset;

            for (int i = 0; i < Styles.Length; i++) Styles[i].Mat = BuildMaterial(Styles[i]);
            RefreshMaterials();
            _burstSprite = BuildBurstSprite();
            LoadMapleGlyphs();
            LoadSkin(Cfg.Skin);
        }

        private static Material BuildMaterial(StyleDef s)
        {
            if (_font == null || _font.material == null) return null;
            var m = new Material(_font.material) { name = "MapleCombatText " + s.Prefix };
            SetF(m, "_FaceDilate", 0.18f);
            SetF(m, "_OutlineWidth", 0.30f);
            SetF(m, "_OutlineSoftness", 0.02f);
            m.EnableKeyword("OUTLINE_ON");
            m.EnableKeyword("UNDERLAY_ON");
            if (m.HasProperty("_UnderlayColor")) m.SetColor("_UnderlayColor", new Color(0, 0, 0, 0.55f));
            SetF(m, "_UnderlayOffsetX", 0.55f);
            SetF(m, "_UnderlayOffsetY", -0.65f);
            SetF(m, "_UnderlayDilate", 0.35f);
            SetF(m, "_UnderlaySoftness", 0.15f);
            return m;
        }

        // Keeps the font look's outline colours in step with the settings (cheap; runs every frame).
        private static void RefreshMaterials()
        {
            for (int i = 0; i < Styles.Length; i++)
            {
                var s = Styles[i];
                if (s.Mat == null || !s.Mat.HasProperty("_OutlineColor")) continue;
                var want = OutlineFor((Style)i);
                if (want == s.MatOutline) continue;
                s.MatOutline = want;
                s.Mat.SetColor("_OutlineColor", want);
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

        // ---------------------------------------------------------------- built-in glyphs

        // Embedded resources maple/<g>_line.png, _face.png, _gloss.png for g = 0..9, miss, plus.
        private static void LoadMapleGlyphs()
        {
            bool ok = true;
            for (int g = 0; g < MapleGlyphs.Count; g++)
            {
                string name = g < 10 ? g.ToString() : g == MapleGlyphs.Miss ? "miss" : "plus";
                Maple.Line[g] = LoadSprite(ReadResource("maple/" + name + "_line.png"), name);
                Maple.Face[g] = LoadSprite(ReadResource("maple/" + name + "_face.png"), name);
                Maple.Gloss[g] = LoadSprite(ReadResource("maple/" + name + "_gloss.png"), name);
                ok &= Maple.Line[g] != null && Maple.Face[g] != null && Maple.Gloss[g] != null;
            }
            Maple.Complete = ok;
            if (!ok) MapleCombatTextPlugin.Warn("Built-in Maple glyphs missing from the DLL; using the font look.");
        }

        private static byte[] ReadResource(string name)
        {
            using (var st = typeof(MapleText).Assembly.GetManifestResourceStream(name))
            {
                if (st == null) return null;
                var ms = new MemoryStream();
                st.CopyTo(ms);
                return ms.ToArray();
            }
        }

        // ---------------------------------------------------------------- custom skins

        // plugins/MapleCombatText/skins/<Skin>/normal_0..9.png, crit_*, taken_*, heal_*, <prefix>_miss.png,
        // <prefix>_plus.png, crit_effect.png (any subset). A sibling "<Skin>Black" folder is used while BlackOutline is on.
        private static void LoadSkin(string skin)
        {
            if (string.IsNullOrWhiteSpace(skin)) return;
            skin = skin.Trim();
            string root = Path.Combine(Path.Combine(Path.Combine(Path.GetDirectoryName(Application.dataPath), "plugins"), "MapleCombatText"), "skins");
            string dir = Path.Combine(root, skin), blackDir = Path.Combine(root, skin + "Black");
            if (!Directory.Exists(dir)) { MapleCombatTextPlugin.Warn("Skin folder not found: " + dir); return; }

            foreach (var s in Styles)
            {
                var set = s.Custom;
                set.Digits = LoadDigits(dir, s.Prefix);
                set.Miss = LoadFile(dir, s.Prefix + "_miss.png");
                set.Plus = LoadFile(dir, s.Prefix + "_plus.png");
                set.DigitsBlack = LoadDigits(blackDir, s.Prefix);
                set.MissBlack = LoadFile(blackDir, s.Prefix + "_miss.png");
            }
            var fx = LoadFile(dir, "crit_effect.png");
            if (fx != null) _burstSprite = fx;
            MapleCombatTextPlugin.Info("Loaded skin '" + skin + "' from " + dir);
        }

        private static Sprite[] LoadDigits(string dir, string prefix)
        {
            var digits = new Sprite[10];
            for (int d = 0; d < 10; d++)
                if ((digits[d] = LoadFile(dir, $"{prefix}_{d}.png")) == null) return null;
            return digits;
        }

        private static Sprite LoadFile(string dir, string file)
        {
            var path = Path.Combine(dir, file);
            return File.Exists(path) ? LoadSprite(File.ReadAllBytes(path), path) : null;
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
            bool taken = target.name == "Player" ||
                         (Cfg.AlliesUseTakenColor && target.GetComponent<SimPlayer>() != null);
            var style = taken ? Style.Taken : (crit && dmg > 0 ? Style.Crit : Style.Normal);
            return Show(style, dmg > 0 ? dmg.ToString() : null, target, mine, source);
        }

        public static bool SpawnHeal(int amount, bool crit, Transform target, bool mine)
        {
            if (amount <= 0) return false;
            string s = amount.ToString();
            return Show(Style.Heal, Cfg.HealPlusSign ? "+" + s : s, target, mine, null, crit);
        }

        // text: digits (optionally starting with '+'), or null for MISS.
        private static bool Show(Style style, string text, Transform target, bool mine, DmgPop source, bool bigPop = false)
        {
            EnsureCanvas();
            EnsureAssets(source);
            var def = Styles[(int)style];

            float size = def.Size * Cfg.Scale;
            if (Cfg.UseGameSizeOption) size *= Mathf.Clamp(GameData.DmgPopSize, 0.25f, 4f);
            bool others = !mine && target.name != "Player";
            if (others) size *= Cfg.OthersScale;

            // Which look: built-in bubbly glyphs, a custom skin, or the game font.
            bool maple = Cfg.MapleDigits && Maple.Complete;
            bool custom = !maple && (text != null ? def.Custom.CurDigits != null : def.Custom.CurMiss != null);
            if (!maple && !custom && _font == null) return false;

            var p = Get();
            p.Active = true;
            p.Age = 0f;
            p.World = HeadPosition(target);
            p.PopScale = style == Style.Crit || bigPop ? 1.75f : 1.45f;
            p.BaseAlpha = others ? Cfg.OthersOpacity : 1f;
            p.TargetId = target.GetInstanceID();
            p.Slot = NextSlot(p, p.TargetId);
            p.StackOffset = p.Slot * size * 1.05f;
            p.Root.SetAsLastSibling();

            GetColors(style, out var top, out var bottom);
            float firstX, firstH;
            if (maple || custom)
            {
                p.Text.gameObject.SetActive(false);
                p.ImageRow.gameObject.SetActive(true);
                LayoutImages(p, def, text, size, maple, top, bottom, OutlineFor(style), out firstX, out firstH);
            }
            else
            {
                p.ImageRow.gameObject.SetActive(false);
                p.Text.gameObject.SetActive(true);
                LayoutText(p, def, text, size, top, bottom, out firstX, out firstH);
            }

            bool burst = style == Style.Crit && _burstSprite != null && Cfg.CritBurst;
            p.Burst.gameObject.SetActive(burst);
            if (burst)
            {
                float bs = firstH * 2.1f;
                p.Burst.sprite = _burstSprite;
                p.Burst.rectTransform.sizeDelta = new Vector2(bs, bs);
                p.Burst.rectTransform.anchoredPosition = new Vector2(firstX - size * 0.15f, firstH * 0.55f);
                p.Burst.rectTransform.localEulerAngles = new Vector3(0, 0, UnityEngine.Random.Range(-15f, 15f));
            }

            Place(p, Cam());
            return true;
        }

        private static void LayoutText(Popup p, StyleDef def, string text, float size, Color top, Color bottom, out float firstX, out float firstH)
        {
            var t = p.Text;
            t.font = _font;
            if (def.Mat != null) t.fontSharedMaterial = def.Mat;
            t.fontSize = size;
            t.enableVertexGradient = true;
            t.colorGradient = new VertexGradient(top, top, bottom, bottom);

            Sb.Length = 0;
            Sb.Append("<cspace=-0.07em>");
            if (text == null)
            {
                Sb.Append("MISS");
            }
            else
            {
                // MapleStory look: first digit bigger, the rest zig-zag slightly up/down.
                bool first = true;
                for (int i = 0; i < text.Length; i++)
                {
                    char ch = text[i];
                    if (ch == '+') { Sb.Append("<size=85%>+</size>"); continue; }
                    if (first) { Sb.Append("<size=122%>").Append(ch).Append("</size>"); first = false; }
                    else if ((i & 1) == 1) Sb.Append("<voffset=0.07em>").Append(ch).Append("</voffset>");
                    else Sb.Append(ch);
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

        private const float Overlap = 0.80f; // fraction of a glyph's width before the next starts

        private static void LayoutImages(Popup p, StyleDef def, string text, float size, bool maple,
            Color top, Color bottom, Color outline, out float firstX, out float firstH)
        {
            // Resolve each character to a glyph index (0-9, Miss, Plus); skip '+' if a custom skin has none.
            var idx = new List<int>(12);
            if (text == null) idx.Add(MapleGlyphs.Miss);
            else
                foreach (char ch in text)
                {
                    if (ch == '+') { if (maple || def.Custom.Plus != null) idx.Add(MapleGlyphs.Plus); }
                    else idx.Add(ch - '0');
                }

            int count = idx.Count;
            while (p.Glyphs.Count < count) p.Glyphs.Add(NewGlyph(p.ImageRow));

            int firstDigit = idx.FindIndex(g => g < 10);
            bool multi = count - (idx.Contains(MapleGlyphs.Plus) ? 1 : 0) > 1;
            float h = size * 1.05f;
            var widths = new float[count];
            var heights = new float[count];
            float total = 0f;
            for (int i = 0; i < count; i++)
            {
                int g = idx[i];
                Sprite face = maple ? Maple.Face[g]
                    : g == MapleGlyphs.Miss ? def.Custom.CurMiss
                    : g == MapleGlyphs.Plus ? def.Custom.Plus
                    : def.Custom.CurDigits[g];
                var gl = p.Glyphs[i];
                gl.Face.sprite = face;
                gl.Face.color = Color.white;
                gl.Grad.enabled = maple;
                gl.Line.gameObject.SetActive(maple);
                gl.Gloss.gameObject.SetActive(maple);
                if (maple)
                {
                    gl.Line.sprite = Maple.Line[g];
                    gl.Line.color = outline;
                    gl.Gloss.sprite = Maple.Gloss[g];
                    gl.Grad.Top = top;
                    gl.Grad.Bottom = bottom;
                    gl.Face.SetVerticesDirty();
                }

                float scale = g == MapleGlyphs.Plus ? 0.8f : (i == firstDigit && multi ? 1.2f : 1f);
                heights[i] = h * scale;
                widths[i] = heights[i] * face.rect.width / Mathf.Max(1f, face.rect.height);
                total += widths[i] * (i == count - 1 ? 1f : Overlap);
            }

            float x = -total * 0.5f;
            firstX = 0f; firstH = heights[Mathf.Max(0, firstDigit)];
            for (int i = 0; i < p.Glyphs.Count; i++)
            {
                var gl = p.Glyphs[i];
                bool on = i < count;
                gl.Rt.gameObject.SetActive(on);
                if (!on) continue;
                int g = idx[i];
                bool digit = g < 10;
                gl.Rt.sizeDelta = new Vector2(widths[i], heights[i]);
                float cx = x + widths[i] * 0.5f;
                float y = g == MapleGlyphs.Plus ? h * 0.12f : (digit && (i & 1) == 1 ? h * 0.09f : 0f);
                gl.Rt.anchoredPosition = new Vector2(cx, y);
                // MapleStory digits lean back and forth a little.
                gl.Rt.localEulerAngles = new Vector3(0f, 0f, digit ? ((i & 1) == 1 ? -5f : 4f) : 0f);
                if (i == firstDigit) firstX = cx;
                x += widths[i] * Overlap;
            }
        }

        private static Glyph NewGlyph(RectTransform row)
        {
            var go = new GameObject("g", typeof(RectTransform));
            go.transform.SetParent(row, false);
            var gl = new Glyph { Rt = (RectTransform)go.transform };
            gl.Rt.pivot = new Vector2(0.5f, 0f);
            gl.Line = NewLayer(gl.Rt, "line");
            gl.Face = NewLayer(gl.Rt, "face");
            gl.Gloss = NewLayer(gl.Rt, "gloss");
            gl.Grad = gl.Face.gameObject.AddComponent<VGradient>();
            return gl;
        }

        private static Image NewLayer(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            return img;
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
            if (_assetsReady) RefreshMaterials();
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
