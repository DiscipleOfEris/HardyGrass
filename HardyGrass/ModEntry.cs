using System;
using System.IO;
using System.Reflection;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Tools;
using StardewValley.TerrainFeatures;
using Harmony;
using Microsoft.Xna.Framework;

namespace HardyGrass
{
    /// <summary>The mod entry point.</summary>
    public partial class ModEntry : Mod
    {
        public static Lazy<Texture2D> texture;
        public static IMonitor monitor;
        public static IModHelper helper;

        public static ModConfig config;
        public static JsonAssetsApi jsonAssetsApi;

        public static string IsQuickModDataKey = "quick";
        public static string IsQuickModDataValue = "true";

        public static int GrassStarterObjectId = 297;
        public static int QuickGrassStarterObjectId = 0;

        public static Multiplayer Game1Multiplayer = null;
        public static MethodInfo GrassShakeMethodInfo = null;

        public enum GrowthType
        {
            Standard,
            Cut,
            Spread,
            SpreadConsolidate
        };

        /*********
        ** Public methods
        *********/
        /// <summary>The mod entry point, called after the mod is first loaded.</summary>
        /// <param name="helper">Provides simplified APIs for writing mods.</param>
        public override void Entry(IModHelper helper)
        {
            texture = new Lazy<Texture2D>(() => helper.Content.Load<Texture2D>("assets/cutgrass.png", ContentSource.ModFolder));
            monitor = Monitor;
            ModEntry.helper = Helper;

            Helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            Helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;

            var harmony = HarmonyInstance.Create(this.ModManifest.UniqueID);
            //Animals
            harmony.Patch(
               original: AccessTools.Method(typeof(FarmAnimal), "grassEndPointFunction", new Type[] { typeof(PathNode), typeof(Point), typeof(GameLocation), typeof(Character) }),
               prefix: new HarmonyMethod(typeof(FarmAnimal_grassEndPointFunction_Patch), nameof(FarmAnimal_grassEndPointFunction_Patch.Prefix))
            );
            harmony.Patch(
               original: AccessTools.Method(typeof(FarmAnimal), "Eat", new Type[] { typeof(GameLocation) }),
               prefix: new HarmonyMethod(typeof(FarmAnimal_Eat_Patch), nameof(FarmAnimal_Eat_Patch.Prefix))
            );
            //GameLocation
            harmony.Patch(
               original: AccessTools.Method(typeof(GameLocation), "growWeedGrass", new Type[] { typeof(int) }),
               prefix: new HarmonyMethod(typeof(GameLocation_growWeedGrass_Patch), nameof(GameLocation_growWeedGrass_Patch.Prefix))
            );

            //Grass
            harmony.Patch(
               original: AccessTools.Method(typeof(StardewValley.TerrainFeatures.Grass), "dayUpdate", new Type[] { typeof(GameLocation), typeof(Vector2) }),
               prefix: new HarmonyMethod(typeof(Grass_dayUpdate_Patch), nameof(Grass_dayUpdate_Patch.Prefix))
            );
            harmony.Patch(
               original: AccessTools.Method(typeof(StardewValley.TerrainFeatures.Grass), "reduceBy", new Type[] { typeof(int), typeof(Vector2), typeof(bool) }),
               postfix: new HarmonyMethod(typeof(Grass_reduceBy_Patch), nameof(Grass_reduceBy_Patch.Postfix))
            );
            harmony.Patch(
               original: AccessTools.Method(typeof(StardewValley.TerrainFeatures.Grass), "doCollisionAction", new Type[] { typeof(Rectangle), typeof(int), typeof(Vector2), typeof(Character), typeof(GameLocation) }),
               prefix: new HarmonyMethod(typeof(Grass_doCollisionAction_Patch), nameof(Grass_doCollisionAction_Patch.Prefix))
            );
            harmony.Patch(
               original: AccessTools.Method(typeof(StardewValley.TerrainFeatures.Grass), "performToolAction", new Type[] { typeof(Tool), typeof(int), typeof(Vector2), typeof(GameLocation) }),
               prefix: new HarmonyMethod(typeof(Grass_performToolAction_Patch), nameof(Grass_performToolAction_Patch.Prefix))
            );
            harmony.Patch(
               original: AccessTools.Method(typeof(StardewValley.TerrainFeatures.Grass), "performToolAction", new Type[] { typeof(Tool), typeof(int), typeof(Vector2), typeof(GameLocation) }),
               postfix: new HarmonyMethod(typeof(Grass_performToolAction_Patch), nameof(Grass_performToolAction_Patch.Postfix))
            );
            harmony.Patch(
               original: AccessTools.Method(typeof(StardewValley.TerrainFeatures.Grass), "draw", new Type[] { typeof(SpriteBatch), typeof(Vector2) }),
               prefix: new HarmonyMethod(typeof(Grass_draw_Patch), nameof(Grass_draw_Patch.Prefix))
            );
            //Objects
            harmony.Patch(
               original: AccessTools.Method(typeof(StardewValley.Object), "placementAction", new Type[] { typeof(GameLocation), typeof(int), typeof(int), typeof(Farmer) }),
               prefix: new HarmonyMethod(typeof(Object_placementAction_Patch), nameof(Object_placementAction_Patch.Prefix))
            );
            harmony.Patch(
              original: AccessTools.Method(typeof(StardewValley.Object), "isPlaceable", new Type[] { }),
              postfix: new HarmonyMethod(typeof(Object_isPlaceable_Patch), nameof(Object_isPlaceable_Patch.Postfix))
            );
            harmony.Patch(
              original: AccessTools.Method(typeof(StardewValley.Object), "isPassable", new Type[] { }),
              postfix: new HarmonyMethod(typeof(Object_isPassable_Patch), nameof(Object_isPassable_Patch.Postfix))
            );

        }

