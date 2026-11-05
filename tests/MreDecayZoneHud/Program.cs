using System;
using MreDecayZoneHud;
using SDG.Unturned;

static class Program
{
    static void Main()
    {
        Check(!HintPolicy.Evaluate(false, HeldItem.HousingPlanner, true).Visible, "disconnected player");
        Check(!HintPolicy.Evaluate(true, HeldItem.Other, true).Visible, "ordinary tools");
        Check(!HintPolicy.Evaluate(true, HeldItem.Unknown, false).Visible, "unknown held item");
        foreach (HeldItem item in new[] { HeldItem.HousingPlanner, HeldItem.BuildingPart })
        {
            var fast = HintPolicy.Evaluate(true, item, true);
            var normal = HintPolicy.Evaluate(true, item, false);
            var unknown = HintPolicy.Evaluate(true, item, null);
            Check(fast.Visible && fast.Text.Contains("2%") && fast.Text.Contains("быстрее"), "accelerated zone");
            Check(normal.Visible && normal.Text.Contains("1%") && normal.Text.Contains("обычное"), "normal zone");
            Check(unknown.Visible && unknown.Text.Contains("не определена"), "unknown zone does not look normal");
        }
        Check(HeldItemClassifier.Classify(null) == HeldItem.Unknown, "missing asset");
        Check(HeldItemClassifier.Classify(new ItemAsset { useableType = typeof(UseableHousingPlanner) }) == HeldItem.HousingPlanner, "planner useable");
        Check(HeldItemClassifier.Classify(new ItemAsset { useableType = typeof(CustomHousingPlanner) }) == HeldItem.HousingPlanner, "derived planner useable");
        Check(HeldItemClassifier.Classify(new ItemStructureAsset()) == HeldItem.BuildingPart, "floor wall and ceiling");
        foreach (EBuild build in new[] { EBuild.DOOR, EBuild.GATE, EBuild.LADDER, EBuild.GLASS, EBuild.HATCH, EBuild.SHUTTER, EBuild.FORTIFICATION, EBuild.BARRICADE, EBuild.BARRICADE_WALL })
            Check(HeldItemClassifier.Classify(new ItemBarricadeAsset { build = build }) == HeldItem.BuildingPart, "building barricade " + build);
        foreach (EBuild build in new[] { EBuild.GENERATOR, EBuild.STORAGE, EBuild.FARM, EBuild.FREEFORM })
            Check(HeldItemClassifier.Classify(new ItemBarricadeAsset { build = build }) == HeldItem.Other, "nonbuilding barricade " + build);
        Check(HeldItemClassifier.Classify(new ItemAsset { useableType = typeof(UseableTool) }) == HeldItem.Other, "ordinary tool");
        var sink = new RecordingSink();
        var controller = new HintController(sink);
        controller.Update(1, true, false, HeldItem.HousingPlanner, true);
        Check(sink.Shows == 0 && sink.Hides == 0, "disabled mechanic hides hint");
        controller.Update(1, true, true, HeldItem.HousingPlanner, true);
        controller.Update(1, true, true, HeldItem.HousingPlanner, true);
        Check(sink.Shows == 1 && sink.Last.Contains("2%"), "unchanged state not repeated");
        controller.Update(1, true, true, HeldItem.HousingPlanner, false);
        Check(sink.Shows == 2 && sink.Last.Contains("1%"), "zone change refresh");
        controller.Update(1, true, true, HeldItem.HousingPlanner, null);
        Check(sink.Shows == 3 && sink.Last.Contains("не определена"), "lost zone changes visible status");
        controller.Update(1, true, true, HeldItem.Other, true);
        controller.Update(1, true, true, HeldItem.Other, true);
        Check(sink.Hides == 1, "unequip hides once");
        controller.Update(1, true, true, HeldItem.BuildingPart, true);
        controller.Update(2, true, true, HeldItem.HousingPlanner, false);
        controller.Remove(1);
        controller.Clear();
        Check(sink.Hides == 3, "disconnect and unload clear views");
        controller.Update(3, true, true, HeldItem.BuildingPart, true);
        controller.Update(3, true, false, HeldItem.BuildingPart, true);
        Check(sink.Hides == 4, "disabled mechanic clears existing hint");
        var faulty = new FaultSink();
        var guarded = new HintController(faulty);
        faulty.FailShowOnce = true;
        try { guarded.Update(7, true, true, HeldItem.BuildingPart, true); throw new Exception("show did not fail"); }
        catch (InvalidOperationException) { }
        guarded.Update(7, true, true, HeldItem.BuildingPart, true);
        Check(faulty.HideCalls.Contains(7) && faulty.ShowCalls == 2, "uncertain show cleaned before retry");
        guarded.Update(8, true, true, HeldItem.BuildingPart, true);
        faulty.FailHideId = 7;
        try { guarded.Clear(); throw new Exception("clear did not fail"); }
        catch (AggregateException) { }
        Check(faulty.HideCalls.Contains(8), "clear continues after hide failure");
        faulty.FailHideId = 0;
        int beforeRetry = faulty.HideCalls.Count;
        guarded.Clear();
        Check(faulty.HideCalls.Count == beforeRetry + 1 && faulty.HideCalls[faulty.HideCalls.Count - 1] == 7, "failed hide remains retryable");
        Console.WriteLine("PASS: decay zone hint policy");
    }
    static void Check(bool passed, string name) { if (!passed) throw new Exception(name); }
    sealed class RecordingSink : IHintSink
    {
        public int Shows, Hides;
        public string Last;
        public void Show(ulong playerId, string text) { Shows++; Last = text; }
        public void Hide(ulong playerId) { Hides++; }
    }
    sealed class FaultSink : IHintSink
    {
        public bool FailShowOnce;
        public ulong FailHideId;
        public int ShowCalls;
        public readonly System.Collections.Generic.List<ulong> HideCalls = new System.Collections.Generic.List<ulong>();
        public void Show(ulong id, string text)
        {
            ShowCalls++;
            if (FailShowOnce) { FailShowOnce = false; throw new InvalidOperationException("unknown send outcome"); }
        }
        public void Hide(ulong id)
        {
            HideCalls.Add(id);
            if (id == FailHideId) throw new InvalidOperationException("hide failed");
        }
    }
}
