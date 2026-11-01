using MreEssentials;

internal static class VoteUiChecks
{
    private static void Assert(bool value, string message)
    {
        if (!value) throw new Exception(message);
    }

    public static int Run()
    {
        DateTime now=new(2026,10,30,12,0,0,DateTimeKind.Utc);
        ulong[] online={11,22,33};
        VoteSession NewVote() => new(online,60,now.AddSeconds(60));
        var cases=new List<(string name, Action test)>
        {
            ("vote UI hides when session does not exist",()=>{
                Assert(VoteUiSnapshot.Capture(null,online,11,"Иван",60,now)==null,"No session produced an active card");
            }),
            ("active vote snapshot contains topic initiator quorum and eligibility",()=>{
                var snapshot=VoteUiSnapshot.Capture(NewVote(),online,11,"Иван",60,now);
                Assert(snapshot.Phase=="active" && snapshot.Topic=="Наступление дня" && snapshot.InitiatorName=="Иван","Active card lost its context");
                Assert(snapshot.YesVotes==0 && snapshot.RequiredVotes==2 && snapshot.EligibleCount==3 && snapshot.CanVote && !snapshot.HasVoted,"Initial vote card has incorrect quorum or viewer eligibility");
            }),
            ("reading vote snapshots neither votes nor resolves session",()=>{
                var session=NewVote();
                for(int i=0;i<5;i++) VoteUiSnapshot.Capture(session,online,11,"Иван",60,now);
                Assert(!session.HasVoted(11) && session.CountOnlineVotes(online)==0 && session.Evaluate(online,now)==VoteOutcome.Active,"HUD read changed votes or outcome");
                session.TryVote(11,online,now);
                var snapshot=VoteUiSnapshot.Capture(session,online,11,"Иван",60,now);
                Assert(snapshot.HasVoted && !snapshot.CanVote && snapshot.YesVotes==1,"Voter was invited to vote a second time");
            }),
            ("late joined viewer cannot vote and does not change denominator",()=>{
                var snapshot=VoteUiSnapshot.Capture(NewVote(),new ulong[]{11,22,33,44},44,"Иван",60,now);
                Assert(!snapshot.CanVote && !snapshot.HasVoted && snapshot.RequiredVotes==2 && snapshot.EligibleCount==3,"Late join changed quorum or gained voting rights");
            }),
            ("disconnected eligible viewer cannot vote",()=>{
                var snapshot=VoteUiSnapshot.Capture(NewVote(),new ulong[]{22,33},11,"Иван",60,now);
                Assert(!snapshot.CanVote && !snapshot.HasVoted,"Offline viewer can vote in UI snapshot");
            }),
            ("departed voter's ballot disappears while quorum stays fixed",()=>{
                var session=NewVote(); session.TryVote(11,online,now);
                var snapshot=VoteUiSnapshot.Capture(session,new ulong[]{22,33},22,"Иван",60,now);
                Assert(snapshot.YesVotes==0 && snapshot.RequiredVotes==2 && snapshot.EligibleCount==3,"Departure kept stale ballot or lowered quorum");
            }),
            ("captured vote snapshot is immutable after another ballot",()=>{
                var session=NewVote(); session.TryVote(11,online,now);
                var old=VoteUiSnapshot.Capture(session,online,22,"Иван",60,now);
                session.TryVote(22,online,now);
                var current=VoteUiSnapshot.Capture(session,online,22,"Иван",60,now);
                Assert(old.YesVotes==1 && old.CanVote && current.YesVotes==2 && !current.CanVote,"Earlier UI snapshot mutated after vote");
            }),
            ("vote countdown clamps at expiry and stops offering a vote",()=>{
                var session=NewVote();
                var running=VoteUiSnapshot.Capture(session,online,22,"Иван",60,now.AddSeconds(10));
                var boundary=VoteUiSnapshot.Capture(session,online,22,"Иван",60,now.AddSeconds(60));
                var expired=VoteUiSnapshot.Capture(session,online,22,"Иван",60,now.AddSeconds(80));
                Assert(running.RemainingSeconds==50 && running.DurationSeconds==60,"Countdown disagrees with server deadline");
                Assert(boundary.RemainingSeconds==0 && !boundary.CanVote && expired.RemainingSeconds==0 && !expired.CanVote,"Expired card has negative time or offers voting");
            }),
            ("all final vote phases preserve result and remove voting action",()=>{
                var session=NewVote(); session.TryVote(11,online,now);
                var active=VoteUiSnapshot.Capture(session,online,11,"Иван",60,now);
                foreach(string phase in new[]{"passed","expired","canceled"}) {
                    var result=active.WithPhase(phase);
                    Assert(result.Phase==phase && result.RemainingSeconds==0 && !result.CanVote,"Final vote result still accepts a ballot");
                    Assert(result.YesVotes==1 && result.RequiredVotes==2 && result.EligibleCount==3 && result.HasVoted && result.InitiatorName=="Иван","Final phase lost result metadata");
                }
                Assert(active.Phase=="active" && active.RemainingSeconds==60,"Creating result mutated active snapshot");
            }),
            ("invalid final phase is rejected",()=>{
                var snapshot=VoteUiSnapshot.Capture(NewVote(),online,11,"Иван",60,now);
                try { snapshot.WithPhase("arbitrary"); throw new Exception("Invalid phase was accepted"); }
                catch(ArgumentOutOfRangeException) { }
            }),
            ("instant solo pass still creates visible final card",()=>{
                ulong[] solo={11}; var session=new VoteSession(solo,60,now.AddSeconds(60));
                session.TryVote(11,solo,now);
                Assert(session.Evaluate(solo,now)==VoteOutcome.Passed,"Solo vote did not pass immediately");
                var result=VoteUiSnapshot.Capture(session,solo,11,"Иван",60,now).WithPhase("passed");
                Assert(result!=null && result.Phase=="passed" && result.YesVotes==1 && result.RequiredVotes==1 && !result.CanVote,"Immediate pass disappeared before UI had a result");
            })
        };
        foreach(var (name,test) in cases) {test();Console.WriteLine("PASS: "+name);}
        return cases.Count;
    }
}
