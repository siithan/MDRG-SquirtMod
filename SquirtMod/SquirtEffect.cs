using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

namespace SquirtMod;

public static class SquirtEffect
{
    // ---------------------------------------------------------------- per-pose settings
    public class PoseProfile
    {
        public string Match;              // matched against the model name, e.g. "doggy" in "bot_doggy(Clone)"
        public string[] Anchors;          // drawables to spawn from, tried in order
        public Vector2 AnchorOffset;      // nudge the spawn point (world units, x right / y up)
        public float AimDegrees;          // 90 = up the screen, 0 = right, 180 = left
        public float SpreadDegrees;
        public string BehindPart;         // drawable the drops are drawn BEHIND; empty = in front of everything
        public string DripBehindPart = "";
        public float DripBehindDepth = 0.0001f;
        public float BehindDepth;         // how far behind that part (world units along z)
        public float Scale = 1f;          // squirt multiplier per pose
        public float Lifetime = -1f;      // -1 = use global variable
        public float PulseFalloff = -1f;
        public float DripOffsetY;   // extra nudge for this pose's drips, world units (+ up, - down)
    }
    private static float Pick(float perPose, float global) => perPose >= 0f ? perPose : global;

    // Edit these live in UnityExplorer (open the list, inspect an entry), or here for permanent changes.
    public static List<PoseProfile> Profiles = new()
    {
        new PoseProfile { Match = "missionary", Anchors = new[] { "VaginaBase" },
            AimDegrees = 270f,
            SpreadDegrees = 8f,
            BehindPart = "MCLeftHandFingering",
            DripBehindPart = "MCLeftHandFingering",
            AnchorOffset = new Vector2(0f, 0.1f),
            Scale = 3f,
            Lifetime = 0.2f,
            DripOffsetY = 0.45f,
        },

        new PoseProfile { Match = "cowgirl", Anchors = new[] { "VaginaClit" },
            AimDegrees = 270f,
            SpreadDegrees = 8f,
            BehindPart = "",
            AnchorOffset = new Vector2(0f, -0.11f),
            BehindDepth = 0.0001f,
            Scale = 1.2f,
            DripOffsetY = 0.02f,
            Lifetime = 0.2f,
        },

        new PoseProfile { Match = "doggy", Anchors = new[] { "XrayVaginaBase", "XrayVaginaCover" },
            AimDegrees = 200f,
            SpreadDegrees = 1f,
            BehindPart = "SkinButt",
            BehindDepth = 0.0001f,
            AnchorOffset = new Vector2(-0.35f, 0.1f),
            Scale = 1f,
            Lifetime = 0.4f,
        },
    };

    // Used for any pose without a profile
    public static PoseProfile DefaultProfile = new PoseProfile
    {
        Match = "", Anchors = new[] { "VaginaBase", "VaginaClit" },
        AimDegrees = 90f, SpreadDegrees = 25f, BehindPart = ""
    };

    // ---------------------------------------------------------------- tuning
    // public static (not const) so UnityExplorer can edit them while the game runs.
    // Sizes and speeds are in world units (missionary's camera shows about 10.8 units top to bottom).

    // Pulses: ON for PulseOnTime, OFF for PulseOffTime, repeated PulseCount times
    public static int   PulseCount       = 4;
    public static float PulseOnTime      = 0.4f;
    public static float PulseOffTime     = 0.5f;
    public static int   DropsPerPulse    = 15;
    public static float PulseFalloff     = 0.6f;  // each pulse's speed = previous × this (1 = all equal)
    public static float DropCountFalloff = 0.6f;

    // Drops
    public static float DropSize         = 0.15f;
    public static float MinSpeed         = 3f;
    public static float MaxSpeed         = 7f;
    public static float Gravity          = 16f;
    public static float Lifetime         = 0.6f;
    public static float FadePortion      = 0.4f;
    public static bool  RotateToVelocity = true;
    
    // ---------------------------------------------------------------- state
    private class Drop
    {
        public GameObject Go;
        public SpriteRenderer Sr;
        public Vector3 Velocity;
        public float Age;
        public float Delay;
        public bool Released;
        public MeshRenderer Anchor;      // re-read at release, so drops follow her movement
        public MeshRenderer Behind;      // re-read at release, for depth
        public Vector3 Offset;
        public float BehindDepth;
        public Vector3 FallbackOrigin;
        public float Scale;
        public float Lifetime;
    }