        /*********
        ** Private methods
        *********/
        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            config = Helper.ReadConfig<ModConfig>();

            ModConfig.Initialize(this);

            jsonAssetsApi = Helper.ModRegistry.GetApi<JsonAssetsApi>("spacechase0.JsonAssets");
            jsonAssetsApi.LoadAssets(Path.Combine(Helper.DirectoryPath, "assets", "JsonAssets"));
        }

        private void OnSaveLoaded(object sender, SaveLoadedEventArgs e)
        {
            QuickGrassStarterObjectId = jsonAssetsApi.GetObjectId("Quick Grass Starter");

            // Cache these private objects and methods we'll need for later patching.
            Game1Multiplayer = typeof(Game1).GetField("multiplayer", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.FlattenHierarchy).GetValue(null) as Multiplayer;
            GrassShakeMethodInfo = typeof(Grass).GetMethod("shake", BindingFlags.NonPublic | BindingFlags.Instance);
        }

        /*********
        ** Static methods
        *********/

        public static int CalculateTuftsToAdd(bool isQuick, GrowthType growthType)
        {
            double rand = Game1.random.NextDouble();
            float averageTufts = 0f;
            switch (growthType)
            {
                case GrowthType.Standard:
                    averageTufts = isQuick ? ModEntry.config.quickAverageTufts : ModEntry.config.vanillaAverageTufts;
                    break;
                case GrowthType.Cut:
                    averageTufts = isQuick ? ModEntry.config.quickCutAverageTufts : ModEntry.config.vanillaCutAverageTufts;
                    break;
                case GrowthType.Spread:
                    averageTufts = (isQuick ? ModEntry.config.quickSpreadAverageTufts : ModEntry.config.vanillaSpreadAverageTufts) * 0.25f;
                    break;
                case GrowthType.SpreadConsolidate:
                    averageTufts = isQuick ? ModEntry.config.quickSpreadAverageTufts : ModEntry.config.vanillaSpreadAverageTufts;
                    break;
            }
            int tuftsToAdd = 0;

            if (ModEntry.config.simplifyGrassGrowth)
            {
                tuftsToAdd = (int)(averageTufts + (averageTufts >= 1 && averageTufts <= 3 ? -1 : 0) + rand * (averageTufts >= 1 && averageTufts <= 3 ? 3 : 1));
            }
            else
            {
                if (averageTufts >= 4.0f)
                {
                    tuftsToAdd = 4;
                }
                else if (averageTufts >= 3.0f)
                {
                    tuftsToAdd = Game1.random.Next(2, 4);
                }
                else if (averageTufts >= 2.0f)
                {
                    tuftsToAdd = Game1.random.Next(1, 4);
                }
                else if (rand <= averageTufts * 0.5f)
                {
                    tuftsToAdd = Game1.random.Next(1, 4);
                }
            }

            return tuftsToAdd;
        }

        public static bool GrassIsQuick(Grass grass)
        {
            return grass.modData.TryGetValue(IsQuickModDataKey, out string quickValue) && quickValue == IsQuickModDataValue;
        }
    }
}