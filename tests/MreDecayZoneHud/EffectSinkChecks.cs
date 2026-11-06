using System;
using System.Collections.Generic;
using MreDecayZoneHud;

static class EffectSinkChecks
{
    public static void Run()
    {
        var wire = new Wire();
        var controller = new HintController(new EffectHintSink(wire));
        controller.Update(1, true, true, HeldItem.BuildingPart, true);
        controller.Update(1, true, true, HeldItem.BuildingPart, false);
        Check(wire.Calls[0] == "create:1" && wire.Calls[1] == "text:1:DecayZoneText" &&
              wire.Calls[2] == "visible:1:DecayZonePanel" && wire.Creates == 1, "create once and update");
        controller.Update(1, true, true, HeldItem.Other, true);
        Check(wire.Calls[wire.Calls.Count - 1] == "clear:1", "hide clears effect");
        wire.FailCreate = true;
        try { controller.Update(2, true, true, HeldItem.BuildingPart, true); throw new Exception("create must fail"); }
        catch (InvalidOperationException) { }
        wire.FailCreate = false;
        controller.Update(2, true, true, HeldItem.BuildingPart, true);
        Check(wire.Calls.Contains("clear:2") && wire.Creates == 3, "unknown create cleared before retry");
        wire.FailClear = true;
        try { controller.Remove(2); throw new Exception("clear must fail"); }
        catch (InvalidOperationException) { }
        wire.FailClear = false;
        controller.Remove(2);
        Check(wire.Calls.FindAll(x => x == "clear:2").Count == 3, "failed clear retried");
        wire.FailText = true;
        try { controller.Update(3, true, true, HeldItem.BuildingPart, true); throw new Exception("text must fail"); }
        catch (InvalidOperationException) { }
        wire.FailText = false;
        controller.Update(3, true, true, HeldItem.BuildingPart, true);
        Check(wire.Calls.Contains("clear:3"), "partial text update cleaned before retry");
        var sessionsWire = new Wire();
        var sessionsSink = new EffectHintSink(sessionsWire);
        var sessionsController = new HintController(sessionsSink);
        sessionsController.Update(9, true, true, HeldItem.BuildingPart, true);
        sessionsController.ForgetDisconnected(9);
        sessionsSink.ForgetDisconnected(9);
        sessionsController.Update(9, true, true, HeldItem.BuildingPart, true);
        Check(sessionsWire.Creates == 2 && !sessionsWire.Calls.Contains("clear:9"), "new session recreates effect");
        bool available = true;
        bool Zone(double x, double z, out bool fast) { fast = true; if (!available) throw new InvalidOperationException(); return true; }
        var lifecycleWire = new Wire();
        var lifecycle = new HintSessions(new EffectHintSink(lifecycleWire), Zone);
        object oldSession = new object(), newSession = new object();
        lifecycle.Update(10, oldSession, HeldItem.BuildingPart, 0, 0);
        try { lifecycle.Update(10, newSession, HeldItem.BuildingPart, 0, 0); throw new Exception("session must be fenced"); }
        catch (InvalidOperationException) { }
        available = false;
        lifecycle.Update(10, oldSession, HeldItem.BuildingPart, 0, 0);
        Check(lifecycleWire.Calls.Contains("clear:10"), "failed zone lookup hides old hint");
        lifecycle.Disconnected(10, oldSession);
        available = true;
        lifecycle.Update(10, newSession, HeldItem.BuildingPart, 0, 0);
        Check(lifecycleWire.Creates == 2, "confirmed reconnect recreates hint");
        var guid = Guid.NewGuid();
        var player = new SDG.Unturned.Player { channel = new SDG.Unturned.Channel() };
        SDG.Unturned.Provider.clients.Add(new SDG.Unturned.SteamPlayer { player = player, playerID = new SDG.Unturned.PlayerId { steamID = new SDG.Unturned.SteamId { m_SteamID = 1 } } });
        SDG.Unturned.Assets.Current = new SDG.Unturned.EffectAsset { id = 42 };
        var unturned = new UnturnedEffectTransport(id => id == 1 ? player : null, 42, guid, 17);
        unturned.Create(1);
        Check(SDG.Unturned.EffectManager.Last == "create", "game transport creates matching asset");
        unturned.SetText(1, EffectHintSink.Label, "hello");
        Check(SDG.Unturned.EffectManager.Last == "text:DecayZoneText", "game transport sets text");
        unturned.SetVisible(1, EffectHintSink.Panel, true);
        Check(SDG.Unturned.EffectManager.Last == "visible:DecayZonePanel", "game transport sets visibility");
        unturned.Clear(1);
        Check(SDG.Unturned.EffectManager.Last == "clear", "game transport clears effect");
        SDG.Unturned.Assets.Current = new SDG.Unturned.EffectAsset { id = 43 };
        try { unturned.Create(1); throw new Exception("mismatch must fail"); }
        catch (InvalidOperationException) { }
        try { unturned.Clear(2); throw new Exception("missing player must fail"); }
        catch (InvalidOperationException) { }
        SDG.Unturned.Provider.clients.Clear();
        try { unturned.Clear(1); throw new Exception("stale player must fail"); }
        catch (InvalidOperationException) { }
        SDG.Unturned.Provider.clients.Add(new SDG.Unturned.SteamPlayer { player = player, playerID = new SDG.Unturned.PlayerId { steamID = new SDG.Unturned.SteamId { m_SteamID = 2 } } });
        try { unturned.Clear(1); throw new Exception("wrong player must fail"); }
        catch (InvalidOperationException) { }
        SDG.Unturned.Provider.clients.Clear();
        SDG.Unturned.Assets.Current = new SDG.Unturned.EffectAsset { id = 42 };
        var first = new SDG.Unturned.SteamPlayer { player = new SDG.Unturned.Player { channel = new SDG.Unturned.Channel(), equipment = new SDG.Unturned.Equipment { asset = new SDG.Unturned.ItemStructureAsset() } }, playerID = new SDG.Unturned.PlayerId { steamID = new SDG.Unturned.SteamId { m_SteamID = 11 } } };
        SDG.Unturned.Provider.clients.Add(first);
        int createsBefore = SDG.Unturned.EffectManager.Creates;
        bool ready = true;
        bool GameZone(double x, double z, out bool fast) { fast = true; return ready; }
        var adapter = new GameHintAdapter(GameZone, 42, guid, 17, e => throw new Exception("adapter error", e));
        adapter.Tick(); adapter.Tick();
        Check(SDG.Unturned.EffectManager.Creates == createsBefore + 1, "adapter emits once per session");
        ready = false; adapter.Tick();
        Check(SDG.Unturned.EffectManager.Last == "clear", "adapter hides on lost readiness");
        ready = true; adapter.Tick();
        SDG.Unturned.Provider.clients.Clear(); adapter.Tick();
        var second = new SDG.Unturned.SteamPlayer { player = new SDG.Unturned.Player { channel = new SDG.Unturned.Channel(), equipment = new SDG.Unturned.Equipment { asset = new SDG.Unturned.ItemStructureAsset() } }, playerID = first.playerID };
        SDG.Unturned.Provider.clients.Add(second); adapter.Tick();
        Check(SDG.Unturned.EffectManager.Creates == createsBefore + 3, "adapter recreates after reconnect");
        var errors = new List<Exception>();
        SDG.Unturned.Provider.clients.Clear();
        var broken = new SDG.Unturned.SteamPlayer { player = new SDG.Unturned.Player { channel = new SDG.Unturned.Channel(), equipment = new SDG.Unturned.Equipment { asset = new SDG.Unturned.ItemStructureAsset() }, transform = null }, playerID = first.playerID };
        SDG.Unturned.Provider.clients.Add(broken);
        var robust = new GameHintAdapter(GameZone, 42, guid, 18, errors.Add);
        robust.Tick();
        Check(errors.Count == 1, "first tick error reported");
        SDG.Unturned.Provider.clients.Clear(); robust.Tick();
        SDG.Unturned.Provider.clients.Add(second); robust.Tick();
        Check(SDG.Unturned.EffectManager.Last == "visible:DecayZonePanel", "first tick error does not block reconnect");
        second.player.transform = null; robust.Tick();
        Check(SDG.Unturned.EffectManager.Last == "clear", "lost position hides existing hint");
        second.player.transform = new UnityEngine.Transform(); robust.Tick();
        SDG.Unturned.Provider.clients.Add(new SDG.Unturned.SteamPlayer { player = new SDG.Unturned.Player { channel = new SDG.Unturned.Channel() }, playerID = second.playerID });
        try { robust.Tick(); throw new Exception("duplicate player must fail"); }
        catch (InvalidOperationException) { }
        Check(SDG.Unturned.EffectManager.Last == "clear", "duplicate player causes best-effort clear");
    }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    sealed class Wire : IHintEffectTransport
    {
        public readonly List<string> Calls = new List<string>();
        public bool FailCreate, FailClear, FailText;
        public int Creates;
        public void Create(ulong playerId) { Calls.Add("create:" + playerId); Creates++; if (FailCreate) throw new InvalidOperationException(); }
        public void SetText(ulong playerId, string element, string text) { Calls.Add("text:" + playerId + ":" + element); if (FailText) throw new InvalidOperationException(); }
        public void SetVisible(ulong playerId, string element, bool visible) { Calls.Add("visible:" + playerId + ":" + element); }
        public void Clear(ulong playerId) { Calls.Add("clear:" + playerId); if (FailClear) throw new InvalidOperationException(); }
    }
}
