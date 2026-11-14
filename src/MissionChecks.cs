using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using FLScanIE.Util_Functions;
using FLScanIE.Logging;

namespace FLScanIE
{
    public static class MissionChecks
    {
        class FORMATION
        {
            public string nickname;
        }

        class SHIPCLASS
        {
            public string nickname;
            public List<string> members = new List<string>();
        }

        class NPCSHIPARCH
        {
            public string nickname;
            // The ship_archetype (lowercase), or null
            public string shipArchetype;
            // All npc_class values (lowercase)
            public HashSet<string> classes = new HashSet<string>();
        }

        class FACTIONPROPS
        {
            public string affiliation;
            public string desc;
            public List<string> npc_ships = new List<string>();
            public Dictionary<string, string> formations = new Dictionary<string, string>();
        }

        class SYSTEM_ENCOUNTER
        {
            public string name;
            public string desc;
            public int level;
            public List<string> factions = new List<string>();
        }

        /// <summary>
        /// A ship line of an [EncounterFormation]: ship_by_class or ship_by_npc_arch.
        /// </summary>
        class SHIP_LINE
        {
            public FLDataFile.Setting setting;
            // ship_by_class: the class (lowercase) and the difficulty offset
            public string shipClass;
            public int offset;
            // ship_by_npc_arch: the NPC ship (lowercase)
            public string npcArch;
        }

        class ENCOUNTER_FORMATION
        {
            public List<SHIP_LINE> lines = new List<SHIP_LINE>();
            public List<FLDataFile.Setting> formationsByClass = new List<FLDataFile.Setting>();
        }

        class ZONE
        {
            public string name;
        }

        class PATH_LABEL
        {
            public string path_label;
            public string desc;
            public List<uint> indices;
        }

        static Dictionary<string, FORMATION> formations = new Dictionary<string, FORMATION>();
        static Dictionary<string, SHIPCLASS> shipclasses = new Dictionary<string, SHIPCLASS>();
        static Dictionary<string, NPCSHIPARCH> npcships = new Dictionary<string, NPCSHIPARCH>();
        static Dictionary<string, FACTIONPROPS> factionprops = new Dictionary<string, FACTIONPROPS>();
        static Dictionary<string, ZONE> zones = new Dictionary<string, ZONE>();
        // The [EncounterFormation]s of each encounter file (full path), parsed when the file is first used
        static Dictionary<string, List<ENCOUNTER_FORMATION>> encounterFormations = new Dictionary<string, List<ENCOUNTER_FORMATION>>(StringComparer.OrdinalIgnoreCase);
        // The class ids that the mission_property values of each ship (lowercase nickname) give it
        static Dictionary<string, HashSet<string>> shipMissionProperties = new Dictionary<string, HashSet<string>>();
        // The nicknames (lowercase) of the ships in the npcships.ini of each mission
        static Dictionary<string, HashSet<string>> missionNpcShips = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The folders of the single-player missions in DATA\MISSIONS. The game's list of missions is hardcoded.
        /// </summary>
        public static readonly string[] Missions = { "M01A", "M01B", "M02", "M03", "M04", "M05", "M06", "M07", "M08", "M09", "M10", "M11", "M12", "M13" };
        // Encounter files are checked again for each zone and faction that uses them. Errors that don't depend
        // on the faction are only reported once (key: setting description + message).
        static HashSet<string> reportedEncounterErrors = new HashSet<string>();

        /// <summary>
        /// Loads a definitions ini file (relative to DATA). A vanilla file that isn't present is read from the
        /// vanilla definitions. Returns null if it doesn't exist, logging an error unless it's a vanilla file.
        /// </summary>
        static FLDataFile LoadIni(string relPath)
        {
            FLDataFile ini = Checker.LoadIni(relPath);
            if (ini == null && !Checker.FileExists(relPath))
                Logger.ELog("File not found: " + relPath);
            return ini;
        }

        /// <summary>
        /// Returns the lowercase nickname of a section, or logs an error and returns null if there's none.
        /// </summary>
        static string GetNickname(FLDataFile.Section sec, string fileName)
        {
            string nick = Util.TryGetStrSetting(sec, "nickname");
            if (nick == null)
                Logger.LogSettingNotFound(fileName, Util.GetSectionIdentifier(sec), sec.sectionName, "nickname");
            return nick == null ? null : nick.ToLowerInvariant();
        }