    private static readonly List<Drop> _drops = new();
    private static readonly List<Sprite> _sprites = new();
    private static string _spriteSignature = "";
    private static bool _hotkeyBroken;

    private static string ArtFolder => Path.Combine(MelonEnvironment.UserDataDirectory, "SquirtMod");
    private static string DropPath  => Path.Combine(ArtFolder, "drop.png");

    // ---------------------------------------------------------------- per frame

    public static void Tick(Il2Cpp.ModelBrain brain)
    {
        if (!_hotkeyBroken)
        {
            try
            {
                if (Input.GetKeyDown(KeyCode.F8)) Spawn(brain);
            }
            catch (Exception e)
            {
                _hotkeyBroken = true;
                MelonLogger.Warning($"[Squirt] F8 test key unavailable ({e.Message}). Use SquirtEffect.Test in UnityExplorer instead.");
            }
        }

        float dt = Time.deltaTime;
        for (int i = _drops.Count - 1; i >= 0; i--)
        {
            var d = _drops[i];
            try
            {
                if (!d.Released)
                {
                    d.Delay -= dt;
                    if (d.Delay > 0f) continue;
                    Release(d);
                }

                d.Age += dt;
                if (d.Age >= d.Lifetime)
                {
                    UnityEngine.Object.Destroy(d.Go);
                    _drops.RemoveAt(i);
                    continue;
                }

                d.Velocity.y -= Gravity * dt * d.Scale;
                d.Go.transform.position += d.Velocity * dt;

                if (RotateToVelocity)
                {
                    float angle = Mathf.Atan2(d.Velocity.y, d.Velocity.x) * Mathf.Rad2Deg + 90f;
                    d.Go.transform.rotation = Quaternion.Euler(0f, 0f, angle);
                }

                var c = d.Sr.color;
                c.a = Mathf.Clamp01((d.Lifetime - d.Age) / (d.Lifetime * Mathf.Max(FadePortion, 0.01f)));
                d.Sr.color = c;
            }
            catch
            {
                _drops.RemoveAt(i);
            }
        }
    }

    private static void Release(Drop d)
    {
        Vector3 origin = d.FallbackOrigin;
        try { if (d.Anchor != null) origin = d.Anchor.bounds.center; }
        catch { /* anchor destroyed by a pose change */ }

        origin += d.Offset;

        // Depth: the camera is at z = -10, so a SMALLER z is in front and a LARGER z is behind
        bool placedBehind = false;
        try
        {
            if (d.Behind != null)
            {
                origin.z = d.Behind.bounds.center.z + d.BehindDepth;
                placedBehind = true;
            }
        }
        catch { /* part destroyed */ }

        if (!placedBehind) origin.z -= 1f;   // in front of her

        d.Go.transform.position = origin;
        d.Sr.enabled = true;
        d.Released = true;
    }

    // ---------------------------------------------------------------- spawning

    /// <summary>Test burst, callable from UnityExplorer. No arguments needed.</summary>
    public static void Test()
    {
        if (Il2Cpp.ModelBrain.TryGet("bot", out var brain)) Spawn(brain);
        else MelonLogger.Warning("[Squirt] Test: no ModelBrain found");
    }

