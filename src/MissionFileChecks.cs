using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;

using FLScanIE.Util_Functions;
using FLScanIE.Logging;

namespace FLScanIE
{
    /// <summary>
    /// Checks the files in DATA\MISSIONS and DATA\RANDOMMISSIONS that aren't checked by <see cref="MissionChecks"/>:
    /// the single-player mission scripts, pilots, empathy, news, mission bases and random mission targets.
    /// None of these files are referenced by freelancer.ini; the game always loads them from these paths.
    /// Has to run after <see cref="MissionChecks.CheckForMissingFormations"/>, which loads the formations and NPC ships.
    /// </summary>
    public static class MissionFileChecks
    {
        // The game loads the population pilots before the story pilots, which inherit from them
        private static readonly string[] pilotFiles = { @"MISSIONS\pilots_population.ini", @"MISSIONS\pilots_story.ini" };

        // The lowercase nicknames of all [Pilot]s, loaded when they're first needed
        private static HashSet<string> pilotNicks;

        /// <summary>
        /// True if a [Pilot] with this nickname is defined in one of the pilot files.
        /// </summary>
        public static bool PilotExists(string nick)
        {
            if (pilotNicks == null)
            {
                var nicks = new HashSet<string>();
                foreach (string file in pilotFiles)
                {
                    FLDataFile ini = Checker.LoadIni(file);
                    if (ini == null)
                        continue; // Reported by CheckPilotFiles
                    foreach (var setting in ini.GetSettings("Pilot", "nickname").Where(s => s.NumValues() > 0))
                        nicks.Add(setting.Str(0).Trim().ToLower());
                }
                pilotNicks = nicks;
            }
            return pilotNicks.Contains(nick.Trim().ToLower());
        }

        /// <summary>
        /// Forgets the pilots of a previous scan.
        /// </summary>
        public static void ResetPilots()
        {
            pilotNicks = null;
        }

        // The [Pilot] settings that refer to a block, and the section of that block
        private static readonly Dictionary<string, string> pilotBlockSettings = new[]
        {
            "gun_id", "missile_id", "evade_dodge_id", "evade_break_id", "buzz_head_toward_id", "buzz_pass_by_id",
            "trail_id", "strafe_id", "engine_kill_id", "mine_id", "countermeasure_id", "damage_reaction_id",
            "missile_reaction_id", "formation_id", "job_id", "repair_id"
        }.ToDictionary(s => s, s => s.Substring(0, s.Length - 3).Replace("_", "") + "block"); // gun_id => gunblock

        public static void CheckMissionFiles()
        {
            Logger.ILog("Checking mission files");
            Util.RunChecks(CheckEmpathyFile, new[] { @"MISSIONS\empathy.ini" });
            Util.RunChecks(CheckNewsFile, new[] { @"MISSIONS\news.ini" });
            Util.RunChecks(CheckMBasesFile, new[] { @"MISSIONS\mbases.ini" });
            Util.RunCheck(CheckPilotFiles, "pilots");
            Util.RunChecks(CheckKillableSolarsFile, new[] { @"RANDOMMISSIONS\killablesolars.ini" });
            Util.RunChecks(CheckCriticalLootFile, new[] { @"RANDOMMISSIONS\vignettecriticalloot.ini" });
            foreach (string mission in MissionChecks.Missions)
            {
                string file = @"MISSIONS\" + mission + @"\" + mission + ".ini";
                if (Checker.FileExistsOnDisk(file))
                    Util.RunCheck(f => CheckMissionScript(f, mission), file);
            }
            Logger.ILog("Finished mission files");
        }

        private static FLDataFile Load(string file)
        {
            return new FLDataFile(Path.Combine(Checker.flDataPath, file), true);
        }

        private static void CheckFaction(string file, FLDataFile.Setting setting)
        {
            if (!UniverseChecks.FactionExists(setting.Str(0)))
                Logger.LogInvalidValue(file, setting, "Faction doesn't exist!", setting.Str(0));
        }

