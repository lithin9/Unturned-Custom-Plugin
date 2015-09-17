using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
using DingusGaming.helper;
using DingusGaming.Party;
using DingusGaming.Store;
using Rocket.Unturned.Events;
using Rocket.Unturned.Player;
using SDG.Unturned;
using Steamworks;
using UnityEngine;

namespace DingusGaming.Events.Arena
{
    public class ArenaEvent : Event
    {
        public static ArenaEvent currentEvent;
        private readonly bool adminsIncluded;
        private static bool occurring = false;
        private readonly Dictionary<CSteamID, int> scores = new Dictionary<CSteamID, int>(),
            credits = new Dictionary<CSteamID, int>(), deaths = new Dictionary<CSteamID, int>();
        private List<int> sortedScores = new List<int>();
        private readonly ushort startItem, dropItem;
        private readonly Dictionary<CSteamID, PlayerState> states = new Dictionary<CSteamID, PlayerState>();
        public Vector3 location;
        private readonly float radius;

        public ArenaEvent(Vector3 location, float radius = 10, ushort startItem = 0, ushort dropItem = 0, bool adminsIncluded = true)
        {
            this.location = location;
            this.radius = radius;
            this.startItem = startItem;
            this.dropItem = dropItem;
            this.adminsIncluded = adminsIncluded;
        }

        public override string ToString()
        {
            return "Arena-startItem:"+startItem;
        }

        public static bool isOccurring
        {
            get { return occurring; }
        }

        private void suppressMessages()
        {
            Currency.showCreditEarnings = false;
            Parties.showDeathMessages = false;
        }

        private void unSuppressMessages()
        {
            Currency.showCreditEarnings = true;
            Parties.showDeathMessages = true;
        }

        private void onPlayerDeath(UnturnedPlayer player, EDeathCause cause, ELimb limb, CSteamID murderer)
        {
            UnturnedPlayer killer = DGPlugin.getKiller(player, cause, murderer);

            //update score of killing player
            if (killer != null)
                scores[killer.CSteamID]++;

            //update the deaths of the victim
            deaths[player.CSteamID]++;

            //clear their inventory so that they don't drop anything
            PlayerState.clearInventory(player);

            //respawn player
            LifeUpdated playerDied = null;
            playerDied = delegate (bool isDead)
            {
                if(isDead)
                    DGPlugin.respawnPlayer(player);
                
                player.Player.PlayerLife.OnUpdateLife -= playerDied;
            };
            player.Player.PlayerLife.OnUpdateLife += playerDied;
        }

        private void onPlayerRevive(UnturnedPlayer player, Vector3 position, byte angle)
        {
            LifeUpdated playerRevived = null;
            playerRevived = delegate (bool isDead)
            {
                if (!isDead)
                {
                    //give them the starting item
                    PlayerState.clearInventory(player);
                    if (startItem != 0)
                    {
                        player.GiveItem(startItem, 1);
                        player.Player.Equipment.equip(2, 1, 1);
                    }

                    DGPlugin.teleportPlayerInRadius(player, location, radius);
                }

                player.Player.PlayerLife.OnUpdateLife -= playerRevived;
            };
            player.Player.PlayerLife.OnUpdateLife += playerRevived;
        }

