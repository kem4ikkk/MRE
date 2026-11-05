using Rocket.Core.Plugins;
using Logger = Rocket.Core.Logging.Logger;

namespace MreHelp
{
    public sealed class MreHelpPlugin : RocketPlugin
    {
        private bool commandVerified;

        protected override void Load()
        {
            HelpUi.Start();
            Logger.Log("[MRE Help] Справка загружена; UI включается при наличии MRE Help EffectAsset.");
        }

        protected override void Unload() { HelpUi.Stop(); }

        private void Update()
        {
            if (commandVerified || !SDG.Unturned.Level.isLoaded) return;
            var selected = Rocket.Core.R.Commands.GetCommand("help") as Rocket.Core.Commands.RocketCommandManager.RegisteredRocketCommand;
            bool ours = selected != null && selected.Command is CommandHelp;
            Logger.Log("[MRE Help] /help dispatch=" + (ours ? "MreHelp OK" : "CONFLICT; use /mrehelp and review Commands.config.xml"));
            commandVerified = true;
        }
    }
}
