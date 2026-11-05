using MreHud;

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

var cases = new List<(string name, Action test)>
{
    ("damage reflects only confirmed health loss", () => {
        Assert(DamageNumber.Calculate(100,78,true)==22,"player damage");
        Assert(DamageNumber.Calculate(100,100,true)==0 && DamageNumber.Calculate(100,101,true)==0,"cancelled or healed hit");
        Assert(DamageNumber.Calculate(9,0,false)==9,"lethal player hit");
        Assert(DamageNumber.Calculate(9,0,false)==9,"resource final hit");
    }),
    ("rapid hits use separate slots and reuse the oldest", () => {
        var slots = new DamageSlots(8); var start = new DateTime(2026, 1, 1);
        for (int i = 0; i < 8; i++) {
            Assert(slots.Add(start.AddMilliseconds(i * 10), TimeSpan.FromSeconds(1.2)) == i, "Hit shared a slot early");
            Assert(slots.IsVisible(i, start.AddSeconds(1)), "Hit expired early");
        }
        Assert(slots.Add(start.AddMilliseconds(80), TimeSpan.FromSeconds(1.2)) == 0, "Ninth hit did not replace oldest");
        Assert(slots.IsVisible(0, start.AddMilliseconds(1250)) && !slots.IsVisible(1, start.AddMilliseconds(1250)), "Reused slot did not get its own expiry");
        slots.Clear(); Assert(!slots.IsVisible(0, start.AddMilliseconds(100)) && slots.Add(start, TimeSpan.FromSeconds(1.2)) == 0, "Clear did not reset slots");
    }),
    ("nested damage only invalidates the same target", () => {
        var versions = new DamageTargetSequence(); var a = new object(); var b = new object();
        long outer = versions.Begin(a); long other = versions.Begin(b);
        Assert(versions.IsCurrent(a, outer) && versions.IsCurrent(b, other), "Different target suppressed outer hit");
        long nested = versions.Begin(a);
        Assert(!versions.IsCurrent(a, outer) && versions.IsCurrent(a, nested), "Same target could be double-counted");
        Assert(versions.IsCurrent(b, other), "Same-target nesting affected another target");
    }),
    ("damage colors distinguish target and headshot", () => {
        Assert(DamageNumber.Color(DamageKind.Resource)=="#A0A0A0", "Resource must be gray");
        Assert(DamageNumber.Color(DamageKind.Building)=="#FFFFFF", "Building must be white");
        Assert(DamageNumber.Color(DamageKind.PlayerBody)=="#40E060", "Body must be green");
        Assert(DamageNumber.Color(DamageKind.PlayerHead)=="#FF4040", "Head must be red");
    }),
    ("meter starts at actual value and animates in both directions", () => {
        var meter = new MeterAnimation();
        Assert(meter.Advance(80, 0) == 80, "First value animated from zero");
        Assert(meter.Advance(20, 0.1) == 76, "Damage did not interpolate at four points per tick");
        Assert(meter.Advance(100, 0.1) == 80, "Changed target did not reverse interpolation");
        Assert(meter.Advance(100, 0.5) == 100, "Meter failed to converge");
        Assert(meter.Advance(100, 0.1) == 100, "Stable meter changed");
    }),
    ("meter reset snaps after respawn and clamps input", () => {
        var meter = new MeterAnimation(); meter.Advance(10, 0); meter.Advance(100, 0.1);
        meter.Reset(); Assert(meter.Advance(95, 0.1) == 95, "Respawn inherited old value");
        meter.Reset(); Assert(meter.Advance(200, 0) == 100, "Value was not clamped");
        Assert(meter.Advance(-1, 0) == 100, "Zero elapsed advanced meter");
        Assert(meter.Advance(-1, 1) == 60 && meter.Advance(-1, 1) == 20 && meter.Advance(-1, 1) == 0, "Negative target was not clamped");
    }),
    ("building hit accepts only live unarmed punch", () => {
        Assert(BuildingHitState.IsEligible(true,true,true),"Unarmed punch rejected");
        Assert(!BuildingHitState.IsEligible(false,true,true) && !BuildingHitState.IsEligible(true,false,true) && !BuildingHitState.IsEligible(true,true,false),"Other damage or dead/armed player accepted");
    }),
    ("building hit rejects invalid distance and target", () => {
        Assert(BuildingHitState.IsValidatedInput(36,true) && BuildingHitState.IsValidatedInput(0,true),"Game distance limit rejected");
        Assert(!BuildingHitState.IsValidatedInput(36.01f,true) && !BuildingHitState.IsValidatedInput(float.NaN,true) && !BuildingHitState.IsValidatedInput(float.PositiveInfinity,true) && !BuildingHitState.IsValidatedInput(1,false),"Invalid input accepted");
    }),
    ("building hit expires and reads current health", () => {
        var now=DateTime.UtcNow; int health=100;
        var hit=new BuildingHitState { Maximum=100, ReadHealth=()=>health, ExpiresUtc=now.AddSeconds(5) };
        health=73;
        Assert(hit.IsVisible(now,true) && hit.ReadHealth()==73,"Health is not read after damage");
        Assert(!hit.IsVisible(now.AddSeconds(5),true) && !hit.IsVisible(now,false),"Expired or dead player's hit remains visible");
        hit.Maximum=0; Assert(!hit.IsVisible(now,true),"Invalid maximum accepted");
    }),
    ("zero and full meters stay within their segments", () => {
        Assert(HudFormatting.FilledSegments(0,100,20)==0,"Empty meter has a filled segment");
        Assert(HudFormatting.FilledSegments(100,100,20)==20,"Full meter lost a segment");
        Assert(HudFormatting.FilledSegments(255,100,20)==20,"Overfull value exceeded the bar");
        Assert(HudFormatting.FilledSegments(-1,100,20)==0,"Negative value filled the bar");
    }),
    ("partial meter keeps positive health visible", () => {
        Assert(HudFormatting.FilledSegments(1,100,20)==1,"Critical positive health is rendered as empty");
        Assert(HudFormatting.FilledSegments(50,100,20)==10,"Half meter does not match scale");
        Assert(HudFormatting.FilledSegments(51,100,20)==11,"Partial segment is discarded");
    }),
    ("invalid meter dimensions do not divide by zero", () => {
        Assert(HudFormatting.FilledSegments(10,0,20)==0,"Zero maximum was not handled");
        Assert(HudFormatting.FilledSegments(10,-1,20)==0,"Negative maximum was not handled");
        Assert(HudFormatting.FilledSegments(10,100,0)==0,"Zero segments were not handled");
    }),
    ("missing ammo state differs from an empty gun", () => {
        Assert(HudFormatting.Ammo(null)==-1 && HudFormatting.Ammo(new byte[10])==-1,"Missing gun state is shown as zero ammo");
        Assert(HudFormatting.Ammo(new byte[11])==0,"Actual empty gun is not shown as zero");
    }),
    ("ammo reads actual byte and supports high capacity magazines", () => {
        var state=new byte[18]; state[10]=200;
        Assert(HudFormatting.Ammo(state)==200,"Ammo was narrowed, scaled or read from wrong offset");
    }),
    ("magazine requires both ID bytes and reads little endian", () => {
        Assert(HudFormatting.MagazineId(null)==-1 && HudFormatting.MagazineId(new byte[9])==-1,"Truncated magazine ID was read");
        Assert(HudFormatting.MagazineId(new byte[10])==0,"Absent magazine ID was not preserved");
        Assert(HudFormatting.MagazineName(-1,null)=="МАГАЗИН НЕИЗВЕСТЕН" && HudFormatting.MagazineName(0,null)=="МАГАЗИН НЕ УСТАНОВЛЕН" && HudFormatting.MagazineName(7,null)=="МАГАЗИН НЕИЗВЕСТЕН","Magazine states conflated");
        Assert(!HudFormatting.MagazineName(7,"<x>\n").Contains('<'),"Magazine markup not escaped");
        var state=new byte[10]; state[8]=0xcd; state[9]=0xab;
        Assert(HudFormatting.MagazineId(state)==0xabcd,"Two-byte magazine ID has wrong byte order");
    }),
    ("unnamed players have readable fallback", () => {
        Assert(HudFormatting.PlayerName(null)=="Игрок" && HudFormatting.PlayerName(" \r\n\t")=="Игрок","Blank player name hides card contents");
    }),
    ("player names cannot inject markup plugin hotkeys or newlines", () => {
        var name=HudFormatting.PlayerName("\u2028\u2029<color=red>A</color>\r\n\t<plugin_0/>");
        Assert(!name.Contains('<') && !name.Contains('>') && !name.Any(char.IsControl),"Player name can inject UI markup or line breaks");
        Assert(!name.Contains('\u2028') && !name.Contains('\u2029'),"Unicode line separators can change the player card layout");
    }),
    ("long and Cyrillic player names remain bounded and readable", () => {
        Assert(HudFormatting.PlayerName(new string('Я',100)).Length==32,"Player name exceeds card limit");
        Assert(HudFormatting.PlayerName("Иван Петров")=="Иван Петров","Normal Cyrillic name was changed");
    })
};

foreach (var (name,test) in cases)
{
    test();
    Console.WriteLine("PASS: "+name);
}
int voteCases=VoteUiChecks.Run();
Console.WriteLine($"Passed {cases.Count+voteCases} HUD checks ({cases.Count} formatting, {voteCases} vote UI). Unity rendering and client gameplay remain separate.");
