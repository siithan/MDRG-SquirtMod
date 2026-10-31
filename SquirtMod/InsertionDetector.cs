using MelonLoader;
using UnityEngine;

namespace SquirtMod;

/// <summary>
/// Level 2: decides whether an orgasm should squirt, based on what's inside.
///
///   InsertionDetector.Tick(brain)               -> call every frame (remembers recent insertion)
///   InsertionDetector.ShouldSquirt(brain, out r) -> call at orgasm; r explains the decision
///   InsertionDetector.PenisInside(brain)         -> penis inside right now
///   InsertionDetector.IsDickOutside(brain)       -> cowgirl: penis shown and NOT inside (in front of her)
///
/// Per pose (from UnityExplorer):
///   missionary: VaginaCockFuckAxis, VaginaFingeringFuckAxis
///   doggy:      VaginaCockFuckAxis (no fingering)
///   cowgirl:    VaginaCockInsertionFuckAxis, VaginaFingeringFuckAxis
///               Its cock axis also turns on for fingers. But fingering is only possible
///               with the cock hidden, so: penis = cock axis on AND DickMain visible.
/// </summary>
public static class InsertionDetector
{
    // ---------------------------------------------------------------- tuning (live-editable)
    public static bool  RequireInsertion     = true;   // false = squirt on every orgasm
    public static bool  AllowFingers         = true;
    public static bool  AllowPenis           = true;
    public static bool  SquirtInUnknownPoses = false;  // poses not listed above (shower, blowjob...)
    public static float GraceSeconds         = 0.5f;   // still counts if pulled out this recently

    // ---------------------------------------------------------------- state
    private static float _lastFingersTime = -999f;
    private static float _lastPenisTime = -999f;

    private struct Reading
    {
        public string Pose;
        public bool Known;
        public bool Fingers;
        public bool Penis;
        public bool DickOut;   // penis shown and NOT inside: it's in front of her
    }

    // ---------------------------------------------------------------- public

    public static void Tick(Il2Cpp.ModelBrain brain)
    {
        try
        {
            var r = Read(brain);
            if (r.Fingers) _lastFingersTime = Time.time;
            if (r.Penis) _lastPenisTime = Time.time;
        }
        catch
        {
            // controller mid-rebuild during a pose change; try again next frame
        }
    }

    /// <summary>True if the penis is inside right now.</summary>
    public static bool PenisInside(Il2Cpp.ModelBrain brain)
    {
        try { return Read(brain).Penis; }
        catch { return false; }
    }

    /// <summary>True when the penis is shown and outside, i.e. in front of her. Cowgirl only.</summary>
    public static bool IsDickOutside(Il2Cpp.ModelBrain brain)
    {
        try { return Read(brain).DickOut; }
        catch { return false; }
    }

    public static bool ShouldSquirt(Il2Cpp.ModelBrain brain, out string reason)
    {
        Reading r;
        try { r = Read(brain); }
        catch (Exception e)
        {
            reason = $"detection failed ({e.Message})";
            return false;
        }

        bool fingers = r.Fingers || Time.time - _lastFingersTime <= GraceSeconds;
        bool penis   = r.Penis   || Time.time - _lastPenisTime   <= GraceSeconds;
        string state = $"pose {r.Pose}, fingers {fingers}, penis {penis}";

        if (!r.Known)
        {
            reason = $"unknown pose ({state}); SquirtInUnknownPoses = {SquirtInUnknownPoses}";
            return SquirtInUnknownPoses;
        }

        if (!RequireInsertion)
        {
            reason = $"insertion not required ({state})";
            return true;
        }

        reason = state;
        return (AllowFingers && fingers) || (AllowPenis && penis);
    }

    // ---------------------------------------------------------------- reading the game

    private static Reading Read(Il2Cpp.ModelBrain brain)
    {
        var r = new Reading { Pose = "none" };
        var ctrl = brain?.ConnectedController;
        if (ctrl == null) return r;

        Il2Cpp.InsertionHandlerFuckAxis cock = null, finger = null;
        bool cowgirl = false;

        // TryCast returns null unless the current controller is that pose's type
        var missionary = ctrl.TryCast<Il2Cpp.Live2DMissionaryFuckController>();
        if (missionary != null)
        {
            r.Pose = "missionary";
            cock = missionary.VaginaCockFuckAxis;
            finger = missionary.VaginaFingeringFuckAxis;
        }

        var doggy = ctrl.TryCast<Il2Cpp.Live2DDoggyFuckController>();
        if (doggy != null)
        {
            r.Pose = "doggy";
            cock = doggy.VaginaCockFuckAxis;
        }

        var cg = ctrl.TryCast<Il2Cpp.Live2DCowgirlController>();
        if (cg != null)
        {
            r.Pose = "cowgirl";
            cock = cg.VaginaCockInsertionFuckAxis;
            finger = cg.VaginaFingeringFuckAxis;
            cowgirl = true;
        }

        if (r.Pose == "none")
        {
            r.Pose = ControllerName(ctrl);
            return r;
        }

        r.Known = true;
        r.Fingers = finger != null && finger.IsInside;
        bool cockInside = cock != null && cock.IsInside;

        if (cowgirl)
        {
            // Fingering needs the cock hidden, so the cock axis only means "penis" while the penis is visible
            bool dickVisible = IsDrawableVisible(brain, "DickMain");
            r.Penis = cockInside && dickVisible;
            r.DickOut = dickVisible && !cockInside;
        }
        else
        {
            r.Penis = cockInside;
        }
        return r;
    }

    // A drawable counts as visible if it's active, enabled, and not faded out
    public static bool IsDrawableVisible(Il2Cpp.ModelBrain brain, string name)
    {
        var root = brain.transform.parent;
        var t = root?.Find("Drawables/" + name);
        if (t == null || !t.gameObject.activeInHierarchy) return false;

        var mr = t.GetComponent<MeshRenderer>();
        if (mr != null && !mr.enabled) return false;

        var cr = t.GetComponent<Il2CppLive2D.Cubism.Rendering.CubismRenderer>();
        return cr == null || cr.Opacity > 0.01f;
    }

    private static string ControllerName(Il2Cpp.Live2DController ctrl)
    {
        try { return ctrl.GetIl2CppType().Name; }
        catch { return "unknown"; }
    }
}
