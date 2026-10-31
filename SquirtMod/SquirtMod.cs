using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(SquirtMod.SquirtMod), "Squirt Mod", "0.1.1", "siithan")]
[assembly: MelonGame("IncontinentCell", "My Dystopian Robot Girlfriend")]

namespace SquirtMod;

public class SquirtMod : MelonMod
{

    private void LogBrainPath()
    {
        try
        {
            var t = _brain.transform;
            string path = t.name;
            while (t.parent != null) { t = t.parent; path = t.name + " > " + path; }
            LoggerInstance.Msg("Brain path: " + path);
        }
        catch (Exception e)
        {
            LoggerInstance.Warning("Brain path failed: " + e.Message);
        }
    }
    private Il2Cpp.ModelBrain _brain;
    private bool _wasCumming;

    public override void OnUpdate()
    {
        if (_brain == null)
        {
            if (!Il2Cpp.ModelBrain.TryGet("bot", out _brain)) return;
            LoggerInstance.Msg("Found ModelBrain");
        }

        SquirtEffect.Tick(_brain);
        InsertionDetector.Tick(_brain);
        DripEffect.Tick();

        bool isCumming = _brain.ConnectedController?.Expression?.IsCumming ?? false;

        if (isCumming && !_wasCumming)
        {
            LoggerInstance.Msg($"Cumming! Pleasure: {_brain.Pleasure}");
            LogBrainPath();
            if (InsertionDetector.ShouldSquirt(_brain, out var why))
            {
                LoggerInstance.Msg($"[Squirt] Squirting: {why}");
                SquirtEffect.Spawn(_brain);
            }
            else
            {
                LoggerInstance.Msg($"[Squirt] Skipped: {why}");
            }
        }
        _wasCumming = isCumming;
    }
}