    public static void Spawn(Il2Cpp.ModelBrain brain)
    {
        if (brain == null) return;
        try
        {
            var root = brain.transform.parent;
            if (root == null) return;
            EnsureSprites();

            var profile = ProfileFor(root.name);

            MeshRenderer anchor = null;
            string anchorUsed = "model origin";
            foreach (var n in profile.Anchors ?? Array.Empty<string>())
            {
                var r = FindDrawableRenderer(root, n);
                if (r != null) { anchor = r; anchorUsed = n; break; }
            }

            // Which part to draw behind. Cowgirl: behind the penis only while it's inside.
            string behindName = profile.BehindPart;
            if (profile.Match == "cowgirl")
                behindName = InsertionDetector.IsDickOutside(brain) ? "DickMain" : "";

            MeshRenderer behind = string.IsNullOrEmpty(behindName)
                ? null
                : FindDrawableRenderer(root, behindName);
            if (!string.IsNullOrEmpty(behindName) && behind == null)
                MelonLogger.Warning($"[Squirt] BehindPart '{behindName}' not found in {root.name}; drawing in front instead");

            Vector3 fallback = anchor != null ? anchor.bounds.center : root.position;

            // Sorting: in front of everything, or the SAME layer/order as the part we hide behind,
            // so depth (z) decides what covers what
            var sortRef = behind ?? anchor;
            string sortingLayer = sortRef != null ? sortRef.sortingLayerName : "Default";
            int sortingOrder = sortRef != null ? sortRef.sortingOrder : 0;
            if (behind == null) sortingOrder += 1000;

            MelonLogger.Msg($"[Squirt] {PulseCount} pulses in {root.name} | profile '{profile.Match}' | " +
                            $"anchor {anchorUsed} | behind {(behind != null ? behindName : "nothing (on top)")}");

            float lifetime = Pick(profile.Lifetime, Lifetime);
            float falloff = Pick(profile.PulseFalloff, PulseFalloff);

            float strength = 1f;
            float countStrength = 1f;

            for (int p = 0; p < PulseCount; p++)
            {
                float pulseStart = p * (PulseOnTime + PulseOffTime);
                int dropsThisPulse = Mathf.Max(1, Mathf.RoundToInt(DropsPerPulse * countStrength));

                for (int i = 0; i < dropsThisPulse; i++)
                {
                    var go = new GameObject("SquirtDrop");

                    float s = DropSize * profile.Scale * UnityEngine.Random.Range(0.6f, 1.3f);
                    go.transform.localScale = new Vector3(s, s, 1f);

                    var sr = go.AddComponent<SpriteRenderer>();
                    sr.sprite = _sprites[UnityEngine.Random.Range(0, _sprites.Count)];
                    sr.sortingLayerName = sortingLayer;
                    sr.sortingOrder = sortingOrder;
                    sr.enabled = false;

                    float angle = (profile.AimDegrees + UnityEngine.Random.Range(-profile.SpreadDegrees, profile.SpreadDegrees)) * Mathf.Deg2Rad;
                    float speed = UnityEngine.Random.Range(MinSpeed, MaxSpeed) * strength * profile.Scale;

                    _drops.Add(new Drop
                    {
                        Go = go,
                        Sr = sr,
                        Velocity = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * speed,
                        Delay = pulseStart + UnityEngine.Random.Range(0f, Mathf.Max(PulseOnTime, 0f)),
                        Anchor = anchor,
                        Behind = behind,
                        Offset = new Vector3(profile.AnchorOffset.x, profile.AnchorOffset.y, 0f),
                        BehindDepth = profile.BehindDepth,
                        FallbackOrigin = fallback,
                        Scale = profile.Scale,
                        Lifetime = lifetime,
                    });
                }

                strength *= falloff;
                countStrength *= DropCountFalloff;
            }
            // Drips start once the last pulse finishes
            float squirtLength = (PulseCount - 1) * (PulseOnTime + PulseOffTime) + PulseOnTime;

            MeshRenderer dripBehind = null;
            if (!string.IsNullOrEmpty(profile.DripBehindPart))
            {
                dripBehind = FindDrawableRenderer(root, profile.DripBehindPart);
                if (dripBehind == null)
                    MelonLogger.Warning($"[Squirt] DripBehindPart '{profile.DripBehindPart}' not found in {root.name}");
                else if (!InsertionDetector.IsDrawableVisible(brain, profile.DripBehindPart))
                    dripBehind = null;   // hand not shown: draw drips on top as usual
            }

            DripEffect.Spawn(root.name, anchor, fallback, sortingLayer, profile.Scale, squirtLength,
                             profile.DripOffsetY, dripBehind, profile.DripBehindDepth);
        }
        catch (Exception e)
        {
            MelonLogger.Error($"[Squirt] Spawn failed: {e}");
        }
    }

    private static PoseProfile ProfileFor(string modelName)
    {
        string lower = modelName.ToLowerInvariant();
        foreach (var p in Profiles)
            if (!string.IsNullOrEmpty(p.Match) && lower.Contains(p.Match.ToLowerInvariant()))
                return p;
        return DefaultProfile;
    }

    private static MeshRenderer FindDrawableRenderer(Transform root, string name)
    {
        var t = root.Find("Drawables/" + name);
        return t != null ? t.GetComponent<MeshRenderer>() : null;
    }

    // ---------------------------------------------------------------- investigation helper