        static void LoadFormations()
        {
            Logger.ILog("Loading formations.ini");
            string fileName = "MISSIONS\\formations.ini";
            FLDataFile file = LoadIni(fileName);
            if (file == null)
                return;
            foreach (FLDataFile.Section sec in file.sections)
            {
                FORMATION o = new FORMATION();
                o.nickname = GetNickname(sec, fileName);
                if (o.nickname == null)
                    continue;
                formations[o.nickname] = o;
            }
        }

        static void LoadShipClasses()
        {
            Logger.ILog("Loading shipclasses.ini");
            string fileName = "MISSIONS\\shipclasses.ini";
            FLDataFile file = LoadIni(fileName);
            if (file == null)
                return;
            foreach (FLDataFile.Section sec in file.sections)
            {
                SHIPCLASS o = new SHIPCLASS();
                o.nickname = GetNickname(sec, fileName);
                if (o.nickname == null)
                    continue;

                foreach (FLDataFile.Setting set in sec.settings)
                {
                    if (set.settingName == "member" && set.NumValues() > 0)
                    {
                        string member = set.Str(0).ToLowerInvariant();
                        o.members.Add(member);
                    }
                }
                shipclasses[o.nickname] = o;
            }
        }

        // npc_class values that aren't a member of a [ShipClass] but are still used:
        // - Random missions look for NPC ships with the classes class_fighter and "D<level>", for each NPC level
        //   from 1 to 25 (content.dll). Neither is a [ShipClass], so they're compared with npc_class directly.
        // - cruiser isn't used by the game, but vanilla gives it to its cruisers, so reporting it isn't useful.
        // The diff<n> classes the game adds to encounter ships are [ShipClass]es in shipclasses.ini.
        static readonly HashSet<string> gameClassIds = new HashSet<string>(
            new[] { "class_fighter", "cruiser" }.Concat(Enumerable.Range(1, 25).Select(level => "d" + level)));

        static bool ShipClassMemberExists(string member)
        {
            if (gameClassIds.Contains(member))
                return true;
            foreach (SHIPCLASS c in shipclasses.Values)
            {
                foreach (string m in c.members)
                {
                    if (m == member)
                        return true;
                }
            }
            return false;
        }
        