        public void startEvent()
        {
            if (!occurring)
            {
                occurring = true;

                suppressMessages();

                //disable all user commands during arena
                DGPlugin.disableCommands();

                states.Clear();
                scores.Clear();
                credits.Clear();
                deaths.Clear();

                //remember to check the adminsIncluded flag
                foreach (var plr in Steam.Players)
                {
                    var player = DGPlugin.getPlayer(plr.playerID.CSteamID);

                    //skip admins
                    if (player.IsAdmin && !adminsIncluded)
                        continue;

                    //revive player if dead
                    if (player.Player.life.isDead)
                    {
                        LifeUpdated playerDied = null;
                        playerDied = delegate (bool isDead)
                        {
                            if (!isDead)
                            {
                                preparePlayer(player);

                                player.Player.PlayerLife.OnUpdateLife -= playerDied;
                            }
                            else
                                DGPlugin.respawnPlayer(player);
                        };
                        player.Player.PlayerLife.OnUpdateLife += playerDied;

                        DGPlugin.respawnPlayer(player);
                    }
                    else
                        preparePlayer(player);
                }

                //drop starting items on location
                if(dropItem != 0)
                    for(int i=0; i<scores.Count; ++i)
                        ItemManager.dropItem(new SDG.Unturned.Item(dropItem, 1, 100), location, true, false, true);

                //hook in player death/revive events
                UnturnedPlayerEvents.OnPlayerDeath += onPlayerDeath;
                UnturnedPlayerEvents.OnPlayerRevive += onPlayerRevive;

                //start 3 second timer that will remove vanish-mode
                var vanishTimer = new Timer(3000);
                vanishTimer.AutoReset = false;
                vanishTimer.Elapsed += delegate
                {
                    foreach (var score in scores)
                    {
                        UnturnedPlayer player = DGPlugin.getPlayer(score.Key);
                        player.Features.GodMode = false;
                        player.Features.VanishMode = false;

                        //update player position so you can't hide vanished in the beginning
                        /*typeof (UnturnedPlayerFeatures).GetMethod("FixedUpdate",
                            BindingFlags.NonPublic | BindingFlags.Instance).Invoke(this, new object[] {});*/
                    }
                };
                vanishTimer.Start();
            }
        }

        private void preparePlayer(UnturnedPlayer player)
        {
            //save player state
            states.Add(player.CSteamID, PlayerState.getState(DGPlugin.getPlayer(player.CSteamID)));
            credits.Add(player.CSteamID, Currency.getBalance(player));

            //add player to scores and deaths lists
            scores.Add(player.CSteamID, 0);
            deaths.Add(player.CSteamID, 0);

            //clear player inventory
            PlayerState.clearInventory(player);

            //heal up all survival stats
            PlayerState.clearStats(player);

            //give players vanish-mode and god-mode
            player.Features.VanishMode = true;
            player.Features.GodMode = true;

            //teleport player to arena location
            DGPlugin.teleportPlayerInRadius(player, location, radius);

            //give players starting item if present
            if (startItem != 0)
            {
                player.GiveItem(startItem, 1);
                player.Player.Equipment.equip(2, 1, 1);
            }
        }

        public void stopEvent()
        {
            //unhook player death/revive events
            UnturnedPlayerEvents.OnPlayerDeath -= onPlayerDeath;
            UnturnedPlayerEvents.OnPlayerRevive -= onPlayerRevive;

            //sort the scores for placements
            sortedScores = scores.Values.ToList();
            sortedScores.Sort();

            //restore the player states
            foreach (var state in states)
            {
                try
                {
                    UnturnedPlayer player = DGPlugin.getPlayer(state.Key);
                    if (player.Player.life.isDead)
                    {
                        DGPlugin.respawnPlayer(player);

                        //set their state when they respawn
                        LifeUpdated playerRevived = null;
                        playerRevived = delegate(bool isDead)
                        {
                            if (!isDead)
                            {
                                state.Value.setCompleteState(player);

                                player.Player.PlayerLife.OnUpdateLife -= playerRevived;
                            }
                            else
                                DGPlugin.respawnPlayer(player);
                        };
                        player.Player.PlayerLife.OnUpdateLife += playerRevived;
                    }
                    else
                        state.Value.setCompleteState(player);

                    //notify everyone of how many people they killed/what place they earned out of everyone(e.g. 4/10, 4th highest score)
                    DGPlugin.messagePlayer(player,
                        "Arena has finished. You killed " + scores[state.Key] + " people(+$" + (Currency.getBalance(DGPlugin.getPlayer(state.Key))-credits[state.Key]) + ") and died " +
                        deaths[player.CSteamID] + " times! You earned place " + getPlace(scores[state.Key]) + "/" + scores.Count + "!");
                }
                catch (Exception)
                {
                    //catch exceptions here so that if one restore fails, the rest of function doesn't die
                }
            }

            //unset toggles
            DGPlugin.enableCommands();
            unSuppressMessages();
            occurring = false;
        }

        private int getPlace(int score)
        {
            for(int i=0; i<sortedScores.Count; ++i)
                if(sortedScores[i] == score)
                    return sortedScores.Count-i;
            return 0;
        }
    }
}