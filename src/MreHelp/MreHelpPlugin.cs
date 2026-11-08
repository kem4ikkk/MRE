using Rocket.Core.Plugins;
using Logger = Rocket.Core.Logging.Logger;

namespace MreHelp
{
    public sealed class MreHelpPlugin : RocketPlugin
    {
        private bool commandVerified;
        private float nextHomeRefresh;

        protected override void Load()
        {
            HelpUi.Start();
            Logger.Log("[MRE Help] Справка загружена; UI включается при наличии MRE Help EffectAsset.");
        }

        protected override void Unload() { HelpUi.Stop(); }

        private void Update()
        {
            if (!SDG.Unturned.Level.isLoaded) return;
            if (UnityEngine.Time.realtimeSinceStartup >= nextHomeRefresh)
            {
                nextHomeRefresh = UnityEngine.Time.realtimeSinceStartup + 5;
                HelpUi.RefreshHome();
            }
            if (commandVerified) return;
            var selected = Rocket.Core.R.Commands.GetCommand("help") as Rocket.Core.Commands.RocketCommandManager.RegisteredRocketCommand;
            var menu = Rocket.Core.R.Commands.GetCommand("menu") as Rocket.Core.Commands.RocketCommandManager.RegisteredRocketCommand;
            bool helpOurs = selected != null && selected.Command is CommandHelp;
            bool menuOurs = menu != null && menu.Command is CommandMenu;
            Logger.Log("[MRE Menu] /menu dispatch=" + (menuOurs ? "MreHelp OK" : "CONFLICT") + "; /help dispatch=" + (helpOurs ? "MreHelp OK" : "CONFLICT; use /mrehelp and review Commands.config.xml"));
            commandVerified = true;
        }
    }
}