    /// <summary>
    /// Logs every drawable whose name contains the text: where it's drawn, its depth (z), and sorting.
    /// Callable from UnityExplorer with an argument, e.g. LogDrawables("Xray") or LogDrawables("Ass").
    /// Larger z = farther from the camera = drawn behind.
    /// </summary>
    public static void LogDrawables(string contains)
    {
        try
        {
            if (!Il2Cpp.ModelBrain.TryGet("bot", out var brain)) { MelonLogger.Warning("[Squirt] No ModelBrain"); return; }
            var root = brain.transform.parent;
            var holder = root?.Find("Drawables");
            if (holder == null) { MelonLogger.Warning("[Squirt] No Drawables found"); return; }

            MelonLogger.Msg($"[Squirt] Drawables in {root.name} containing '{contains}':");
            int found = 0;
            for (int i = 0; i < holder.childCount; i++)
            {
                var child = holder.GetChild(i);
                if (!string.IsNullOrEmpty(contains) &&
                    child.name.IndexOf(contains, StringComparison.OrdinalIgnoreCase) < 0) continue;

                var mr = child.GetComponent<MeshRenderer>();
                if (mr == null) continue;
                var b = mr.bounds;
                MelonLogger.Msg($"  {child.name,-28} center ({b.center.x:F3}, {b.center.y:F3})  z {b.center.z:F5}  " +
                                $"size ({b.size.x:F2}, {b.size.y:F2})  order {mr.sortingOrder}");
                found++;
            }
            MelonLogger.Msg($"[Squirt] {found} drawable(s) listed");
        }
        catch (Exception e)
        {
            MelonLogger.Error($"[Squirt] LogDrawables failed: {e.Message}");
        }
    }

    // ---------------------------------------------------------------- art

    /// <summary>Loads every drop*.png (except stretch sprites), reloading if files were added, removed or saved over.</summary>
    private static void EnsureSprites()
    {
        try
        {
            Directory.CreateDirectory(ArtFolder);

            var files = Directory.GetFiles(ArtFolder, "drop*.png")
                .Where(f => Path.GetFileName(f).IndexOf("stretch", StringComparison.OrdinalIgnoreCase) < 0)
                .OrderBy(f => f)
                .ToArray();

            // A fingerprint of the file names and save times; unchanged means nothing to reload
            string signature = string.Join("|", files.Select(f => f + File.GetLastWriteTime(f).Ticks));
            if (_sprites.Count > 0 && signature == _spriteSignature) return;

            _sprites.Clear();
            foreach (var f in files)
            {
                var s = LoadSpriteFromPng(f);
                if (s != null)
                {
                    _sprites.Add(s);
                    MelonLogger.Msg($"[Squirt] Loaded art: {Path.GetFileName(f)} ({s.texture.width}x{s.texture.height})");
                }
                else
                {
                    MelonLogger.Warning($"[Squirt] Couldn't load {Path.GetFileName(f)} (not a real PNG?)");
                }
            }
            _spriteSignature = signature;

            if (files.Length == 0)
                MelonLogger.Msg($"[Squirt] No drop PNGs in {ArtFolder}, using the placeholder");
        }
        catch (Exception e)
        {
            MelonLogger.Warning($"[Squirt] Loading art failed: {e.Message}");
        }

        if (_sprites.Count == 0) _sprites.Add(MakeDropSprite());
    }

    private static Sprite LoadSpriteFromPng(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!ImageConversion.LoadImage(tex, bytes)) return null;

        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;
        return MakeSprite(tex);
    }

    private static Sprite MakeDropSprite()
    {
        const int N = 64;
        var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode = TextureWrapMode.Clamp;

        var body = new Color(0.78f, 0.92f, 1f);
        for (int y = 0; y < N; y++)
        {
            for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N * 2f - 1f;
                float dy = (y + 0.5f) / N * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                if (r >= 1f) { tex.SetPixel(x, y, new Color(body.r, body.g, body.b, 0f)); continue; }

                float fill = Mathf.Clamp01((1f - r) * 6f) * 0.5f;
                float rim  = Mathf.Clamp01(1f - Mathf.Abs(r - 0.85f) * 8f) * 0.35f;
                float hx = dx + 0.35f, hy = dy - 0.35f;
                float hl   = Mathf.Clamp01(1f - Mathf.Sqrt(hx * hx + hy * hy) * 5f);

                var c = Color.Lerp(body, Color.white, hl);
                c.a = Mathf.Clamp01(fill + rim + hl * 0.9f);
                tex.SetPixel(x, y, c);
            }
        }
        tex.Apply();
        return MakeSprite(tex);
    }

    private static Sprite MakeSprite(Texture2D tex)
    {
        tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
        var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width);
        sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return sprite;
    }
}
