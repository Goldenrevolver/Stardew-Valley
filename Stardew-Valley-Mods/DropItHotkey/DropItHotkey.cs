﻿using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Menus;
using System;

namespace DropItHotkey
{
    public class Config
    {
        public KeybindList DropKey { get; set; } = KeybindList.Parse("LeftStick");
    }

    public class DropItHotkey : Mod
    {
        private Config config;

        public override void Entry(IModHelper helper)
        {
            config = Helper.ReadConfig<Config>();

            helper.Events.Input.ButtonsChanged += CheckForHotkey;
        }

        private void CheckForHotkey(object sender, ButtonsChangedEventArgs e)
        {
            if (config.DropKey.JustPressed())
            {
                Helper.Input.SuppressActiveKeybinds(config.DropKey);
                if (Context.IsPlayerFree)
                {
                    Item item = Game1.player.CurrentItem;
                    if (item != null && item.canBeDropped() && item.canBeTrashed())
                    {
                        DropItem(Game1.player, item.getOne());
                        Game1.player.reduceActiveItemByOne();
                    }
                }
                else if (Context.IsWorldReady && Game1.activeClickableMenu is GameMenu menu)
                {
                    if (menu.GetCurrentPage() is InventoryPage invPage)
                    {
                        if (CheckHeldItem((Item i) => i != null && i.canBeDropped() && i.canBeTrashed()))
                        {
                            DropItem(Game1.player, TakeHeldItem());
                        }
                    }
                }
            }
        }

        private void DropItem(Farmer player, Item toDrop)
        {
            // typo by the base game
            Game1.playSound("throwDownITem");
            Game1.createItemDebris(toDrop, player.getStandingPosition(), player.FacingDirection, null, -1).DroppedByPlayerID.Value = player.UniqueMultiplayerID;
        }

        private bool CheckHeldItem(Func<Item, bool> f = null)
        {
            if (f == null)
            {
                return Game1.player.CursorSlotItem != null;
            }

            return f(Game1.player.CursorSlotItem);
        }

        private Item TakeHeldItem()
        {
            Item cursorSlotItem = Game1.player.CursorSlotItem;
            Game1.player.CursorSlotItem = null;

            return cursorSlotItem;
        }
    }
}