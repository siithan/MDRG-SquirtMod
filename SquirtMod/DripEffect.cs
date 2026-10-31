using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

namespace SquirtMod;

/// <summary>
/// Lingering drips after the squirt: each drip appears at the bottom of the anchor,
/// stretches downward (only the neck stretches; cap and bulb keep their shape),
/// hangs for a moment, then fades out in place. No snapping, no falling.
///
///   DripEffect.Spawn(...)  -> called from SquirtEffect.Spawn, after the pulses
///   DripEffect.Tick()      -> call every frame
///   DripEffect.Test()      -> drips right now, callable from UnityExplorer (Evaluate)
///
/// Art: [game folder]\UserData\SquirtMod\drop_stretch.png
///   Measure it with Paint (y = 0 at the top edge) and set the three *Px values below.
/// </summary>
public static class DripEffect
{
    // ---------------------------------------------------------------- tuning (live-editable)
    public static bool     Enabled       = true;
    public static string[] DripPoses     = { "missionary", "cowgirl" };  // doggy's anchor is hidden behind the butt
    public static int      DripCount     = 3;
    public static float    StartSpread   = 0.6f;   // drips start at random moments within this window after the squirt
    public static float    DripWidth     = 0.33f;  // world units, before the pose's Scale
    public static float    SpreadX       = 0.25f;  // random sideways placement, as a fraction of the anchor's width
    public static float    AttachOffsetY = 0.04f;     // nudge the hanging point up (+) or down (-), world units
    public static float    GrowTime      = 1f;   // seconds to stretch to full length
    public static float    MaxStretch    = 1.3f;   // final visible length compared to the drawn length
    public static float    HoldTime      = 0.1f;   // seconds it hangs at full stretch before fading
    public static float    FadeTime      = 0.3f;   // seconds to fade out, staying in place
    public static float    TestScale     = 1f;     // Scale used by Test() (real drips use the pose's Scale)

    // drop_stretch.png measurements, in pixels from the image edges
    public static int TopBorderPx    = 121;  // top rows that never stretch: transparent space + cap (rows 0-120)
    public static int BottomBorderPx = 85;   // bottom rows that never stretch: the bulb (rows 171-255)
    public static int TopPaddingPx   = 91;   // transparent rows above the drip (rows 0-90). 0 if you crop it.

    // ---------------------------------------------------------------- state
    private enum Phase { Grow, Hold, Fade }

    private class Drip
    {
        public GameObject Go;
        public SpriteRenderer Sr;
        public MeshRenderer Anchor;
        public Vector3 Fallback;
        public Vector2 Offset;      // from the anchor's bottom-center
        public float Scale;
        public float Delay;
        public Phase Phase;
        public float T;
        public float Stretch = 1f;
        public MeshRenderer Behind;
        public float BehindDepth;
    }

    private static readonly List<Drip> _drips = new();
    private static Sprite _sprite;
    private static string _signature = "";
    private static bool _warnedMissing;

    private static string ArtPath =>
        Path.Combine(MelonEnvironment.ModsDirectory, "SquirtMod", "drop_stretch.png");

    // ---------------------------------------------------------------- spawning

