using System;
using System.Collections.Generic;
using Rocket.API;

namespace MreModeration
{
    public sealed class TrustGroupRule
    {
        public int MinimumScore;
        public string GroupId;
    }
    public sealed class MreModerationConfiguration : IRocketPluginConfiguration
    {
        public TrustPolicy Trust;
        public bool LogChat,LogCommands,TrustGroupsEnabled,WebhookEnabled;
        public List<TrustGroupRule> TrustGroups;
        public string WebhookUrl;
        public void LoadDefaults()
        {
            Trust=new TrustPolicy();LogChat=true;LogCommands=true;TrustGroupsEnabled=false;TrustGroups=new List<TrustGroupRule>();WebhookEnabled=false;WebhookUrl="";
        }
        public void Validate()
        {
            if(Trust==null||TrustGroups==null)throw new InvalidOperationException("Нет конфигурации trustscore.");Trust.Validate();
            if(TrustGroups.Count>10||TrustGroups.Exists(g=>g==null||g.MinimumScore<0||g.MinimumScore>100||!StaffPolicy.SafeTrustGroup(g.GroupId)))throw new InvalidOperationException("Недопустимые trust-группы.");
            if(WebhookEnabled&&!WebhookOutbox.SafeEndpoint(WebhookUrl))throw new InvalidOperationException("Webhook требует HTTPS URL Discord из локальной конфигурации.");
        }
    }
}
