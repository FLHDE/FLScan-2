using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

using FLScanIE.Util_Functions;
using FLScanIE.Logging;

namespace FLScanIE
{
    public static class LoadoutChecks
    {
        private static string[] loadoutFiles;
        private static string[] loadoutNicknames;

        public static void CheckLoadouts()
        {
            Logger.ILog("Checks Loadouts");
            Util.RunChecks(CheckLoadoutFile, loadoutFiles);
            Logger.ILog("Finished Loadouts");
        }

        private static void CheckLoadoutFile(string file)
        {
            FLDataFile ini = new FLDataFile(Path.Combine(Checker.flDataPath, file), true);

            foreach (var loadout in ini.sections.Where(s => s.sectionName.ToLower() == "loadout"))
            {
                // common.dll creates the loadout when it reads the first nickname and ignores later ones. equip and
                // cargo add to that loadout, so before the nickname they crash the game.
                bool gotNickname = false;
                foreach (var setting in loadout.settings.Where(st => st.NumValues() > 0))
                {
                    string key = setting.settingName.ToLower();
                    if (key == "nickname")
                    {
                        if (gotNickname)
                            Logger.LogInvalidValue(file, setting, "The loadout already has a nickname! The game ignores this one.", LogLevel.warning);
                        gotNickname = true;
                    }
                    else if (!gotNickname && (key == "equip" || key == "cargo"))
                        Logger.LogInvalidValue(file, setting, "Has to come after the nickname! The game crashes otherwise.");
                }

                // The model of the archetype, used to check hardpoints. Null if unknown (no archetype, the model
                // couldn't be loaded, it's a vanilla model that isn't present, or UTF checks are disabled).
                UtfFile model = null;

                var archetype = Util.TryGetSetting(loadout, "archetype");
                if (archetype != null && archetype.NumValues() > 0)
                {
                    string arch = archetype.Str(0);
                    if (ShipChecks.ShipExists(arch))
                        ShipChecks.shipUTF.TryGetValue(arch, out model);
                    else if (SolarChecks.SolarExists(arch))
                        SolarChecks.solarUTF.TryGetValue(arch, out model);
                    else
                        Logger.LogInvalidValue(file, archetype, "Archetype not found!");
                }

                foreach (var setting in loadout.settings)
                {
                    if (setting.NumValues() == 0)
                        continue;

                    switch (setting.settingName.ToLower())
                    {
                        case "equip": // TODO: check if there are more weapons than hardpoints
                            if (!EquipmentChecks.EquipExists(setting.Str(0)))
                                Logger.LogInvalidValue(file, setting, "Equip doesn't exist!");

                            // equip = item[, hardpoint[, ...]]; an empty hardpoint means it's mounted internally
                            if (setting.NumValues() >= 2 && model != null && !Checker.DisableUTF)
                            {
                                string hardpoint = setting.Str(1).Trim();
                                if (hardpoint.Length != 0 && !model.HardpointExists(hardpoint))
                                    Logger.LogHardpoint(file, setting, hardpoint);
                            }
                            break;
                        case "cargo":
                            if (!EquipmentChecks.EquipExists(setting.Str(0)))
                                Logger.LogInvalidValue(file, setting, "Item doesn't exist!");
                            break;
                    }
                }
            }
        }

        public static void ParseLoadouts()
        {
            Logger.ILog("Parsing Loadouts");

            string[][] loadouts = Util.GetNicks("Data", "loadouts", "Loadout", "nickname");
            LoadoutChecks.loadoutFiles = loadouts[0];
            LoadoutChecks.loadoutNicknames = loadouts[1].Select(n => n.ToLower()).ToArray();

            Logger.ILog(string.Format("Finished Loadouts, found {0} loadouts", loadoutNicknames.Length));
        }

        public static bool LoadoutExists(string loadout)
        {
            loadout = loadout.ToLower();
            return loadoutNicknames.Contains(loadout);
        }
    }
}