    /// <summary>Starts drips. startDelay = seconds to wait first (e.g. the length of the squirt).</summary>
    public static void Spawn(string poseName, MeshRenderer anchor, Vector3 fallback,
                             string sortingLayer, float scale, float startDelay, float poseOffsetY = 0f,
                             MeshRenderer behind = null, float behindDepth = 0.0001f)
    {
        try
        {
            if (!Enabled || DripCount <= 0) return;

            string lower = poseName.ToLowerInvariant();
            if (!DripPoses.Any(p => lower.Contains(p.ToLowerInvariant()))) return;

            if (!EnsureSprite()) return;

            float anchorWidth = 0.5f;
            try { if (anchor != null) anchorWidth = anchor.bounds.size.x; } catch { }

            for (int i = 0; i < DripCount; i++)
            {
                float width = DripWidth * scale * UnityEngine.Random.Range(0.8f, 1.2f);

                var go = new GameObject("SquirtDrip");
                go.transform.localScale = new Vector3(width, width, 1f);   // sprite is 1 unit wide

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = _sprite;
                sr.drawMode = SpriteDrawMode.Sliced;      // stretch only the neck
                // Behind a part: same layer/order as it, so depth decides. Otherwise on top of everything.
                sr.sortingLayerName = behind != null ? behind.sortingLayerName : sortingLayer;
                sr.sortingOrder = behind != null ? behind.sortingOrder : 1000;
                sr.enabled = false;

                var d = new Drip
                {
                    Go = go,
                    Sr = sr,
                    Anchor = anchor,
                    Fallback = fallback,
                    Offset = new Vector2(UnityEngine.Random.Range(-SpreadX, SpreadX) * anchorWidth,
                                         AttachOffsetY * scale + poseOffsetY),
                    Scale = scale,
                    Delay = startDelay + UnityEngine.Random.Range(0f, StartSpread),
                    Phase = Phase.Grow,
                    Behind = behind,
                    BehindDepth = behindDepth,
                };
                SetStretch(d, 1f);
                SetAlpha(d, 0f);
                _drips.Add(d);
            }
        }
        catch (Exception e)
        {
            MelonLogger.Error($"[Squirt] Drip spawn failed: {e}");
        }
    }

    /// <summary>Drips right now at the current pose's anchor. Callable from UnityExplorer.</summary>
    public static void Test()
    {
        if (!Il2Cpp.ModelBrain.TryGet("bot", out var brain)) { MelonLogger.Warning("[Squirt] Drip test: no ModelBrain"); return; }
        var root = brain.transform.parent;
        if (root == null) return;

        string lower = root.name.ToLowerInvariant();
        MeshRenderer anchor = null;
        foreach (var p in SquirtEffect.Profiles)
        {
            if (string.IsNullOrEmpty(p.Match) || !lower.Contains(p.Match.ToLowerInvariant())) continue;
            foreach (var n in p.Anchors ?? Array.Empty<string>())
            {
                var t = root.Find("Drawables/" + n);
                var r = t != null ? t.GetComponent<MeshRenderer>() : null;
                if (r != null) { anchor = r; break; }
            }
            if (anchor != null) break;
        }

        Vector3 fallback = anchor != null ? anchor.bounds.center : root.position;
        string layer = anchor != null ? anchor.sortingLayerName : "Default";
        Spawn(root.name, anchor, fallback, layer, TestScale, 0f);
        MelonLogger.Msg($"[Squirt] Drip test in {root.name}");
    }

    // ---------------------------------------------------------------- per frame

    public static void Tick()
    {
        float dt = Time.deltaTime;
        for (int i = _drips.Count - 1; i >= 0; i--)
        {
            var d = _drips[i];
            try
            {
                if (d.Delay > 0f)
                {
                    d.Delay -= dt;
                    if (d.Delay > 0f) continue;
                    d.Sr.enabled = true;
                }

                d.T += dt;
                switch (d.Phase)
                {
                    case Phase.Grow:
                    {
                        float t = Mathf.Clamp01(d.T / Mathf.Max(GrowTime, 0.01f));
                        t = 1f - (1f - t) * (1f - t);                 // ease-out: quick at first, then slows
                        SetStretch(d, Mathf.Lerp(1f, MaxStretch, t));
                        Place(d);
                        SetAlpha(d, Mathf.Clamp01(d.T / 0.15f));      // quick fade-in
                        if (d.T >= GrowTime) { d.Phase = Phase.Hold; d.T = 0f; }
                        break;
                    }
                    case Phase.Hold:
                    {
                        Place(d);
                        if (d.T >= HoldTime) { d.Phase = Phase.Fade; d.T = 0f; }
                        break;
                    }
                    case Phase.Fade:
                    {
                        // Stays fully stretched and attached, just fades away
                        Place(d);
                        float t = Mathf.Clamp01(d.T / Mathf.Max(FadeTime, 0.01f));
                        SetAlpha(d, 1f - t);
                        if (d.T >= FadeTime)
                        {
                            UnityEngine.Object.Destroy(d.Go);
                            _drips.RemoveAt(i);
                        }
                        break;
                    }
                }
            }
            catch
            {
                _drips.RemoveAt(i);   // destroyed by a scene change etc.
            }
        }
    }