        /// <summary>
        /// empathy.ini: how a reputation change with one faction changes the reputation with the others.
        /// </summary>
        private static void CheckEmpathyFile(string file)
        {
            foreach (var setting in Load(file).GetSettings("RepChangeEffects"))
            {
                if (setting.NumValues() == 0)
                    continue;
                switch (setting.settingName.ToLower())
                {
                    case "group":
                    case "empathy_rate": // empathy_rate = faction, rate
                        CheckFaction(file, setting);
                        break;
                    case "event": // event = name, rate. Content.dll ignores the line if it doesn't know the name.
                        string eventName = setting.Str(0).Trim().ToLower();
                        if (eventName != "object_destruction" && eventName != "random_mission_success"
                            && eventName != "random_mission_failure" && eventName != "random_mission_abortion")
                            Logger.LogInvalidValue(file, setting, "Has to be object_destruction, random_mission_success, random_mission_failure or random_mission_abortion! The game ignores the line otherwise.");
                        break;
                }
            }
        }

        private static void CheckNewsFile(string file)
        {
            foreach (var setting in Load(file).GetSettings("NewsItem", "base"))
            {
                if (setting.NumValues() > 0 && !UniverseChecks.BaseExists(setting.Str(0)))
                    Logger.LogInvalidValue(file, setting, "Base doesn't exist!");
            }
        }

        /// <summary>
        /// mbases.ini: the factions, characters and rooms of each base.
        /// </summary>
        private static void CheckMBasesFile(string file)
        {
            foreach (var setting in Load(file).sections.SelectMany(s => s.settings))
            {
                if (setting.NumValues() == 0)
                    continue;
                switch (setting.settingName.ToLower())
                {
                    case "local_faction": // [MBase]
                    case "faction":       // [BaseFaction]
                    case "affiliation":   // [GF_NPC]
                    case "bribe":         // [GF_NPC] bribe = faction, price, ids
                        CheckFaction(file, setting);
                        break;
                }
            }
        }

