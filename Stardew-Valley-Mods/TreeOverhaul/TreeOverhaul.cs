﻿namespace TreeOverhaul
{
    using StardewModdingAPI;
    using StardewModdingAPI.Events;
    using StardewValley;
    using StardewValley.GameData.WildTrees;
    using StardewValley.TerrainFeatures;
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Tree overhaul mod class that changes the behavior of wild trees.
    /// </summary>
    public class TreeOverhaul : Mod
    {
        internal TreeOverhaulConfig Config { get; set; }

        /// <summary>
        /// The mod entry point, called after the mod is first loaded.
        /// Loads config file and subscribes methods to some of the events
        /// </summary>
        /// <param name="helper">Provides simplified APIs for writing mods.</param>
        public override void Entry(IModHelper helper)
        {
            Config = Helper.ReadConfig<TreeOverhaulConfig>();

            TreeOverhaulConfig.VerifyConfigValues(Config, this);

            Helper.Events.GameLoop.DayStarted += delegate { OnDayStarted(); };

            Helper.Events.GameLoop.GameLaunched += delegate { TreeOverhaulConfig.SetUpModConfigMenu(Config, this); };

            Helper.Events.Content.AssetRequested += OnAssetRequested;

            Patcher.PatchAll(this);
        }

        /// <summary>
        /// Small helper method to log to the console because I keep forgetting the signature
        /// </summary>
        /// <param name="o">the object I want to log as a string</param>
        public void DebugLog(object o)
        {
            this.Monitor.Log(o == null ? "null" : o.ToString(), LogLevel.Debug);
        }

        private void OnAssetRequested(object sender, AssetRequestedEventArgs e)
        {
            if (e.NameWithoutLocale.IsEquivalentTo("Data/WildTrees"))
            {
                e.Edit((asset) =>
                {
                    IDictionary<string, WildTreeData> data = asset.AsDictionary<string, WildTreeData>().Data;

                    // for 'UseCustomShakingSeedChance'
                    float customShakingSeedChance = Math.Clamp(Config.CustomShakingSeedChance / 100f, 0f, 1f);
                    // for 'UseCustomTreeGrowthChance'
                    float customGrowthChance = Math.Clamp(Config.CustomTreeGrowthChance / 100f, 0f, 1f);

                    var normalTrees = new string[] { Tree.bushyTree, Tree.leafyTree, Tree.pineTree, Tree.mahoganyTree, Tree.palmTree, Tree.palmTree2 };
                    var palmTrees = new string[] { Tree.palmTree, Tree.palmTree2 };

                    foreach (var tree in normalTrees)
                    {
                        if (data.TryGetValue(tree, out var treeData))
                        {
                            if (Config.NormalTreesGrowInWinter)
                            {
                                treeData.GrowsInWinter = true;
                            }

                            if (Config.UseCustomShakingSeedChance && !palmTrees.Contains(tree))
                            {
                                treeData.SeedChance = customShakingSeedChance;
                            }

                            if (Config.UseCustomTreeGrowthChance)
                            {
                                treeData.GrowthChance = customGrowthChance;
                            }

                            data[tree] = treeData;
                        }
                    }

                    if (data.TryGetValue(Tree.mushroomTree, out var mushroomTreeData))
                    {
                        if (Config.MushroomTreesGrowInWinter)
                        {
                            mushroomTreeData.GrowsInWinter = true;
                            mushroomTreeData.IsStumpDuringWinter = false;

                            foreach (var tapItem in mushroomTreeData.TapItems)
                            {
                                var queries = GameStateQuery.SplitRaw(tapItem.Condition).Where(
                                    (q) => q.ToLower() != "!LOCATION_SEASON Target Winter".ToLower());

                                tapItem.Condition = string.Join(',', queries);
                            }
                        }

                        if (Config.UseCustomTreeGrowthChance)
                        {
                            mushroomTreeData.GrowthChance = customGrowthChance;
                        }

                        data[Tree.mushroomTree] = mushroomTreeData;
                    }

                    if (Config.BuffMahoganyTrees && data.TryGetValue(Tree.mahoganyTree, out var mahoganyTreeData) && data.TryGetValue(Tree.bushyTree, out var oakTreeData))
                    {
                        mahoganyTreeData.GrowthChance = oakTreeData.GrowthChance;
                        mahoganyTreeData.FertilizedGrowthChance = oakTreeData.FertilizedGrowthChance;

                        data[Tree.mahoganyTree] = mahoganyTreeData;
                    }
                }, AssetEditPriority.Late);
            }
        }

        private void OnDayStarted()
        {
            if (!Context.IsMainPlayer)
            {
                return;
            }

            Utility.ForEachLocation(delegate (GameLocation location)
            {
                foreach (var terrainfeature in location.terrainFeatures.Pairs)
                {
                    if (terrainfeature.Value is Tree tree)
                    {
                        if (tree.treeType.Value == Tree.mushroomTree && Config.MushroomTreesGrowInWinter && tree.stump.Value && Helper.Reflection.GetField<Season?>(tree, "localSeason").GetValue() == Season.Winter)
                        {
                            FixMushroomStump(tree, location);
                        }

                        if (tree.treeType.Value == Tree.mahoganyTree)
                        {
                            tree.modData.Remove($"{this.ModManifest.UniqueID}/growthStage");
                        }
                    }
                }

                return true;
            });
        }

        /// <summary>
        /// Reverts the mushroom stump back into a tree exactly like its done in StardewValley.TerrainFeatures.Tree.dayUpdate, but only if it's not a chopped down tree
        /// Then updates the tapper, so it works again
        /// </summary>
        /// <param name="tree">current mushroom tree</param>
        private void FixMushroomStump(Tree tree, GameLocation location)
        {
            var shakeRotation = Helper.Reflection.GetField<float>(tree, "shakeRotation");

            // if the value is higher than this, the game considers the tree as falling or having fallen
            if (Math.Abs(shakeRotation.GetValue()) < 1.5707963267948966)
            {
                tree.stump.Value = false;
                tree.health.Value = 10f;
                shakeRotation.SetValue(0f);
            }

            if (tree.stump.Value)
            {
                return;
            }

            if (tree.tapped.Value)
            {
                StardewValley.Object tile_object = location.getObjectAtTile((int)tree.Tile.X, (int)tree.Tile.Y, false);

                if (tile_object != null && tile_object.IsTapper() && tile_object.heldObject.Value == null)
                {
                    tree.UpdateTapperProduct(tile_object);
                }
            }
        }
    }

    /// <summary>
    /// Extension methods for IGameContentHelper.
    /// </summary>
    public static class GameContentHelperExtensions
    {
        /// <summary>
        /// Invalidates both an asset and the locale-specific version of an asset.
        /// </summary>
        /// <param name="helper">The game content helper.</param>
        /// <param name="assetName">The (string) asset to invalidate.</param>
        /// <returns>if something was invalidated.</returns>
        public static bool InvalidateCacheAndLocalized(this IGameContentHelper helper, string assetName)
            => helper.InvalidateCache(assetName)
                | (helper.CurrentLocaleConstant != LocalizedContentManager.LanguageCode.en && helper.InvalidateCache(assetName + "." + helper.CurrentLocale));
    }
}