        /// <summary>
        /// Checks an npcships.ini file and adds its ships to <see cref="npcships"/>, or to
        /// <see cref="missionNpcShips"/> if it's the npcships.ini of a mission (mission isn't null).
        /// </summary>
        static void CheckNPCShips(string npcShipPath, string mission)
        {
            FLDataFile file = LoadIni(npcShipPath);
            if (file == null)
                return;
            Logger.ILog("Checking " + npcShipPath);

            foreach (FLDataFile.Section sec in file.sections)
            {
                if (sec.sectionName.ToLowerInvariant() != "npcshiparch")
                    Logger.WLog(npcShipPath + ": Illegal section at line " + Util.GetSectionIdentifier(sec));

                NPCSHIPARCH o = new NPCSHIPARCH();
                o.nickname = GetNickname(sec, npcShipPath);
                if (o.nickname == null)
                    continue;

                foreach (FLDataFile.Setting set in sec.settings)
                {
                    if (set.NumValues() == 0)
                        continue;

                    if (set.settingName.ToLowerInvariant() == "level")
                    {
                        // The game skips the characters before the number, so d5 is 5
                        string number = new string(set.Str(0).SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
                        uint level;
                        if (!uint.TryParse(number, out level))
                            Logger.LogInvalidValue(npcShipPath, set, "Invalid level!");
                    }
                    else if (set.settingName.ToLowerInvariant() == "npc_class")
                    {
                        // A list of classes, in any order: the members of [ShipClass]es (e.g. lawful, d5), and the
                        // classes the game uses itself
                        foreach (string shipClass in set.values.Select(v => v.ToString().Trim().ToLowerInvariant()).TakeWhile(v => v != ""))
                        {
                            if (!ShipClassMemberExists(shipClass))
                                Logger.LogInvalidValue(npcShipPath, set, "Invalid shipclass member!", shipClass);
                            o.classes.Add(shipClass);
                        }
                    }
                    else if (set.settingName.ToLowerInvariant() == "ship_archetype")
                    {
                        string shipArch = set.Str(0).ToLowerInvariant();
                        o.shipArchetype = shipArch;
                        if (!ShipChecks.ShipExists(shipArch))
                            Logger.LogInvalidValue(npcShipPath, set, "Ship archetype doesn't exist!");
                    }
                    else if (set.settingName.ToLowerInvariant() == "loadout")
                    {
                        if (!LoadoutChecks.LoadoutExists(set.Str(0)))
                            Logger.LogInvalidValue(npcShipPath, set, "Loadout doesn't exist!");
                    }
                    else if (set.settingName.ToLowerInvariant() == "pilot")
                    {
                        if (!MissionFileChecks.PilotExists(set.Str(0)))
                            Logger.LogInvalidValue(npcShipPath, set, "Pilot doesn't exist! The ship gets the default AI personality.");
                    }
                    else if (set.settingName.ToLowerInvariant() == "state_graph")
                    {
                        // Content.dll looks it up with pub::StateGraph::get_state_graph(name, LEADER)
                        if (!UniverseChecks.LeaderStateGraphExists(set.Str(0)))
                            Logger.LogInvalidValue(npcShipPath, set, "Has to be a leader state graph from AI\\state_graph.db!");
                    }
                }
                if (mission == null)
                {
                    // The game keeps the first definition
                    if (!npcships.ContainsKey(o.nickname))
                        npcships[o.nickname] = o;
                }
                else
                    missionNpcShips[mission].Add(o.nickname);
            }
        }

        static void LoadNPCShips()
        {
            CheckNPCShips("MISSIONS\\npcships.ini", null);
            // The game reads it right after npcships.ini, into the same table
            if (Checker.FileExists("MISSIONS\\npcships_test.ini"))
                CheckNPCShips("MISSIONS\\npcships_test.ini", null);
            foreach (var mission in Missions)
            {
                missionNpcShips[mission] = new HashSet<string>();
                CheckNPCShips("MISSIONS\\" + mission + "\\npcships.ini", mission);
            }
        }

        /// <summary>
        /// Loads the mission_property values of the ships. An encounter's ship class that isn't a [ShipClass] can
        /// also be one of these (content.dll), under the names the game gives them.
        /// </summary>
        static void LoadShipMissionProperties()
        {
            var propertyClasses = new Dictionary<string, string>
            {
                { "can_use_berths", "can_use_berths" },
                { "can_use_med_moors", "can_use_medium_moors" },
                { "can_use_large_moors", "can_use_large_moors" },
            };
            foreach (var shipFile in Checker.flIni.GetSettings("Data", "ships").Where(s => s.NumValues() > 0))
            {
                FLDataFile ini = Checker.LoadIni(shipFile.Str(0));
                if (ini == null)
                    continue; // Reported by ShipChecks
                foreach (var sec in ini.sections.Where(sec => sec.sectionName.ToLowerInvariant() == "ship"))
                {
                    string nick = Util.TryGetStrSetting(sec, "nickname");
                    if (nick == null)
                        continue;
                    var properties = new HashSet<string>();
                    foreach (var property in sec.settings.Where(p => p.settingName.ToLowerInvariant() == "mission_property" && p.NumValues() > 0))
                    {
                        string propertyClass;
                        if (propertyClasses.TryGetValue(property.Str(0).Trim().ToLowerInvariant(), out propertyClass))
                            properties.Add(propertyClass);
                    }
                    shipMissionProperties[nick.ToLowerInvariant()] = properties;
                }
            }
        }

        /// <summary>
        /// Checks the keys of a [FactionProps] that only Content.dll uses. Returns true if the key is one of them.
        /// </summary>
        static bool CheckFactionPropsSetting(string fileName, FLDataFile.Setting set)
        {
            switch (set.settingName.ToLowerInvariant())
            {
                case "scan_for_cargo": // good, level (0, 1 or 2)
                    if (!EquipmentChecks.GoodExists(set.Str(0)))
                        Logger.LogInvalidValue(fileName, set, "Good doesn't exist!");
                    int level;
                    if (set.NumValues() > 1 && (!int.TryParse(set.Str(1), out level) || level < 0 || level > 2))
                        Logger.LogInvalidValue(fileName, set, "Has to be 0, 1 or 2! The game uses 0 otherwise.", set.Str(1));
                    return true;
                case "mc_costume":
                    if (!CharacterChecks.CostumeExists(set.Str(0)))
                        Logger.LogInvalidValue(fileName, set, "Costume doesn't exist!");
                    return true;
                case "space_costume": // head, body, accessories
                    var parts = set.values.Select(v => v.ToString().Trim()).TakeWhile(v => v != "").ToArray();
                    for (int i = 0; i < parts.Length; i++)
                    {
                        if (!(i < 2 ? CharacterChecks.BodypartExists(parts[i], i == 0 ? "head" : "body") : CharacterChecks.BodypartExists(parts[i], "accessory")))
                            Logger.LogInvalidValue(fileName, set, i == 0 ? "Head doesn't exist!" : i == 1 ? "Body doesn't exist!" : "Accessory doesn't exist!", parts[i]);
                    }
                    if (parts.Length < 2)
                        Logger.LogArgCount(fileName, set, 2);
                    return true;
                case "voice":
                    if (!AudioChecks.VoiceExists(set.Str(0)))
                        Logger.LogInvalidValue(fileName, set, "Voice doesn't exist!");
                    return true;
                case "jump_preference":
                    // Compared case-sensitively; anything else means any
                    if (set.Str(0) != "jumpgate" && set.Str(0) != "jumphole" && set.Str(0) != "any")
                        Logger.LogInvalidValue(fileName, set, "Has to be jumpgate, jumphole or any (lowercase)! The game uses any otherwise.", LogLevel.warning);
                    return true;
                case "legality":
                    if (set.Str(0).ToLowerInvariant() != "lawful" && set.Str(0).ToLowerInvariant() != "unlawful")
                        Logger.LogInvalidValue(fileName, set, "Has to be lawful or unlawful! The game ignores it otherwise.");
                    return true;
                case "nickname_plurality":
                    if (set.Str(0).ToLowerInvariant() != "singular" && set.Str(0).ToLowerInvariant() != "plural")
                        Logger.LogInvalidValue(fileName, set, "Has to be singular or plural! The game ignores it otherwise.");
                    return true;
            }
            return false;
        }

        static void LoadFactionProps()
        {
            Logger.ILog("Checking faction_prop.ini");
            string fileName = "MISSIONS\\faction_prop.ini";
            FLDataFile file = LoadIni(fileName);
            if (file == null)
                return;
            foreach (FLDataFile.Section sec in file.sections)
            {
                if (sec.sectionName.ToLowerInvariant() != "factionprops")
                    Logger.WLog(fileName + ": Illegal section at line " + Util.GetSectionIdentifier(sec));

                string affiliation = Util.TryGetStrSetting(sec, "affiliation");
                if (affiliation == null)
                {
                    Logger.LogSettingNotFound(fileName, Util.GetSectionIdentifier(sec), sec.sectionName, "affiliation");
                    continue;
                }

                FACTIONPROPS o = new FACTIONPROPS();
                o.affiliation = affiliation.ToLowerInvariant();
                o.desc = sec.settings[0].desc;

                foreach (FLDataFile.Setting set in sec.settings)
                {
                    if (set.NumValues() == 0)
                        continue;

                    if (set.settingName == "formation" && Util.CheckNumberOfArgs(set, fileName, 2))
                    {
                        string formation_by_class = set.Str(0).ToLowerInvariant();
                        string formation = set.Str(1).ToLowerInvariant();

                        // The game keeps the first formation of each class
                        if (!o.formations.ContainsKey(formation_by_class))
                            o.formations[formation_by_class] = formation;

                        if (!formations.ContainsKey(formation))
                            Logger.LogInvalidValue(fileName, set, "Formation not found!");
                    }
                    else if (CheckFactionPropsSetting(fileName, set))
                    {
                    }
                    else if (set.settingName == "npc_ship")
                    {
                        string npc_ship = set.Str(0).ToLowerInvariant();

                        if (!npcships.ContainsKey(npc_ship))
                            Logger.LogInvalidValue(fileName, set, "npc_ship not found!");

                        o.npc_ships.Add(npc_ship);
                    }
                }

                // The game keeps the first definition of a faction
                if (!factionprops.ContainsKey(o.affiliation))
                    factionprops[o.affiliation] = o;
            }
        }

        static void LoadSystem(string systemPath)
        {
            string fileName = "UNIVERSE\\" + systemPath;
            // Missing system files are reported by UniverseChecks
            if (!Checker.FileExistsOnDisk(fileName))
                return;
            FLDataFile file = new FLDataFile(Path.Combine(Checker.flDataPath, fileName), true);

            // All defined encounters, and the paths of the encounter files that can be checked
            HashSet<string> encounterNicks = new HashSet<string>();
            Dictionary<string, string> encounterFiles = new Dictionary<string, string>();
            foreach (FLDataFile.Section sec in file.sections)
            {
                if (sec.sectionName.ToLowerInvariant() == "encounterparameters")
                {
                    // Missing nicknames are reported by UniverseChecks
                    string nickname = Util.TryGetStrSetting(sec, "nickname");
                    string relEncounterFile = Util.TryGetStrSetting(sec, "filename");
                    if (nickname == null)
                        continue;
                    encounterNicks.Add(nickname.ToLowerInvariant());
                    if (relEncounterFile == null)
                    {
                        Logger.LogSettingNotFound(fileName, nickname, sec.sectionName, "filename");
                        continue;
                    }

                    if (Checker.FileExistsOnDisk(relEncounterFile))
                        encounterFiles[nickname.ToLowerInvariant()] = Path.Combine(Checker.flDataPath, relEncounterFile);
                    else if (!Checker.FileExists(relEncounterFile)) // Vanilla files that aren't present can't be checked
                        Logger.LogFileNotFound(fileName, sec.GetSetting("filename"));
                }
            }

            Dictionary<string, PATH_LABEL> path_labels = new Dictionary<string, PATH_LABEL>();

            foreach (FLDataFile.Section sec in file.sections)
            {
                if (sec.sectionName.ToLowerInvariant() == "zone")
                {
                    // Load the encounter information for the zone.
                    // Content.dll gives an encounter the faction lines that directly follow its encounter line
                    var encounters = new List<SYSTEM_ENCOUNTER>();
                    bool followsEncounter = false;
                    foreach (FLDataFile.Setting set in sec.settings)
                    {
                        bool directlyAfterEncounter = followsEncounter;
                        followsEncounter = false;
                        if (set.NumValues() == 0)
                            continue;

                        if (set.settingName.ToLowerInvariant() == "path_label")
                        {
                            string path_label = set.Str(0).ToLowerInvariant();
                            if (!path_labels.ContainsKey(path_label))
                            {
                                path_labels[path_label] = new PATH_LABEL();
                                path_labels[path_label].desc = set.desc;
                                path_labels[path_label].indices = new List<uint>();
                            }

                            path_labels[path_label].path_label = path_label;

                            for (int i = 1; i < set.NumValues(); i++)
                            {
                                uint path_index;
                                if (!uint.TryParse(set.Str(i), out path_index))
                                {
                                    Logger.LogInvalidValue(fileName, set, "Has to be a non-negative integer!", set.Str(i));
                                    continue;
                                }
                                if (path_labels[path_label].indices.Contains(path_index))
                                    Logger.LogDublicateValue(fileName, set, "Duplicate index!");
                                path_labels[path_label].indices.Add(path_index);
                            }
                        }
                        if (set.settingName.ToLowerInvariant() == "zone")
                        {
                            string zone = set.Str(0).ToLowerInvariant();
                            if (zones.ContainsKey(zone))
                                Logger.LogDublicateValue(fileName, set, "Duplicate zone!");
                            zones[zone] = new ZONE();
                            zones[zone].name = zone;
                        }
                        else if (set.settingName.ToLowerInvariant() == "encounter")
                        {
                            string encounter = set.Str(0).ToLowerInvariant();
                            if (!encounterNicks.Contains(encounter))
                                Logger.LogInvalidValue(fileName, set, "Encounter not found!");

                            if (encounters.Any(e => e.name == encounter))
                                Logger.LogDublicateValue(fileName, set, "Duplicate encounter - this may be intentional.", LogLevel.warning);

                            var newEncounter = new SYSTEM_ENCOUNTER { name = encounter, desc = set.desc };
                            encounters.Add(newEncounter);
                            followsEncounter = true;
                            // encounter = nickname, level, chance. The game truncates the level to an integer.
                            float level;
                            if (set.NumValues() < 2)
                                Logger.LogArgCount(fileName, set, 3);
                            else if (!float.TryParse(set.Str(1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out level))
                                Logger.LogInvalidValue(fileName, set, "Has to be a number!", set.Str(1));
                            else
                                newEncounter.level = (int)level;
                        }
                        else if (set.settingName.ToLowerInvariant() == "faction")
                        {
                            string faction = set.Str(0).ToLowerInvariant();
                            if (!factionprops.ContainsKey(faction))
                                Logger.LogInvalidValue(fileName, set, "Faction not found!");

                            if (directlyAfterEncounter)
                            {
                                encounters.Last().factions.Add(faction);
                                followsEncounter = true;
                            }
                            else if (encounters.Count == 0)
                                Logger.LogInvalidValue(fileName, set, "Faction isn't preceded by an encounter!");
                            else
                                Logger.LogInvalidValue(fileName, set, "The game ignores this faction: it doesn't directly follow an encounter or one of its factions!", LogLevel.warning);
                        }
                    }


                    // Check the encounter information for the zone.
                    foreach (SYSTEM_ENCOUNTER e in encounters)
                    {
                        if (encounterFiles.ContainsKey(e.name))
                            CheckEncounter(encounterFiles[e.name], e);
                    }
                }
            }

            // Check path label indices are contiguous.
            foreach (PATH_LABEL pl in path_labels.Values)
            {
                if (pl.indices.Count == 0)
                {
                    Logger.ELog(String.Format("{0}: missing path_label indices, path_label={1}", pl.desc, pl.path_label));
                    continue;
                }
                
                pl.indices.Sort();
                uint low = pl.indices.Min();
                uint end = pl.indices.Max();
                for (int i = 0; i < pl.indices.Count; i++)
                {
                    if (pl.indices[i] != low++)
                        Logger.ELog(String.Format("{0}: missing non-contiguous path_label index, path_label={1}", pl.desc, pl.path_label));
                }
                if ((low - 1) != end)
                    Logger.ELog(String.Format("{0}: missing non-contiguous path_label index, path_label={1}", pl.desc, pl.path_label));
            }
        }

        /// <summary>
        /// True if the class can be the class of a ship_by_class line: a [ShipClass], or a class that an NPC ship
        /// can have itself (an npc_class or a mission_property of its ship).
        /// </summary>
        static bool EncounterClassExists(string shipClass)
        {
            return shipclasses.ContainsKey(shipClass) || npcships.Values.Any(a => a.classes.Contains(shipClass))
                || shipMissionProperties.Values.Any(p => p.Contains(shipClass));
        }

        /// <summary>
        /// True if the NPC ship has the class, the way content.dll compares them: for a [ShipClass], one of its
        /// members has to be an npc_class of the ship. Any other class has to be an npc_class itself, or a
        /// mission_property of the ship's archetype.
        /// </summary>
        static bool HasClass(NPCSHIPARCH arch, string shipClass)
        {
            SHIPCLASS sc;
            if (shipclasses.TryGetValue(shipClass, out sc))
                return sc.members.Any(m => arch.classes.Contains(m));
            HashSet<string> properties;
            return arch.classes.Contains(shipClass) || arch.shipArchetype != null
                && shipMissionProperties.TryGetValue(arch.shipArchetype, out properties) && properties.Contains(shipClass);
        }

        /// <summary>
        /// Parses the [EncounterFormation]s of an encounter file (full path), and reports the errors that don't
        /// depend on the zone that uses it. Each file is parsed once.
        /// </summary>
        static List<ENCOUNTER_FORMATION> LoadEncounterFile(string encounterFile)
        {
            List<ENCOUNTER_FORMATION> result;
            if (encounterFormations.TryGetValue(encounterFile, out result))
                return result;
            result = new List<ENCOUNTER_FORMATION>();
            encounterFormations[encounterFile] = result;

            FLDataFile file = new FLDataFile(encounterFile, true);
            // The path relative to DATA, for the messages
            string relFile = encounterFile.StartsWith(Checker.flDataPath, StringComparison.OrdinalIgnoreCase)
                ? encounterFile.Substring(Checker.flDataPath.Length).TrimStart('\\', '/') : encounterFile;
            foreach (FLDataFile.Section sec in file.sections.Where(sec => sec.sectionName.ToLowerInvariant() == "encounterformation"))
            {
                var formation = new ENCOUNTER_FORMATION();
                result.Add(formation);
                foreach (FLDataFile.Setting set in sec.settings)
                {
                    if (set.NumValues() == 0)
                        continue;
                    switch (set.settingName.ToLowerInvariant())
                    {
                        case "ship_by_class": // min, max, class[, difficulty offset]
                            if (!Util.CheckNumberOfArgs(set, relFile, 3, 4))
                                break;
                            var line = new SHIP_LINE { setting = set, shipClass = set.Str(2).ToLowerInvariant() };
                            if (set.NumValues() == 4 && set.Str(3) != "" && !int.TryParse(set.Str(3), out line.offset))
                                Logger.LogInvalidValue(relFile, set, "Has to be an integer!", set.Str(3));
                            formation.lines.Add(line);
                            if (!EncounterClassExists(line.shipClass))
                                Logger.ELog(String.Format("{0}: missing shipclass, ship_class={1}", set.desc, line.shipClass));
                            break;
                        case "ship_by_npc_arch": // min, max, NPC ship
                            if (!Util.CheckNumberOfArgs(set, relFile, 3))
                                break;
                            var archLine = new SHIP_LINE { setting = set, npcArch = set.Str(2).ToLowerInvariant() };
                            formation.lines.Add(archLine);
                            if (!npcships.ContainsKey(archLine.npcArch))
                                Logger.LogInvalidValue(relFile, set, "NPC ship doesn't exist!", archLine.npcArch);
                            break;
                        case "make_class": // Classes that the ships of the last line get (they don't select ships)
                            foreach (string shipClass in set.values.Select(v => v.ToString().Trim().ToLowerInvariant()).TakeWhile(v => v != ""))
                            {
                                if (!ShipClassMemberExists(shipClass))
                                    Logger.ELog(String.Format("{0}: missing shipclass member, ship_class={1}", set.desc, shipClass));
                            }
                            break;
                        case "formation":
                            string formationNick = set.Str(0).ToLowerInvariant();
                            if (!formations.ContainsKey(formationNick))
                                Logger.ELog(String.Format("{0}: missing formation, formation={1}", set.desc, formationNick));
                            break;
                        case "formation_by_class": // Depends on the faction
                            formation.formationsByClass.Add(set);
                            break;
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Checks that each faction of an encounter of a zone can create the formations of the encounter. Like
        /// content.dll, a faction can only create a formation if, for each ship line, one of its npc_ships fits.
        /// A ship_by_class line also gets the class diff&lt;N&gt;, where N is the zone's encounter level plus the
        /// line's difficulty offset (at least 1); diff1 to diff19 are [ShipClass]es in shipclasses.ini.
        /// </summary>
        static void CheckEncounter(string encounterFile, SYSTEM_ENCOUNTER e)
        {
            List<ENCOUNTER_FORMATION> encounterFormationList = LoadEncounterFile(encounterFile);
            foreach (string faction in e.factions.Distinct())
            {
                FACTIONPROPS fp;
                if (!factionprops.TryGetValue(faction, out fp))
                    continue; // Reported as "Faction not found!" where the zone names the faction
                // Factions that use NPC ships that don't exist are reported by LoadFactionProps
                var ships = fp.npc_ships.Where(npcships.ContainsKey).Select(n => npcships[n]).ToList();

                foreach (ENCOUNTER_FORMATION formation in encounterFormationList)
                {
                    foreach (SHIP_LINE line in formation.lines)
                    {
                        string desc = line.setting.desc;
                        if (line.npcArch != null)
                        {
                            if (npcships.ContainsKey(line.npcArch) && !fp.npc_ships.Contains(line.npcArch)
                                && reportedEncounterErrors.Add(desc + "|" + faction))
                                Logger.ELog(String.Format("{0}: the npc_ship isn't an npc_ship of the faction, so it can't create this formation, npc_ship={1} faction={2} ref={3}",
                                    desc, line.npcArch, faction, e.desc));
                            continue;
                        }
                        if (!EncounterClassExists(line.shipClass))
                            continue; // Reported by LoadEncounterFile

                        string difficulty = "diff" + Math.Max(1, e.level + line.offset);
                        if (!ships.Any(s => HasClass(s, line.shipClass)))
                        {
                            if (reportedEncounterErrors.Add(desc + "|" + faction))
                                Logger.ELog(String.Format("{0}: no npc_ship of the faction has the ship class, so it can't create this formation, ship_class={1} faction={2} ref={3}",
                                    desc, line.shipClass, faction, e.desc));
                        }
                        else if (!ships.Any(s => HasClass(s, line.shipClass) && HasClass(s, difficulty))
                            && reportedEncounterErrors.Add(desc + "|" + faction + "|" + difficulty))
                        {
                            string note = shipclasses.ContainsKey(difficulty) ? "" : " (" + difficulty + " isn't a [ShipClass] in shipclasses.ini)";
                            Logger.WLog(String.Format("{0}: no npc_ship of the faction has both the ship class and the difficulty of the zone level {1} + offset {2}, so it can't create this formation, ship_class={3} difficulty={4}{5} faction={6} ref={7}",
                                desc, e.level, line.offset, line.shipClass, difficulty, note, faction, e.desc));
                        }
                    }

                    // The formation of a formation_by_class comes from the faction's [FactionProps]
                    foreach (FLDataFile.Setting set in formation.formationsByClass)
                    {
                        string formation_by_class = set.Str(0).ToLowerInvariant();
                        if (!fp.formations.ContainsKey(formation_by_class) && reportedEncounterErrors.Add(set.desc + "|" + faction))
                            Logger.ELog(String.Format("{0}: missing formation_by_class, formation_by_class={1} faction={2} ref={3}", set.desc, formation_by_class, faction, e.desc));
                    }
                }
            }
        }

        public static bool FormationExists(string nick)
        {
            return formations.ContainsKey(nick.ToLowerInvariant());
        }

        /// <summary>
        /// True if the NPC ship is defined in the npcships.ini of the mission or in the global one.
        /// </summary>
        public static bool NpcShipExists(string nick, string mission)
        {
            nick = nick.ToLowerInvariant();
            HashSet<string> ships;
            return npcships.ContainsKey(nick) || missionNpcShips.TryGetValue(mission, out ships) && ships.Contains(nick);
        }

        static void LoadUniverse()
        {
            FLDataFile file = LoadIni("UNIVERSE\\universe.ini");
            if (file == null)
                return;
            foreach (FLDataFile.Section sec in file.sections)
            {
                if (sec.sectionName.ToLowerInvariant() == "system")
                {
                    // Missing settings are reported by UniverseChecks
                    string systemFile = Util.TryGetStrSetting(sec, "file");
                    if (systemFile != null)
                        LoadSystem(systemFile);
                }
            }
        }

        public static void CheckForMissingFormations()
        {
#if !DEBUG
            try
            {
#endif
                formations.Clear();
                shipclasses.Clear();
                npcships.Clear();
                missionNpcShips.Clear();
                factionprops.Clear();
				zones.Clear();
                reportedEncounterErrors.Clear();
                encounterFormations.Clear();
                shipMissionProperties.Clear();

                LoadFormations();
                LoadShipClasses();
                LoadShipMissionProperties();
                LoadNPCShips();
                LoadFactionProps();
                LoadUniverse();
#if !DEBUG
            }
            catch (Exception e)
            {
                Logger.FLog("Scan aborted: " + e.Message);
            }
#endif
        }
    }
}