        /// <summary>
        /// Checks the references of the pilots: their blocks, and the pilot each one inherits from. A pilot can only
        /// inherit from a pilot that is defined before it.
        /// </summary>
        private static void CheckPilotFiles()
        {
            // The nicknames of the blocks of each type (lowercase section name), and the position of each pilot
            var blocks = new Dictionary<string, HashSet<string>>();
            var pilotPositions = new Dictionary<string, int>();
            int position = 0;
            foreach (string file in pilotFiles)
            {
                // A vanilla file that isn't present is read from the vanilla definitions
                FLDataFile ini = Checker.LoadIni(file);
                if (ini == null)
                {
                    if (!Checker.FileExists(file))
                        Logger.ELog("File not found: " + file);
                    continue;
                }
                foreach (var section in ini.sections)
                {
                    string type = section.sectionName.ToLower();
                    string nick = Util.TryGetStrSetting(section, "nickname");
                    if (nick == null)
                        continue; // Reported below if the file is present
                    nick = nick.ToLower();
                    if (type == "pilot")
                    {
                        if (!pilotPositions.ContainsKey(nick))
                            pilotPositions[nick] = position;
                    }
                    else
                    {
                        if (!blocks.ContainsKey(type))
                            blocks[type] = new HashSet<string>();
                        blocks[type].Add(nick);
                    }
                    position++;
                }
            }

            // The block keys each pilot ends up with, including the inherited ones (Content.dll copies every block
            // the parent has when it reads inherit, so they replace the ones set before it)
            var pilotBlockKeys = new Dictionary<string, HashSet<string>>();
            position = 0;
            foreach (string file in pilotFiles)
            {
                bool present = Checker.FileExistsOnDisk(file);
                FLDataFile ini = present ? Load(file) : Checker.LoadIni(file);
                if (ini == null)
                    continue;
                foreach (var section in ini.sections)
                {
                    string nick = Util.TryGetStrSetting(section, "nickname");
                    if (nick == null && present)
                        Logger.LogSettingNotFound(file, Util.GetSectionIdentifier(section), section.sectionName, "nickname");
                    if (nick == null)
                        continue;
                    int current = position++;
                    if (section.sectionName.ToLower() != "pilot")
                        continue;
                    var blockKeys = new HashSet<string>();
                    if (!pilotBlockKeys.ContainsKey(nick.ToLower()))
                        pilotBlockKeys[nick.ToLower()] = blockKeys;

                    foreach (var setting in section.settings)
                    {
                        if (setting.NumValues() > 0)
                        {
                            string key = setting.settingName.ToLower();
                            HashSet<string> parentKeys;
                            if (key == "inherit" && pilotBlockKeys.TryGetValue(setting.Str(0).ToLower(), out parentKeys) && parentKeys != blockKeys)
                            {
                                foreach (string overwritten in blockKeys.Intersect(parentKeys).ToArray())
                                {
                                    if (present)
                                        Logger.LogInvalidValue(file, setting, "Replaces the " + overwritten + " set before it with the one of the inherited pilot! Put inherit first.", LogLevel.warning);
                                }
                                blockKeys.UnionWith(parentKeys);
                            }
                            else if (pilotBlockSettings.ContainsKey(key))
                                blockKeys.Add(key);
                        }
                    }
                    if (!present)
                        continue;

                    foreach (var setting in section.settings)
                    {
                        if (setting.NumValues() == 0)
                            continue;
                        string name = setting.settingName.ToLower();
                        string value = setting.Str(0).ToLower();
                        string blockType;
                        HashSet<string> nicks;
                        if (name == "inherit")
                        {
                            int inheritPosition;
                            if (!pilotPositions.TryGetValue(value, out inheritPosition))
                                Logger.LogInvalidValue(file, setting, "Pilot doesn't exist!");
                            else if (inheritPosition >= current)
                                Logger.LogInvalidValue(file, setting, "The pilot is defined after the pilot that inherits from it!");
                        }
                        else if (pilotBlockSettings.TryGetValue(name, out blockType)
                            && !(blocks.TryGetValue(blockType, out nicks) && nicks.Contains(value)))
                        {
                            Logger.LogInvalidValue(file, setting, "Block doesn't exist!");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// killablesolars.ini: the solars that random missions can ask the player to destroy.
        /// </summary>
        private static void CheckKillableSolarsFile(string file)
        {
            foreach (var setting in Load(file).GetSettings("KillableSolar"))
            {
                if (setting.NumValues() == 0)
                    continue;
                switch (setting.settingName.ToLower())
                {
                    case "archetype":
                        if (!SolarChecks.SolarExists(setting.Str(0)))
                            Logger.LogInvalidValue(file, setting, "Solar doesn't exist!");
                        break;
                    case "loadout":
                        if (!LoadoutChecks.LoadoutExists(setting.Str(0)))
                            Logger.LogInvalidValue(file, setting, "Loadout doesn't exist!");
                        break;
                }
            }
        }

        /// <summary>
        /// vignettecriticalloot.ini: the loot of random missions.
        /// </summary>
        private static void CheckCriticalLootFile(string file)
        {
            foreach (var setting in Load(file).GetSettings("critical_loot", "archetype"))
            {
                if (setting.NumValues() == 0)
                    continue;
                string loot = setting.Str(0);
                if (!EquipmentChecks.GoodExists(loot) && !EquipmentChecks.EquipExists(loot))
                    Logger.LogInvalidValue(file, setting, "Good or equipment doesn't exist!");
            }
        }

        /// <summary>
        /// Returns the lowercase nicknames of the sections with this (case-insensitive) name.
        /// </summary>
        private static HashSet<string> GetNicknames(FLDataFile ini, string sectionName)
        {
            return new HashSet<string>(ini.GetSettings(sectionName, "nickname")
                .Where(s => s.NumValues() > 0).Select(s => s.Str(0).ToLower()));
        }

        /// <summary>
        /// Returns the nicknames, as written, of the sections with these (case-insensitive) names.
        /// </summary>
        private static HashSet<string> GetExactNicknames(FLDataFile ini, params string[] sectionNames)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (string sectionName in sectionNames)
                names.UnionWith(ini.GetSettings(sectionName, "nickname").Where(s => s.NumValues() > 0).Select(s => s.Str(0).Trim()));
            return names;
        }

        /// <summary>
        /// Checks a reference that content.dll looks up with a case-sensitive string compare.
        /// </summary>
        private static void CheckExactReference(string file, FLDataFile.Setting setting, string value, HashSet<string> names, string what)
        {
            value = value.Trim();
            if (names.Contains(value))
                return;
            if (names.Contains(value, StringComparer.OrdinalIgnoreCase))
                Logger.LogInvalidValue(file, setting, "Has to match the case of the " + what + " nickname! The game compares them case-sensitively and does nothing otherwise.", value);
            else
                Logger.LogInvalidValue(file, setting, what + " doesn't exist!", value);
        }

        // The [MsnShip] and [MsnFormation] keys that content.dll writes to the group's current ship slot
        private static readonly string[] shipSlotKeys = { "nickname", "npc", "random_name", "label", "init_objectives",
            "init_metabehavior", "jumper", "cargo", "hit_points", "radius" };

        /// <summary>
        /// Checks the [MsnShip] and [MsnFormation] sections of a mission the way content.dll (FUN_06f18eb0) reads
        /// them into its one list of ship groups. Each section starts with one ship slot; ship and formation drop
        /// it, so the slot keys after them crash the game. ship = name takes the first ship of the first group read
        /// so far with exactly that nickname (strcmp) and removes that group from the list. Returns the nicknames
        /// of the groups left in the list, and where the groups that were taken were taken.
        /// </summary>
        private static HashSet<string> CheckShipGroups(string file, FLDataFile ini, Dictionary<string, int> takenAt)
        {
            var allNames = GetExactNicknames(ini, "MsnShip", "MsnFormation");
            var groups = new List<string>(); // In the order they were read
            var groupTypes = new List<string>();
            foreach (var section in ini.sections)
            {
                string sectionName = section.sectionName.ToLower();
                if (sectionName != "msnship" && sectionName != "msnformation")
                    continue;
                FLDataFile.Setting droppedBy = null;
                foreach (var setting in section.settings.Where(st => st.NumValues() > 0))
                {
                    string key = setting.settingName.ToLower();
                    string value = setting.Str(0).Trim();
                    if (droppedBy != null && shipSlotKeys.Contains(key))
                        Logger.LogInvalidValue(file, setting, "Has to come before " + droppedBy.settingName + "! The game crashes otherwise.");
                    if (key == "nickname")
                    {
                        // Duplicates of the same section type are reported by NicknameChecks
                        int index = groups.IndexOf(value);
                        if (index != -1 && groupTypes[index] != sectionName)
                            Logger.LogInvalidValue(file, setting, "A " + (groupTypes[index] == "msnship" ? "MsnShip" : "MsnFormation") + " with this nickname is still in the game's list of ship groups! Act_SpawnShip and Act_SpawnFormation spawn the first one.");
                        groups.Add(value);
                        groupTypes.Add(sectionName);
                    }
                    else if (key == "formation")
                    {
                        droppedBy = droppedBy ?? setting;
                    }
                    else if (key == "ship")
                    {
                        droppedBy = droppedBy ?? setting;
                        int index = groups.IndexOf(value);
                        if (index != -1)
                        {
                            groups.RemoveAt(index);
                            groupTypes.RemoveAt(index);
                            if (!takenAt.ContainsKey(value))
                                takenAt[value] = setting.LineNumber;
                        }
                        else if (takenAt.ContainsKey(value))
                            Logger.LogInvalidValue(file, setting, "Already in " + FormationAt(takenAt[value]) + "! The game removes the MsnShip when a formation takes it, so this formation doesn't get it.");
                        else if (groups.Contains(value, StringComparer.OrdinalIgnoreCase))
                            Logger.LogInvalidValue(file, setting, "Has to match the case of the MsnShip nickname! The game compares them case-sensitively and leaves the ship out otherwise.");
                        else if (allNames.Contains(value, StringComparer.OrdinalIgnoreCase))
                            Logger.LogInvalidValue(file, setting, "The MsnShip has to be defined before this formation! The game leaves the ship out otherwise.");
                        else
                            Logger.LogInvalidValue(file, setting, "MsnShip doesn't exist!");
                    }
                }
            }
            return new HashSet<string>(groups, StringComparer.Ordinal);
        }

        private static string FormationAt(int line)
        {
            return line > 0 ? "the formation at line " + line : "another formation";
        }

        /// <summary>
        /// Checks the ship group of Act_SpawnShip or Act_SpawnFormation.
        /// </summary>
        private static void CheckShipGroupReference(string file, FLDataFile.Setting setting, string value, HashSet<string> groups, Dictionary<string, int> takenAt)
        {
            value = value.Trim();
            if (!groups.Contains(value) && takenAt.ContainsKey(value))
                Logger.LogInvalidValue(file, setting, "The MsnShip is in " + FormationAt(takenAt[value]) + "! The game removes it when the formation takes it, so this does nothing.", value);
            else
                CheckExactReference(file, setting, value, groups, "MsnShip or MsnFormation");
        }

        /// <summary>
        /// Checks the references in the script of a single-player mission.
        /// </summary>
        private static void CheckMissionScript(string file, string mission)
        {
            FLDataFile ini = Load(file);
            var npcs = GetNicknames(ini, "NPC");
            var objLists = GetNicknames(ini, "ObjList");
            var objectives = GetNicknames(ini, "NNObjective");
            var triggers = GetNicknames(ini, "Trigger");
            var dialogs = GetNicknames(ini, "Dialog");
            var metaBehaviors = GetNicknames(ini, "MetaBehavior");
            // Content.dll finds these with strcmp, so the case has to match
            var solars = GetExactNicknames(ini, "MsnSolar");
            var loot = GetExactNicknames(ini, "MsnLoot");
            var takenShips = new Dictionary<string, int>(StringComparer.Ordinal);
            var shipGroups = CheckShipGroups(file, ini, takenShips); // One list for both
            var encounters = GetExactNicknames(ini, "MsnRandEnc");

            foreach (var section in ini.sections)
            {
                string sectionName = section.sectionName.ToLower();
                foreach (var setting in section.settings)
                {
                    if (setting.NumValues() == 0)
                        continue;
                    string name = setting.settingName.ToLower();
                    string value = setting.Str(0);

                    switch (sectionName + ":" + name)
                    {
                        case "npc:npc_ship_arch":
                            if (!MissionChecks.NpcShipExists(value, mission))
                                Logger.LogInvalidValue(file, setting, "NPC ship doesn't exist!");
                            break;
                        case "npc:affiliation":
                            CheckFaction(file, setting);
                            break;
                        case "msnship:npc":
                            if (!npcs.Contains(value.ToLower()))
                                Logger.LogInvalidValue(file, setting, "NPC doesn't exist!");
                            break;
                        case "msnformation:formation":
                            if (!MissionChecks.FormationExists(value))
                                Logger.LogInvalidValue(file, setting, "Formation doesn't exist!");
                            break;
                        case "trigger:system":
                            if (value.ToLower() != "any" && value.ToLower() != "all" && !UniverseChecks.SystemExists(value))
                                Logger.LogInvalidValue(file, setting, "System doesn't exist!");
                            break;
                        case "trigger:cnd_timer":
                            float seconds;
                            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
                                Logger.LogInvalidValue(file, setting, "Has to be a number!");
                            break;
                        case "trigger:act_giveobjlist": // Act_GiveObjList = ship, objlist. no_ol stands for no ObjList.
                            if (setting.NumValues() < 2)
                                Logger.LogArgCount(file, setting, 2);
                            else if (setting.Str(1).ToLower() != "no_ol" && !objLists.Contains(setting.Str(1).ToLower()))
                                Logger.LogInvalidValue(file, setting, "ObjList doesn't exist!", setting.Str(1));
                            break;
                        case "trigger:act_setnnobj": // 0 removes the objective
                            if (value != "0" && !objectives.Contains(value.ToLower()))
                                Logger.LogInvalidValue(file, setting, "NNObjective doesn't exist!");
                            break;
                        case "trigger:act_callthorn":
                            if (!Checker.FileExists(value))
                                Logger.LogFileNotFound(file, setting);
                            break;
                        case "trigger:act_acttrig":
                        case "trigger:act_deacttrig":
                            if (!triggers.Contains(value.ToLower()))
                                Logger.LogInvalidValue(file, setting, "Trigger doesn't exist!");
                            break;
                        case "trigger:act_spawnsolar":
                            CheckExactReference(file, setting, value, solars, "MsnSolar");
                            break;
                        case "trigger:act_spawnloot":
                            CheckExactReference(file, setting, value, loot, "MsnLoot");
                            break;
                        case "trigger:act_spawnship": // Act_SpawnShip = ship[, objlist[, x, y, z[, orientation]]]. no_ol stands for no ObjList.
                            CheckShipGroupReference(file, setting, value, shipGroups, takenShips);
                            if (setting.NumValues() > 1 && setting.Str(1).Trim() != "" && setting.Str(1).Trim().ToLower() != "no_ol" && !objLists.Contains(setting.Str(1).Trim().ToLower()))
                                Logger.LogInvalidValue(file, setting, "ObjList doesn't exist!", setting.Str(1));
                            break;
                        case "trigger:act_spawnformation":
                            CheckShipGroupReference(file, setting, value, shipGroups, takenShips);
                            break;
                        case "trigger:act_enableenc":
                        case "trigger:act_disableenc":
                            CheckExactReference(file, setting, value, encounters, "MsnRandEnc");
                            break;
                        case "trigger:act_setnnstate": // Act_SetNNState = objective, state
                            if (!objectives.Contains(value.ToLower()))
                                Logger.LogInvalidValue(file, setting, "NNObjective doesn't exist!");
                            if (setting.NumValues() < 2)
                                Logger.LogArgCount(file, setting, 2);
                            else if (!new[] { "active", "inactive", "complete", "failed" }.Contains(setting.Str(1).Trim().ToLower()))
                                Logger.LogInvalidValue(file, setting, "Has to be ACTIVE, INACTIVE, COMPLETE or FAILED! The game uses INACTIVE otherwise.", setting.Str(1));
                            break;
                        case "trigger:act_setnnhidden": // Act_SetNNHidden = objective or all, hidden
                            if (value.ToLower() != "all" && !objectives.Contains(value.ToLower()))
                                Logger.LogInvalidValue(file, setting, "NNObjective doesn't exist!");
                            break;
                        case "trigger:act_startdialog":
                            if (!dialogs.Contains(value.ToLower()))
                                Logger.LogInvalidValue(file, setting, "Dialog doesn't exist!");
                            break;
                        case "trigger:act_givemb": // Act_GiveMB = ship, metabehavior
                            if (setting.NumValues() < 2)
                                Logger.LogArgCount(file, setting, 2);
                            else if (!metaBehaviors.Contains(setting.Str(1).Trim().ToLower()))
                                Logger.LogInvalidValue(file, setting, "MetaBehavior doesn't exist!", setting.Str(1));
                            break;
                    }
                }
            }
        }
    }
}
