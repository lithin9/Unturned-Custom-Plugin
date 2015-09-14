using System.Collections.Generic;
using Rocket.API;
using Rocket.Unturned.Player;
using SDG.Unturned;

namespace DingusGaming
{
    public class CommandTest : IRocketCommand
    {
        private const string NAME = "test";
        private const string HELP = "";
        private const string SYNTAX = "";
        private const bool ALLOW_FROM_CONSOLE = false;
        private const bool RUN_FROM_CONSOLE = false;
        private InteractableVehicle vehicle = null;

        public bool RunFromConsole
        {
            get { return RUN_FROM_CONSOLE; }
        }

        public string Name
        {
            get { return NAME; }
        }

        public string Help
        {
            get { return HELP; }
        }

        public string Syntax
        {
            get { return SYNTAX; }
        }

        public List<string> Aliases { get; } = new List<string>();

        public bool AllowFromConsole
        {
            get { return ALLOW_FROM_CONSOLE; }
        }

        public List<string> Permissions { get; } = new List<string>();

        public void Execute(IRocketPlayer caller, string[] command)
        {
            Execute((UnturnedPlayer) caller, command);
        }

        public void Execute(UnturnedPlayer caller, string[] command)
        {
            if (vehicle == null)
            {
                vehicle = caller.Player.Movement.getVehicle();
                DGPlugin.removeFromVehicle(caller);
            }
            else
            {
                DGPlugin.addToVehicle(caller, vehicle);
                vehicle = null;
            }

            DGPlugin.broadcastMessage("isinvehicle=" + (caller.Player.Movement.getVehicle() != null));
        }
    }
}