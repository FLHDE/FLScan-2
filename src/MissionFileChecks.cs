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
            Util.RunChecks(CheckKnowledgeMapFile, new[] { @"INTERFACE\knowledgemap.ini" });
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
        /// Checks the ids of a text: it has to be a string or an infocard. Returns false if it isn't a number.
        /// </summary>
        private static bool CheckTextIds(string file, FLDataFile.Setting setting, int index, string message, LogLevel level)
        {
            uint ids;
            if (!uint.TryParse(setting.Str(index).Trim(), out ids))
            {
                Logger.LogInvalidValue(file, setting, "Has to be a number!", setting.Str(index));
                return false;
            }
            if (ids != 0 && !Infocards.IsInUnloadedDll(ids) && !Infocards.IsName(ids) && !Infocards.IsInfocard(ids))
                Logger.LogInvalidValue(file, setting, message, setting.Str(index), level);
            return true;
        }

        /// <summary>
        /// interface\knowledgemap.ini, which freelancer.exe reads (FUN_00459470): Map = key, nickname, flags. When the
        /// player learns the key, the client sets the visit flags of CreateID(nickname), or with flag 0x40 of the faction
        /// (Reputation::get_id of the nickname, cut off after 15 characters). The keys are mostly ids, but many of them
        /// have no text, also in vanilla, so they aren't checked.
        /// </summary>
        private static void CheckKnowledgeMapFile(string file)
        {
            foreach (var setting in Load(file).GetSettings("KnowledgeMapTable", "map"))
            {
                if (setting.NumValues() < 3)
                {
                    Logger.LogArgCount(file, setting, 3);
                    continue;
                }
                int flags;
                if (!int.TryParse(setting.Str(2).Trim(), out flags))
                {
                    Logger.LogInvalidValue(file, setting, "Has to be a number!", setting.Str(2));
                    continue;
                }
                string nick = setting.Str(1).Trim();
                if ((flags & 0x40) != 0)
                {
                    if (nick.Length > 15)
                        Logger.LogInvalidValue(file, setting, "The game cuts faction nicknames off after 15 characters here, so it looks up " + nick.Substring(0, 15) + "!", nick);
                    else if (!UniverseChecks.FactionExists(nick))
                        Logger.LogInvalidValue(file, setting, "Faction doesn't exist! Flag 0x40 (64) makes this a faction.", nick);
                }
                else if (!UniverseChecks.SystemExists(nick) && !UniverseChecks.BaseExists(nick) && !UniverseChecks.ObjectOrZoneExistsAnywhere(nick))
                    Logger.LogInvalidValue(file, setting, "No system, base, object or zone has this nickname, so the line does nothing!", nick, LogLevel.warning);
            }
        }

        // The story stages of a rumor, which content.dll compares case-insensitively (the table at 0x6fce5d0)
        private static readonly string[] rumorStages =
        {
            "base_0_rank", "mission_01a_loaded", "mission_01a_accepted", "mission_01b_loaded", "mission_01b_accepted", "freetime_01_02",
            "mission_02_loaded", "mission_02_accepted", "freetime_02_03", "mission_03_loaded", "mission_03_accepted", "freetime_03_04",
            "mission_04_loaded", "mission_04_accepted", "freetime_04_05", "mission_05_loaded", "mission_05_accepted", "freetime_05_06",
            "mission_06_loaded", "mission_06_accepted", "freetime_06_07", "mission_07_loaded", "mission_07_accepted", "freetime_07_08",
            "mission_08_loaded", "mission_08_accepted", "freetime_08_09", "mission_09_loaded", "mission_09_accepted", "freetime_09_10",
            "mission_10_loaded", "mission_10_accepted", "freetime_10_11", "mission_11_loaded", "mission_11_accepted", "freetime_11_12",
            "mission_12_loaded", "mission_12_accepted", "freetime_12_13", "mission_13_loaded", "mission_13_accepted", "mission_end"
        };

        /// <summary>
        /// mbases.ini: the factions, characters and rooms of each base. Content.dll (FUN_06eaa820) reads the first
        /// [MBase] whose nickname is the base, and the sections after it up to the next [MBase]. A fixture looks its
        /// [GF_NPC] up while the file is read, so the NPC has to come first.
        /// </summary>
        private static void CheckMBasesFile(string file)
        {
            var seenBases = new Dictionary<string, int>();
            string baseNick = null;     // The base of the current [MBase], or null if the game doesn't read it
            HashSet<string> rooms = null;
            var npcs = new HashSet<string>();
            var factionNpcs = new List<FLDataFile.Setting>();
            Action endBase = () =>
            {
                foreach (var setting in factionNpcs.Where(s => !npcs.Contains(s.Str(0).Trim().ToLower())))
                    Logger.LogInvalidValue(file, setting, "No [GF_NPC] of this base has this nickname, so the line does nothing!");
                npcs.Clear();
                factionNpcs.Clear();
            };

            FLDataFile ini = Load(file);
            foreach (var section in ini.sections)
            {
                string type = section.sectionName.ToLower();
                if (type == "mbase")
                {
                    endBase();
                    var nickSetting = Util.TryGetSetting(section, "nickname");
                    baseNick = nickSetting == null || nickSetting.NumValues() == 0 ? null : nickSetting.Str(0).Trim().ToLower();
                    rooms = null;
                    if (baseNick == null)
                        Logger.LogSettingNotFound(file, Util.GetSectionIdentifier(section), section.sectionName, "nickname");
                    else if (!UniverseChecks.BaseExists(baseNick))
                    {
                        Logger.LogInvalidValue(file, nickSetting, "Base doesn't exist! The game never reads this [MBase].");
                        baseNick = null;
                    }
                    else if (seenBases.ContainsKey(baseNick))
                    {
                        Logger.LogDublicateValue(file, nickSetting, "Duplicate [MBase]! The game only reads the first one of a base"
                            + (seenBases[baseNick] > 0 ? ", at line " + seenBases[baseNick] : ""));
                        baseNick = null;
                    }
                    else
                    {
                        seenBases[baseNick] = nickSetting.LineNumber;
                        rooms = UniverseChecks.GetBaseRooms(baseNick);
                    }
                }
                else if (type == "gf_npc")
                {
                    var nickSetting = Util.TryGetSetting(section, "nickname");
                    if (nickSetting != null && nickSetting.NumValues() > 0)
                    {
                        npcs.Add(nickSetting.Str(0).Trim().ToLower());
                    }
                }

                foreach (var setting in section.settings.Where(s => s.NumValues() > 0))
                {
                    string key = setting.settingName.ToLower();
                    string value = setting.Str(0).Trim();
                    switch (type + ":" + key)
                    {
                        case "mbase:local_faction":
                        case "basefaction:faction":
                        case "gf_npc:affiliation":
                        case "gf_npc:bribe": // bribe = faction, price, ids
                            CheckFaction(file, setting);
                            if (key == "bribe")
                            {
                                int price;
                                if (setting.NumValues() < 3)
                                    Logger.LogArgCount(file, setting, 3);
                                else if (int.TryParse(setting.Str(1).Trim(), out price) && price == 0)
                                    Logger.LogInvalidValue(file, setting, "The game ignores a bribe that costs 0!", setting.Str(1));
                                else
                                    CheckTextIds(file, setting, 2, "Text doesn't exist!", LogLevel.error);
                            }
                            break;
                        case "basefaction:npc": // Gives the NPC the faction as its affiliation
                            factionNpcs.Add(setting);
                            break;
                        case "gf_npc:base_appr":
                            if (!CharacterChecks.CostumeExists(value))
                                Logger.LogInvalidValue(file, setting, "Costume doesn't exist! The game ignores the line.");
                            break;
                        case "gf_npc:head":
                        case "gf_npc:body":
                        case "gf_npc:lefthand":
                        case "gf_npc:righthand":
                        case "gf_npc:accessory":
                            if (!CharacterChecks.BodypartExists(value, key))
                                Logger.LogInvalidValue(file, setting, "Has to be a [" + key + "] of the bodyparts files!");
                            break;
                        case "gf_npc:voice":
                            if (!AudioChecks.VoiceExists(value))
                                Logger.LogInvalidValue(file, setting, "Voice doesn't exist!");
                            break;
                        case "gf_npc:individual_name":
                            CheckTextIds(file, setting, 0, "Text doesn't exist!", LogLevel.error);
                            break;
                        case "gf_npc:room": // pub::GF::FindLocation(base, room)
                        case "mroom:nickname":
                            if (rooms != null && !rooms.Contains(value.ToLower()))
                                Logger.LogInvalidValue(file, setting, type == "mroom" ? "The base has no such room, so the game ignores this [MRoom]!" : "The base has no such room!");
                            break;
                        case "mroom:fixture": // fixture = npc, placement, fidget, role
                            if (setting.NumValues() < 4)
                                Logger.LogArgCount(file, setting, 4);
                            if (!npcs.Contains(value.ToLower()))
                            {
                                // A [GF_NPC] of the same [MBase] that comes after this section
                                bool definedLater = ini.sections.SkipWhile(s => s != section).Skip(1)
                                    .TakeWhile(s => s.sectionName.ToLower() != "mbase")
                                    .Any(s => s.sectionName.ToLower() == "gf_npc" && string.Equals((Util.TryGetStrSetting(s, "nickname") ?? "").Trim(), value, StringComparison.OrdinalIgnoreCase));
                                Logger.LogInvalidValue(file, setting, definedLater
                                    ? "The [GF_NPC] has to come before this [MRoom]! The game looks it up while it reads the file, and ignores the fixture otherwise."
                                    : "No [GF_NPC] of this base has this nickname, so the game ignores the fixture!");
                            }
                            else
                            {
                                // The placement is looked up in the room, so the room has to be known by then. The NPC
                                // placements come from the room's scenes, not its INI, so they aren't checked.
                                var roomNick = Util.TryGetSetting(section, "nickname");
                                if (roomNick == null || roomNick.LineNumber > setting.LineNumber)
                                    Logger.LogInvalidValue(file, setting, "Has to come after the nickname of the [MRoom]! The game ignores the fixture otherwise.");
                            }
                            break;
                        case "gf_npc:know": // know = ids, ids, price, reputation level
                            if (setting.NumValues() < 4)
                            {
                                Logger.LogArgCount(file, setting, 4);
                                break;
                            }
                            CheckTextIds(file, setting, 0, "Text doesn't exist!", LogLevel.error);
                            CheckTextIds(file, setting, 1, "Text doesn't exist!", LogLevel.error);
                            int knowLevel;
                            if (!int.TryParse(setting.Str(3).Trim(), out knowLevel) || knowLevel < 0 || knowLevel > 3)
                                Logger.LogInvalidValue(file, setting, "Reputation levels are 0 to 3! The game ignores the line otherwise.", setting.Str(3));
                            break;
                        case "gf_npc:rumor": // rumor = from stage, to stage, reputation level, ids
                        case "gf_npc:rumor_type2":
                            if (setting.NumValues() < 4)
                            {
                                Logger.LogArgCount(file, setting, 4);
                                break;
                            }
                            if (!rumorStages.Contains(value, StringComparer.OrdinalIgnoreCase))
                                Logger.LogInvalidValue(file, setting, "Not a story stage! The game then uses the address of its INI reader as the stage, so it never offers the rumor.", value);
                            else if (!rumorStages.Contains(setting.Str(1).Trim(), StringComparer.OrdinalIgnoreCase))
                                Logger.LogInvalidValue(file, setting, "Not a story stage! The game then uses the first stage, so it only offers the rumor at that stage.", setting.Str(1));
                            // The game (0x06ed4100) skips the rumor if the player's reputation is below a table of 4
                            // thresholds (0, 0.2, 0.4, 0.6 at 0x6fce350). After it comes a double that is set to 15.0: as
                            // a float, its low half is 0 and its high half 2.72. The rumor's weight is 64 if its stages
                            // lie inside the story (after base_0_rank, before mission_end), and (level + 1) * 5 otherwise.
                            int rumorLevel;
                            if (!int.TryParse(setting.Str(2).Trim(), out rumorLevel))
                                Logger.LogInvalidValue(file, setting, "Has to be a number!", setting.Str(2));
                            else if (rumorLevel == 4)
                            {
                                bool insideStory = !value.Equals("base_0_rank", StringComparison.OrdinalIgnoreCase)
                                    && !setting.Str(1).Trim().Equals("mission_end", StringComparison.OrdinalIgnoreCase);
                                Logger.LogInvalidValue(file, setting, insideStory
                                    ? "Reputation levels are 0 to 3. For 4 the game reads past its table and gets 0, so this works like level 0; use 0 instead."
                                    : "Reputation levels are 0 to 3. For 4 the game reads past its table and gets 0, so the rumor needs as little reputation as at level 0, but is picked five times as often.",
                                    setting.Str(2), LogLevel.warning);
                            }
                            else if (rumorLevel < 0 || rumorLevel > 3)
                                Logger.LogInvalidValue(file, setting, "Reputation levels are 0 to 3! The game reads outside its table of reputation thresholds otherwise.", setting.Str(2));
                            for (int i = 3; i < setting.NumValues() && setting.Str(i).Trim() != ""; i++)
                                CheckTextIds(file, setting, i, "Text doesn't exist!", LogLevel.error);
                            break;
                    }
                }
            }
            endBase();
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
        /// so far with exactly that nickname (strcmp) and removes that group from the list. Act_SpawnShip and
        /// Act_SpawnFormation (FUN_06ec6580, FUN_06ec71d0) spawn the first group in the list with exactly that
        /// nickname, and leave it in the list. Returns the nicknames of the groups left in the list, and where the
        /// groups that were taken were taken.
        /// </summary>
        private static HashSet<string> CheckShipGroups(string file, FLDataFile ini, Dictionary<string, int> takenAt)
        {
            var allNames = GetExactNicknames(ini, "MsnShip", "MsnFormation");
            var groups = new List<string>(); // In the order they were read
            var groupTypes = new List<string>();
            var groupSections = new List<FLDataFile.Section>();
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
                        groups.Add(value);
                        groupTypes.Add(sectionName);
                        groupSections.Add(section);
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
                            groupSections.RemoveAt(index);
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

            // The spawn actions run after the whole file is read, so they find the first group with a nickname in
            // the list that's left
            var spawned = new HashSet<string>(ini.sections.Where(s => s.sectionName.ToLower() == "trigger").SelectMany(s => s.settings)
                .Where(s => s.NumValues() > 0 && (s.settingName.ToLower() == "act_spawnship" || s.settingName.ToLower() == "act_spawnformation"))
                .Select(s => s.Str(0).Trim()), StringComparer.Ordinal);
            for (int i = 0; i < groups.Count; i++)
            {
                int first = groups.IndexOf(groups[i]);
                if (first == i)
                    continue;
                var nickSetting = Util.TryGetSetting(groupSections[i], "nickname");
                string firstType = groupTypes[first] == "msnship" ? "MsnShip" : "MsnFormation";
                var firstNick = Util.TryGetSetting(groupSections[first], "nickname");
                string firstAt = firstNick.LineNumber > 0 ? " at line " + firstNick.LineNumber : "";
                string message = string.Format("Never spawned! Act_SpawnShip and Act_SpawnFormation spawn the {0} with this nickname{1}, which comes first in the game's list of ship groups.", firstType, firstAt);
                if (groupTypes[first] == groupTypes[i] && SameSettings(groupSections[first], groupSections[i]))
                    Logger.LogInvalidValue(file, nickSetting, message + " This one is an identical copy.", LogLevel.warning);
                else if (!spawned.Contains(groups[i]))
                    Logger.LogInvalidValue(file, nickSetting, message + " Nothing spawns this nickname, so neither one is used.", LogLevel.warning);
                else
                    Logger.LogInvalidValue(file, nickSetting, message);
            }
            return new HashSet<string>(groups, StringComparer.Ordinal);
        }

        /// <summary>
        /// Whether two sections have the same settings and values (ignoring case and surrounding spaces).
        /// </summary>
        private static bool SameSettings(FLDataFile.Section a, FLDataFile.Section b)
        {
            if (a.settings.Count != b.settings.Count)
                return false;
            for (int i = 0; i < a.settings.Count; i++)
            {
                FLDataFile.Setting x = a.settings[i], y = b.settings[i];
                if (x.settingName.ToLower() != y.settingName.ToLower() || x.NumValues() != y.NumValues())
                    return false;
                for (int v = 0; v < x.NumValues(); v++)
                {
                    if (x.Str(v).Trim().ToLower() != y.Str(v).Trim().ToLower())
                        return false;
                }
            }
            return true;
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
                            if (!triggers.Contains(value.ToLower()))
                                Logger.LogInvalidValue(file, setting, "Trigger doesn't exist!");
                            break;
                        case "trigger:act_deacttrig":
                            if (!triggers.Contains(value.ToLower()))
                                Logger.LogInvalidValue(file, setting, "Trigger doesn't exist! The game does nothing then.");
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
