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
            public string desc;
            public uint level;
            public Dictionary<uint, bool> levels = new Dictionary<uint, bool>();
            public List<string> shipclass_members = new List<string>();
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
            public uint level;
            public List<string> factions = new List<string>();
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

        static void LoadDefaultShipClassMembers()
        {
            List<string> default_members = new List<string>()
            {
                "FIGHTER", "FREIGHTER", "TRANSPORT", "GUNBOAT", "CRUISER", "CAPITAL", "TRADELANE",
                "JUMPGATE", "WEAPONS_PLATFORM", "DESTROYABLE_DEPOT", "SOLAR", "ANYTHING"
            }.ConvertAll(m => m.ToLowerInvariant());

            SHIPCLASS s = new SHIPCLASS();
            s.nickname = "_";
            s.members = default_members;
            shipclasses[s.nickname] = s;

            for (int i = 0; i < 10; ++i)
            for (int j = 0; j < 10; ++j)
            {
                s.nickname = $"diff{i}{j}";
                s.members = default_members;
                shipclasses[s.nickname] = s;

                default_members.Add($"d{i}{j}");
            }
        }

        static bool ShipClassMemberExists(string member)
        {
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
                if (sec.sectionName != "NPCShipArch")
                    Logger.WLog(npcShipPath + ": Illegal section at line " + Util.GetSectionIdentifier(sec));

                NPCSHIPARCH o = new NPCSHIPARCH();
                o.nickname = GetNickname(sec, npcShipPath);
                if (o.nickname == null)
                    continue;

                foreach (FLDataFile.Setting set in sec.settings)
                {
                    if (set.NumValues() == 0)
                        continue;

                    if (set.settingName == "level")
                    {
                        uint level;
                        if (!TryParseLevel(set.Str(0), out level))
                            Logger.LogInvalidValue(npcShipPath, set, "Invalid level!");

                        o.level = level;
                    }
                    else if (set.settingName == "npc_class")
                    {
                        if (set.NumValues() < 2)
                        {
                            Logger.LogArgCount(npcShipPath, set, 2);
                            continue;
                        }

                        string legality = set.Str(0).ToLowerInvariant();
                        if (!ShipClassMemberExists(legality))
                            Logger.LogInvalidValue(npcShipPath, set, "Invalid legality member!");

                        string shipclass_member = set.Str(1).ToLowerInvariant();
                        if (!ShipClassMemberExists(shipclass_member))
                            Logger.LogInvalidValue(npcShipPath, set, "Invalid shipclass member!");

                        o.shipclass_members.Add(shipclass_member);
                        o.desc = set.desc;

                        for (int i = 2; i < set.NumValues(); i++)
                        {
                            uint level;
                            string tlevel = set.Str(i);
                            if (!TryParseLevel(tlevel, out level))
                            {
                                Logger.LogInvalidValue(npcShipPath, set, "Invalid level!", tlevel);
                                continue;
                            }

                            if (!ShipClassMemberExists(tlevel.ToLowerInvariant()))
                                Logger.LogInvalidValue(npcShipPath, set, "Invalid shipclass member!", tlevel);

                            o.levels[level] = true;
                        }
                    }
                    else if (set.settingName == "ship_archetype")
                    {
                        string shipArch = set.Str(0).ToLowerInvariant();
                        if (!ShipChecks.ShipExists(shipArch))
                            Logger.LogInvalidValue(npcShipPath, set, "Ship archetype doesn't exist!");
                    }
                    else if (set.settingName.ToLowerInvariant() == "loadout")
                    {
                        if (!LoadoutChecks.LoadoutExists(set.Str(0)))
                            Logger.LogInvalidValue(npcShipPath, set, "Loadout doesn't exist!");
                    }
                }
                if (mission == null)
                    npcships[o.nickname] = o;
                else
                    missionNpcShips[mission].Add(o.nickname);
            }
        }

        /// <summary>
        /// Parses a level like "d5" into 5.
        /// </summary>
        static bool TryParseLevel(string str, out uint level)
        {
            level = 0;
            return str.Length > 1 && (str[0] == 'd' || str[0] == 'D') && uint.TryParse(str.Substring(1), out level);
        }

        static void LoadNPCShips()
        {
            CheckNPCShips("MISSIONS\\npcships.ini", null);
            foreach (var mission in Missions)
            {
                missionNpcShips[mission] = new HashSet<string>();
                CheckNPCShips("MISSIONS\\" + mission + "\\npcships.ini", mission);
            }
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
                if (sec.sectionName != "FactionProps")
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

                        o.formations[formation_by_class] = formation;

                        if (!formations.ContainsKey(formation))
                            Logger.LogInvalidValue(fileName, set, "Formation not found!");
                    }
                    else if (set.settingName == "npc_ship")
                    {
                        string npc_ship = set.Str(0).ToLowerInvariant();

                        if (!npcships.ContainsKey(npc_ship))
                            Logger.LogInvalidValue(fileName, set, "npc_ship not found!");

                        o.npc_ships.Add(npc_ship);
                    }
                }

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
                if (sec.sectionName == "EncounterParameters")
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
                    string encounter = "";
                    Dictionary<string, SYSTEM_ENCOUNTER> encounters = new Dictionary<string, SYSTEM_ENCOUNTER>();
                    foreach (FLDataFile.Setting set in sec.settings)
                    {
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
                            encounter = set.Str(0).ToLowerInvariant();
                            if (!encounterNicks.Contains(encounter))
                                Logger.LogInvalidValue(fileName, set, "Encounter not found!");

                            if (encounters.ContainsKey(encounter))
                                Logger.LogDublicateValue(fileName, set, "Duplicate encounter - this may be intentional.", LogLevel.warning);

                            encounters[encounter] = new SYSTEM_ENCOUNTER();
                            encounters[encounter].name = encounter;
                            encounters[encounter].desc = set.desc;
                            if (set.NumValues() < 2)
                                Logger.LogArgCount(fileName, set, 3);
                            else if (!uint.TryParse(set.Str(1), out encounters[encounter].level))
                                Logger.LogInvalidValue(fileName, set, "Has to be an integer!", set.Str(1));
                        }
                        else if (set.settingName.ToLowerInvariant() == "faction")
                        {
                            string faction = set.Str(0).ToLowerInvariant();
                            if (!factionprops.ContainsKey(faction))
                                Logger.LogInvalidValue(fileName, set, "Faction not found!");

                            // A faction belongs to the encounter defined before it
                            if (!encounters.ContainsKey(encounter))
                                Logger.LogInvalidValue(fileName, set, "Faction isn't preceded by an encounter!");
                            else
                                encounters[encounter].factions.Add(faction);
                        }
                    }


                    // Check the encounter information for the zone.
                    foreach (SYSTEM_ENCOUNTER e in encounters.Values)
                    {
                        if(!encounterFiles.ContainsKey(e.name))
                            continue;
                        string encounterFile = encounterFiles[e.name];
                        foreach (string faction in e.factions)
                        {
                            CheckEncounterForFaction(faction, encounterFile, e.desc);
                            CheckLevelForFaction(faction, encounterFile, e.level, e.desc);
                        }
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

        static void CheckLevelForFaction(string encounterFaction, string encounterFile, uint level, string referencedFrom)
        {
            if (!factionprops.ContainsKey(encounterFaction))
                return; // TODO

            FACTIONPROPS fp = factionprops[encounterFaction];
            foreach (string npc_ship in fp.npc_ships)
            {
                if (!npcships.ContainsKey(npc_ship))
                {
                    Logger.ELog(String.Format("{0}: missing npc_ship, npc_ship={1} ref={2}", encounterFile, npc_ship, referencedFrom));
                    continue;
                }

                if (npcships[npc_ship].levels.ContainsKey(level))
                    return;
            }

            Logger.WLog(String.Format("{0}: missing level for npc_ship referenced from faction_prop, level={1} ref={2}", fp.desc, level, referencedFrom)); 
        }

        // Helper function to check:
        // [Encounter] reference [ShipClass] via the ship_class parameter
        // [FactionProps] reference [NPCShipArch] via the npc_ship parameter
        // [NPCShipArch] reference [ShipClass] via the npc_class parameter.
        // The npc_class must be a member of the [ShipClass]
        static bool CheckFactionNpcShipAndEncounterShipClassAreValid(SHIPCLASS sc, FACTIONPROPS fp)
        {
            foreach (string npc_ship in fp.npc_ships)
            {
                if (npcships.ContainsKey(npc_ship))
                {
                    foreach (string shipclass_member in npcships[npc_ship].shipclass_members)
                    {
                        if (sc.members.Contains(shipclass_member))
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        static void CheckEncounterForFaction(string encounterFaction, string encounterFile, string referencedFrom)
        {
            if(!factionprops.ContainsKey(encounterFaction))
                return; // TODO
            FACTIONPROPS fp = factionprops[encounterFaction];

            FLDataFile file = new FLDataFile(encounterFile, true);
            foreach (FLDataFile.Section sec in file.sections)
            {
                if (sec.sectionName == "EncounterFormation")
                {
                    foreach (FLDataFile.Setting set in sec.settings)
                    {
                        if (set.NumValues() == 0)
                            continue;

                        // Check that ship_by_class parameter references a valid [ShipClass]
                        // (not member of shipclass).
                        // Check [Encounter] reference [ShipClass] via the ship_class parameter
                        // Check [FactionProps] reference [NPCShipArch] via the npc_ship parameter
                        // Check [NPCShipArch] reference [ShipClass] via the npc_class parameter.
                        // Check The npc_class must be a member of the [ShipClass]           
                        if (set.settingName == "ship_by_class")
                        {
                            if (set.NumValues() != 3 && set.NumValues() != 4)
                            {
                                if (reportedEncounterErrors.Add(set.desc + "|argcount"))
                                    Util.CheckNumberOfArgs(set, encounterFile, 3, 4);
                                continue;
                            }

                            // Check that the ship class exists
                            string ship_class = set.Str(2).ToLowerInvariant();
                            if (!shipclasses.ContainsKey(ship_class))
                            {
                                if (reportedEncounterErrors.Add(set.desc + "|shipclass"))
                                    Logger.ELog(String.Format("{0}: missing shipclass, ship_class={1} ref={2}", set.desc, ship_class, referencedFrom));
                                continue;
                            }

                            SHIPCLASS sc = shipclasses[ship_class];

                            if (!CheckFactionNpcShipAndEncounterShipClassAreValid(sc, fp))
                            {
                                Logger.ELog(String.Format("{0}: encounter npc_ship is not a member of ship class, ship_class={1} ref={2}", set.desc, ship_class, referencedFrom));
                                continue;
                            }
                            
                        }
                        // Check that make_class parameter references a valid [ShipClass] member
                        else if (set.settingName == "make_class")
                        {
                            string ship_class = set.Str(0).ToLowerInvariant();
                            if (!ShipClassMemberExists(ship_class))
                                Logger.ELog(String.Format("{0}: missing shipclass member, ship_class={1} ref={2}", set.desc, ship_class, referencedFrom));
                        }
                        // Check that the formation_by_class parameter references a formation class
                        // defined in the [FactionProps]
                        else if (set.settingName == "formation_by_class")
                        {
                            string formation_by_class = set.Str(0).ToLowerInvariant();

                            // this refers to the formation for the faction on the faction props
                            if (!fp.formations.ContainsKey(formation_by_class))
                                Logger.ELog(String.Format("{0}: missing formation_by_class, formation_by_class={1} ref={2}", set.desc, formation_by_class, referencedFrom));
                        }
                        else if (set.settingName == "formation")
                        {
                            string formation = set.Str(0).ToLowerInvariant();
                            if (!formations.ContainsKey(formation))
                                Logger.ELog(String.Format("{0}: missing formation, formation={1} ref={2}", set.desc, formation, referencedFrom));
                        }
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
                if (sec.sectionName == "system")
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

                LoadFormations();
                LoadShipClasses();
                LoadDefaultShipClassMembers();
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