    // ---------------------------------------------------------------- helpers

    // Hang from the anchor's bottom-center (re-read every frame, so it follows her)
    private static void Place(Drip d)
    {
        Vector3 attach = d.Fallback;
        try
        {
            if (d.Anchor != null)
            {
                var b = d.Anchor.bounds;
                attach = new Vector3(b.center.x, b.min.y, b.center.z);
            }
        }
        catch { /* anchor destroyed */ }

        attach.x += d.Offset.x;
        attach.y += d.Offset.y;
        // Depth: behind the chosen part (larger z = farther back), or in front of her
        bool placed = false;
        try
        {
            if (d.Behind != null)
            {
                attach.z = d.Behind.bounds.center.z + d.BehindDepth;
                placed = true;
            }
        }
        catch { /* part destroyed */ }
        if (!placed) attach.z -= 1f;

        // The pivot is the image's top edge, so lift it by the transparent padding
        // to put the visible top of the drip exactly on the attach point
        float width = d.Go.transform.localScale.x;
        attach.y += TopPaddingPx / (float)_sprite.texture.width * width;

        d.Go.transform.position = attach;
    }

    // Sets the stretch; returns how much SHORTER it got in world units (negative if longer)
    private static float SetStretch(Drip d, float stretch)
    {
        var tex = _sprite.texture;
        float visiblePx = tex.height - TopPaddingPx;
        float heightPx = tex.height + visiblePx * (stretch - 1f);   // only the neck grows
        float heightUnits = heightPx / tex.width;

        float oldUnits = d.Sr.size.y;
        d.Sr.size = new Vector2(1f, heightUnits);
        d.Stretch = stretch;
        return (oldUnits - heightUnits) * d.Go.transform.localScale.y;
    }

    private static void SetAlpha(Drip d, float a)
    {
        var c = d.Sr.color;
        c.a = a;
        d.Sr.color = c;
    }

    // Loads drop_stretch.png as a 9-sliced sprite; reloads when the file or the *Px settings change
    private static bool EnsureSprite()
    {
        try
        {
            if (!File.Exists(ArtPath))
            {
                if (!_warnedMissing) MelonLogger.Msg($"[Squirt] No drip art, drips skipped. Put it at: {ArtPath}");
                _warnedMissing = true;
                return false;
            }
            _warnedMissing = false;

            string signature = $"{File.GetLastWriteTime(ArtPath).Ticks}|{TopBorderPx}|{BottomBorderPx}";
            if (_sprite != null && signature == _signature) return true;

            byte[] bytes = File.ReadAllBytes(ArtPath);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(tex, bytes))
            {
                MelonLogger.Warning("[Squirt] drop_stretch.png isn't a valid PNG");
                return false;
            }
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;

            int top = Mathf.Clamp(TopBorderPx, 0, tex.height - 1);
            int bottom = Mathf.Clamp(BottomBorderPx, 0, tex.height - 1 - top);
            var border = new Vector4(0f, bottom, 0f, top);   // left, bottom, right, top

            _sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                                    new Vector2(0.5f, 1f),     // pivot at the top edge: stretches downward
                                    tex.width, 0u, SpriteMeshType.FullRect, border);
            _sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
            _signature = signature;

            MelonLogger.Msg($"[Squirt] Loaded drip art {tex.width}x{tex.height}, border top {top} / bottom {bottom}");
            return true;
        }
        catch (Exception e)
        {
            MelonLogger.Warning($"[Squirt] Loading drip art failed: {e.Message}");
            return false;
        }
    }
}
