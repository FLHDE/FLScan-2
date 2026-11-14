using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Globalization;

using FLScanIE.Util_Functions;
using FLScanIE.Logging;

namespace FLScanIE
{
    public static class UniverseChecks
    {
        private static string[] universeFiles;
        private static Dictionary<string, string> baseFiles;
        private static string[] systemFiles;

        //                         sys     nicks
        private static Dictionary<string, string[]> baseNicks;
        private static string[] allBaseNicks;
        private static string[] systemNicks;

        //                         sys                type     nicks
        private static Dictionary<string, Dictionary<string, string[]>> objectNicks;

        //                         sys      systems it has a jump gate or hole to (all lowercase)
        private static Dictionary<string, HashSet<string>> systemConnections;

        // The system and object (lowercase) each jump gate or hole leads to. Key: "system:object"
        private static Dictionary<string, string[]> jumpTargets;

        // The game reads these files instead of computing the routes itself
        private static readonly string[] shortestPathFiles =
            { @"universe\shortest_illegal_path.ini", @"universe\shortest_legal_path.ini", @"universe\systems_shortest_path.ini" };

        private static string[] popTypes = Util.LoadConfig("ZonePopTypes");
        // Faction ([Group]) nicknames from initialworld.ini (lowercase), or null if that file isn't present
        private static HashSet<string> factionNicks;
        private static string[] hotspotNames = Util.LoadConfig("BaseHotspotNames");
        private static string[] hotspotBehaviors = Util.LoadConfig("BaseHotspotBehaviors");

        public static void CheckUniverseFolder()
        {
            Logger.ILog("Checking Universe-Folder");
            Util.RunChecks(CheckUniverseFile, universeFiles);
            Util.RunChecks(CheckBaseFile, baseFiles.Select(b => b.Value));
            Util.RunChecks(CheckShortestPathFile, shortestPathFiles);
            Logger.ILog("Finished Universe-Folder");
        }

        private static void CheckBaseFile(string file)
        {
            FLDataFile ini = new FLDataFile(Path.Combine(Checker.flDataPath, file), true);

            var rooms = ini.sections.Where(s => s.sectionName.ToLower() == "room").ToArray();
            // The game reads [Base] and [BaseInfo] the same way (common.dll)
            FLDataFile.Section baseInfo = ini.sections.FirstOrDefault(s => s.sectionName.ToLower() == "baseinfo" || s.sectionName.ToLower() == "base");

            var roomNicks = new List<string>();
            foreach (var room in rooms)
            {
                string roomNick = Util.TryGetStrSetting(room, "nickname");
                if (roomNick == null)
                    Logger.LogSettingNotFound(file, Util.GetSectionIdentifier(room), "Room", "nickname");
                else
                    roomNicks.Add(roomNick.ToLower());
            }
            var roomNickArray = roomNicks.ToArray();

            if (baseInfo == null)
                Logger.ELog(file + ": [BaseInfo] section not found!");
            else
            {
                string nick = Util.TryGetStrSetting(baseInfo, "nickname");
                string startRoom = Util.TryGetStrSetting(baseInfo, "start_room");
                if (startRoom == null)
                    Logger.LogSettingNotFound(file, nick ?? Util.GetSectionIdentifier(baseInfo), "BaseInfo", "start_room");
                else if (!roomNickArray.Contains(startRoom.ToLower()))
                    Logger.LogInvalidValue(file, baseInfo.GetSetting("start_room"), "Room doesn't exist!");
            }

            foreach (var room in rooms)
            {
                string roomFile = Util.TryGetStrSetting(room, "file");
                if(roomFile == null)
                    Logger.LogSettingNotFound(file, Util.GetSectionIdentifier(room), "Room", "file");
                else if (Checker.FileExistsOnDisk(roomFile))
                    CheckRoomFile(roomFile, ref roomNickArray);
                else if (!Checker.FileExists(roomFile)) // Vanilla files that aren't present can't be checked
                    Logger.LogFileNotFound(file, room.GetSetting("file"));
            }
        }

        private static void CheckRoomFile(string file, ref string[] roomNicks)
        {
            FLDataFile ini = new FLDataFile(Path.Combine(Checker.flDataPath, file), true);

            /*
            [Room_Sound] music  sound exists
            [Room_Sound] ambient  sound exists
            [Camera] name  TODO: thn?
            [CharacterPlacement] name  TODO: thn
            */

            // The game reads [Room] and [Room_Info] the same way (common.dll)
            var roomInfoSettings = ini.GetSettings("Room_Info").Concat(ini.GetSettings("Room")).ToArray();
            foreach (var script in roomInfoSettings.Where(s => s.settingName.ToLower() == "set_script").Union(ini.GetSettings("CharacterPlacement", "start_script")))
            {
                if (script.NumValues() > 0 && !Checker.FileExists(script.Str(0)))
                    Logger.LogFileNotFound(file, script);
            }

            // scene = [all,] ambient|traffic|once, script[, priority]. Like the game, the values end at the first
            // empty one, and a scene with any other number of values is ignored.
            foreach (var scene in roomInfoSettings.Where(s => s.settingName.ToLower() == "scene"))
            {
                string[] values = scene.values.Select(v => v.ToString().Trim()).TakeWhile(v => v != "").ToArray();
                if (values.Length > 0 && values[0].ToLower() == "all")
                    values = values.Skip(1).ToArray();
                if (values.Length != 2 && values.Length != 3)
                    Logger.LogInvalidValue(file, scene, "Has to be [all,] ambient|traffic|once, script[, priority]!");
                else if (values[0].ToLower() != "ambient" && values[0].ToLower() != "traffic" && values[0].ToLower() != "once")
                    Logger.LogInvalidValue(file, scene, "Has to be ambient, traffic or once!", values[0]);
                else if (!Checker.FileExists(values[1]))
                    Logger.LogFileNotFound(file, scene, values[1]);
            }

            foreach (var hotspot in ini.GetSettings("Hotspot"))
            {
                string val = hotspot.Str(0).ToLower();

                switch (hotspot.settingName)
                {
                    case "name":
                        if (!hotspotNames.Contains(val))
                            Logger.LogInvalidValue(file, hotspot, "Invalid name!");
                        break;
                    case "room_switch":
                    case "set_virtual_room":
                       // if (!roomNicks.Contains(val)) // TODO: spams like hell - find out which values allowed when the room doesn't exist
                       //     Logger.LogInvalidValue(file, hotspot, "Room doesn't exist!");
                        break;
                    case "behavior":
                        // The game only compares the first word
                        val = val.Split(',', ';', '/', ' ')[0];
                        if (!hotspotBehaviors.Contains(val))
                            Logger.LogInvalidValue(file, hotspot, "Invalid behavior!");
                        break;
                }
            }
        }

        private static void CheckUniverseFile(string file)
        {
            FLDataFile ini = new FLDataFile(Path.Combine(Checker.flDataPath, file), true);
            bool gotSystem = false;
            // The system at each map position, to find systems that share one
            var systemPositions = new Dictionary<string, string>();

            foreach (var section in ini.sections)
            {
                //string nick; unused
                switch (section.sectionName.ToLower())
                {
                    case "base":
                        if (gotSystem)
                        {
                            gotSystem = false;
                            Logger.WLog(file + ":" + (section.settings.Count == 0 ? "0" : section.settings[0].LineNumber.ToString()) + ": Base/System entries are in the wrong order!");
                        }
                        CheckBaseEntry(file, section);
                        break;
                    case "system":
                        gotSystem = true;
                        CheckSystemEntry(file, section, systemPositions);
                        break;
                    case "time":
                        break;
                    default:
                        Logger.WLog(string.Format("Unexpected section {0} in file {1}", section.sectionName, file));
                        break;
                }
            }
        }

        /// <summary>
        /// Checks a file with the routes between all systems: Path = from, to, from, ..., to. Each system on the
        /// route has to have a jump gate or hole to the next one.
        /// </summary>
        private static void CheckShortestPathFile(string file)
        {
            FLDataFile ini = new FLDataFile(Path.Combine(Checker.flDataPath, file), true);

            foreach (var path in ini.GetSettings("SystemConnections", "Path"))
            {
                if (path.NumValues() < 3)
                {
                    Logger.LogArgCount(file, path, 3);
                    continue;
                }

                string[] systems = path.values.Select(v => v.ToString().Trim().ToLower()).ToArray();
                // An unknown system is reported once per path
                string unknownSystem = systems.FirstOrDefault(sys => !SystemExists(sys));
                if (unknownSystem != null)
                {
                    Logger.LogInvalidValue(file, path, "System doesn't exist!", unknownSystem);
                    continue;
                }

                for (int i = 2; i < systems.Length - 1; i++)
                {
                    HashSet<string> connections;
                    if (!systemConnections.TryGetValue(systems[i], out connections) || !connections.Contains(systems[i + 1]))
                        Logger.LogInvalidValue(file, path, "There's no jump gate or hole between these systems!", systems[i] + " -> " + systems[i + 1]);
                }
            }
        }

        private static void CheckBaseEntry(string file, FLDataFile.Section section)
        {
            string nick = Util.TryGetStrSetting(section, "nickname");
            Util.CheckIDName(section, "strid_name", nick, file);

            foreach (var setting in section.settings)
            {
                if (setting.NumValues() == 0)
                    continue;
                string val = setting.Str(0);

                switch (setting.settingName.ToLower())
                {
                    case "file":
                        if (!Checker.FileExists(val))
                            Logger.LogFileNotFound(file, setting);
                        break;
                    case "system":
                        if(!SystemExists(val))
                            Logger.LogInvalidValue(file, setting, "System not found!");
                        break;
                    case "terrain_dyna_01":
                    case "terrain_dyna_02":
                    case "terrain_lrg":
                    case "terrain_mdm":
                    case "terrain_sml":
                    case "terrain_tiny":
                        if(!SolarChecks.AsteroidExists(val))
                            Logger.LogInvalidValue(file, setting, "Asteroid not found!");
                        break;

                    case "bgcs_base_run_by": // TODO
                        break;
                }
            }
        }

        private static void CheckSystemEntry(string file, FLDataFile.Section section, Dictionary<string, string> systemPositions)
        {
            string nick = Util.TryGetStrSetting(section, "nickname");
            if (nick == null)
                return; // Already reported while parsing
            nick = nick.ToLower();
            Util.CheckIDInfo(section, "ids_info", nick, file);
            Util.CheckIDName(section, "strid_name", nick, file);

            foreach (var setting in section.settings)
            {
                if (setting.NumValues() == 0)
                    continue;
                string val = setting.Str(0);

                switch (setting.settingName.ToLower())
                {
                    case "file":
                        string relSystemFile = Path.Combine("universe", val);
                        if (Checker.FileExistsOnDisk(relSystemFile))
                            CheckSystemFile(nick, val);
                        else if (!Checker.FileExists(relSystemFile)) // Vanilla files that aren't present can't be checked
                            Logger.LogFileNotFound(file, setting);
                        break;
                    case "pos": // x, y[, z]
                        if (!Util.CheckNumberOfArgs(setting, file, 2, 3))
                            break;
                        float x, y;
                        if (!float.TryParse(setting.Str(0), NumberStyles.Float, CultureInfo.InvariantCulture, out x)
                            || !float.TryParse(setting.Str(1), NumberStyles.Float, CultureInfo.InvariantCulture, out y))
                        {
                            Logger.LogInvalidValue(file, setting, "Has to be two numbers!");
                            break;
                        }
                        string pos = x.ToString(CultureInfo.InvariantCulture) + ", " + y.ToString(CultureInfo.InvariantCulture);
                        string otherSystem;
                        if (systemPositions.TryGetValue(pos, out otherSystem))
                            Logger.LogDublicateValue(file, setting, "Another system has the same position: " + otherSystem + "!", pos);
                        else
                            systemPositions[pos] = nick;
                        break;
                    case "msg_id_prefix":
                        //if(!AudioChecks.VoiceMsgExists(val)) // TODO
                        //    Logger.LogInvalidValue(file, setting, "Msg doesn't exist!");
                        break;
                }
            }
        }

        private static void CheckSystemFile(string sysnick, string file)
        {
            FLDataFile ini = new FLDataFile(Path.Combine(Path.Combine(Checker.flDataPath, "universe"), file), true);
            string val, nick;
            /*
[zone] attack_ids => ?
[zone] lane_id => ?, look attack_ids
[zone] density_restriction => Str(1) [ShipClass] 
             */
            foreach (var section in ini.sections)
            {
                switch (section.sectionName.ToLower())
                {
                    case "encounterparameters":
                        string enc_file = Util.TryGetStrSetting(section, "filename");

                        if (enc_file != null)
                            CheckEncounterFile(enc_file);
                        else
                            Logger.LogSettingNotFound(file, section.settings.Count == 0 ? "0" : section.settings[0].LineNumber.ToString(), "EncounterParameters", "file");
                        break;
                    case "nebula":
                        string neb_file = Util.TryGetStrSetting(section, "file");
                        string neb_zone = Util.TryGetStrSetting(section, "zone");

                        if (neb_file != null)
                            CheckNebulaFile(neb_file, sysnick, file, section.GetSetting("file"));
                        else
                            Logger.LogSettingNotFound(file, section.settings.Count == 0 ? "0" : section.settings[0].LineNumber.ToString(), "Nebula", "file");
                        if (neb_zone != null && !ZoneExists(sysnick, neb_zone))
                            Logger.LogInvalidValue(file, section.GetSetting("zone"), "Zone doesn't exist!");
                        break;
                    case "zone":
                        nick = Util.TryGetStrSetting(section, "nickname");
                        Util.CheckIDName(section, "ids_name", nick, file);
                        Util.CheckIDInfo(section, "ids_info", nick, file);

                        foreach (var setting in section.settings)
                        {
                            if (setting.NumValues() == 0)
                                continue;
                            val = setting.Str(0);

                            switch (setting.settingName.ToLower())
                            {
                                case "vignette_type":
                                    val = val.ToLower();
                                    if (val != "exclusion" && val != "field" && val != "open")
                                        Logger.LogInvalidValue(file, setting, "Invalid type!");
                                    break;
                                case "usage": // Any number of words
                                    foreach (string usage in setting.values.Select(v => v.ToString().Trim().ToLower()).TakeWhile(v => v != ""))
                                    {
                                        if (usage != "patrol" && usage != "trade")
                                            Logger.LogInvalidValue(file, setting, "Invalid usage!", usage);
                                    }
                                    break;
                                case "spacedust":
                                    if (!FXChecks.EffectExists(val))
                                        Logger.LogInvalidValue(file, setting, "Effect doesn't exist!");
                                    break;
                                case "shape":
                                    val = val.ToLower();
                                    if (val != "box" && val != "cylinder" && val != "ellipsoid" && val != "sphere" && val != "ring" && val != "mesh")
                                        Logger.LogInvalidValue(file, setting, "Invalid shape!");
                                    break;
                                case "pos":
                                case "rotate":
                                case "property_fog_color":
                                    Util.CheckNumberOfArgs(setting, file, 3);
                                    break;
                                case "pop_type":
                                    // A list of population types and factions
                                    foreach (var popType in setting.values.Select(v => v.ToString().Trim()))
                                    {
                                        if (!PopTypeValid(popType))
                                            Logger.LogInvalidValue(file, setting, "Invalid pop_type!", popType);
                                    }
                                    break;
                                case "music":
                                    if(!AudioChecks.SoundExists(val))
                                        Logger.LogInvalidValue(file, setting, "Sound not found!");
                                    break;
                                case "faction": // Checked by MissionChecks, which needs a [FactionProps] of the faction
                                case "edge_fraction":
                                    break;
                                case "faction_weight":
                                case "reputation":
                                    if (!FactionExists(val))
                                        Logger.LogInvalidValue(file, setting, "Faction doesn't exist!", val);
                                    break;
                                case "mission_type": // Any number of words
                                    foreach (string missionType in setting.values.Select(v => v.ToString().Trim().ToLower()).TakeWhile(v => v != ""))
                                    {
                                        if (missionType != "lawful" && missionType != "unlawful")
                                            Logger.LogInvalidValue(file, setting, "Invalid mission_type!", missionType);
                                    }
                                    break;
                                case "encounter":
                                    if (!ThingExistsInSystem(sysnick, val, "encounterparameters"))
                                        Logger.LogInvalidValue(file, setting, "Encounter doesn't exist!");
                                    break;
                            }
                        }
                        break;
                    case "systeminfo": // Updated 01/2024
                        string name = Util.TryGetStrSetting(section, "name");
                        string local_fact = Util.TryGetStrSetting(section, "local_faction");
                        string rpopd = Util.TryGetStrSetting(section, "rpop_solar_detection");
                        string farclip = Util.TryGetStrSetting(section, "space_farclip");

                        //if (name != null && name.ToLower() != sysnick)
                        //Logger.LogInvalidValue(file, section.GetSetting("name"), "Should equal system-nickname!");
                        if (name != null)
                            Logger.LogInvalidValue(file, section.GetSetting("name"), "Invalid line in file.");
                        if (local_fact != null && !FactionExists(local_fact))
                            Logger.LogInvalidValue(file, section.GetSetting("local_faction"), "Faction doesn't exist!");
                        if (rpopd != null)
                            if (rpopd.ToLower() != "false" && rpopd.ToLower() != "true" && rpopd != "0" && rpopd != "1")
                                Logger.LogInvalidValue(file, section.GetSetting("rpop_solar_detection"), "Must be true or false.");
                        if (farclip != null)
                        {
                            float farclip_float;
                            if (!float.TryParse(farclip, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out farclip_float))
                                Logger.LogInvalidValue(file, section.GetSetting("space_farclip"), "Invalid data type, expected numeric value.");
                            else if (farclip_float < 1000)
                                Logger.LogInvalidValue(file, section.GetSetting("space_farclip"), "The game raises values below 1000 to 1000.", LogLevel.warning);
                        }
                        break;
                    case "texturepanels":
                        val = Util.TryGetStrSetting(section, "file");
                        if (val != null && !Checker.FileExists(val))
                            Logger.LogFileNotFound(file, section.GetSetting("file"));
                        break;
                    case "archetype": // Updated 01/2024
                        foreach (var setting in section.settings)
                        {
                            switch (setting.settingName)
                            {
                                case "solar":
                                    break;
                                case "ship":
                                    break;
                                case "simple":
                                    break;
                                case "equipment":
                                    break;
                                case "snd":
                                    break;
                                case "voice":
                                    break;
                            }
                        }
                        break;
                    case "asteroids":
                        string ast_file = Util.TryGetStrSetting(section, "file");
                        string ast_zone = Util.TryGetStrSetting(section, "zone");

                        if (ast_file != null)
                            CheckAsteroidFile(ast_file, sysnick, file, section.GetSetting("file"));
                        else
                            Logger.LogSettingNotFound(file, section.settings.Count == 0 ? "0" : section.settings[0].LineNumber.ToString(), "Asteroids", "file");

                        if (ast_zone != null)
                        {
                            if (!ThingExistsInSystem(sysnick, ast_zone, "zone"))
                                Logger.LogInvalidValue(file, section.GetSetting("zone"), "Zone doesn't exist!");
                        }
                        else
                            Logger.LogSettingNotFound(file, section.settings.Count == 0 ? "0" : section.settings[0].LineNumber.ToString(), "Asteroids", "zone");
                        break;
                    case "music":
                        string music_battle = Util.TryGetStrSetting(section, "battle");
                        string music_danger = Util.TryGetStrSetting(section, "danger");
                        string music_space = Util.TryGetStrSetting(section, "space");

                        // Each track is optional: without one, the game plays no music in that situation
                        if (music_battle != null && !AudioChecks.SoundExists(music_battle))
                            Logger.LogInvalidValue(file, section.GetSetting("battle"), "Sound doesn't exist!");
                        if (music_danger != null && !AudioChecks.SoundExists(music_danger))
                            Logger.LogInvalidValue(file, section.GetSetting("danger"), "Sound doesn't exist!");
                        if (music_space != null && !AudioChecks.SoundExists(music_space))
                            Logger.LogInvalidValue(file, section.GetSetting("space"), "Sound doesn't exist!");

                        break;
                    case "background":
                        string basic_stars = Util.TryGetStrSetting(section, "basic_stars");
                        string complex_stars = Util.TryGetStrSetting(section, "complex_stars");
                        string bg_file = Util.TryGetStrSetting(section, "file"); // The same as complex_stars
                        string nebulae = Util.TryGetStrSetting(section, "nebulae");

                        if (basic_stars != null)
                        {
                            if (!Checker.FileExists(basic_stars))
                                Logger.LogFileNotFound(file, section.GetSetting("basic_stars"));
                        }
                        //else
                        //    Logger.LogSettingNotFound(file, section.settings.Count == 0 ? "0" : section.settings[0].LineNumber.ToString(), "Background", "basic_stars");

                        if (complex_stars != null)
                        {
                            if (!Checker.FileExists(complex_stars))
                                Logger.LogFileNotFound(file, section.GetSetting("complex_stars"));
                        }
                        //else
                        //    Logger.LogSettingNotFound(file, section.settings.Count == 0 ? "0" : section.settings[0].LineNumber.ToString(), "Background", "complex_stars");

                        if (bg_file != null && !Checker.FileExists(bg_file))
                            Logger.LogFileNotFound(file, section.GetSetting("file"));
                        if (nebulae != null && !Checker.FileExists(nebulae))
                            Logger.LogFileNotFound(file, section.GetSetting("nebulae"));

                        break;
                    case "dust":
                        string dust_spacedust = Util.TryGetStrSetting(section, "spacedust");
                        if(dust_spacedust != null && !FXChecks.EffectExists(dust_spacedust))
                            Logger.LogInvalidValue(file, section.GetSetting("spacedust"), "Effect doesn't exist!");
                        break;
                    case "lightsource": // Updated 01/2024
                        /*string ls_pos = Util.TryGetStrSetting(section, "pos"); TODO
                        string ls_rot = Util.TryGetStrSetting(section, "rotate");
                        string ls_color = Util.TryGetStrSetting(section, "color");
                        string ls_range = Util.TryGetStrSetting(section, "range");
                        string ls_atten = Util.TryGetStrSetting(section, "attenuation");*/
                        string ls_behavior = Util.TryGetStrSetting(section, "behavior");
                        string ls_type = Util.TryGetStrSetting(section, "type");

                       /* if (ls_pos != null)
                            string pos_x = ls_pos.Str(0);
                            string pos_y = ls_pos.Str(1);
                            string pos_z = ls_pos.Str(2);
                            float posx;
                            float posy;
                            float posz;
                            if (!Util.CheckNumberOfArgs(ls_pos, file, 3) && float.TryParse(posx, out pos_x))
                                Logger.LogInvalidValue(file, section.GetSetting("pos"), "Position must have three number values: 0, 0, 0");
                       */
                        // The curves are [IGraph]s from igraph.ini. The game shows a message box for an unknown one.
                        foreach (var curve in section.settings.Where(s => s.NumValues() > 0))
                        {
                            string curveKey = curve.settingName.ToLower();
                            if ((curveKey == "atten_curve" || curveKey == "ambient_curve" || curveKey == "color_curve") && !FXChecks.GraphExists(curve.Str(0)))
                                Logger.LogInvalidValue(file, curve, "Graph doesn't exist in igraph.ini!", curve.Str(0));
                        }

                        if (ls_behavior != null) // The game ignores keys it doesn't know
                            Logger.LogInvalidValue(file, section.GetSetting("behavior"), "Not read by the game.", LogLevel.warning);
                        /*{
                            if (ls_behavior.ToLower() != "nothing")
                                Logger.LogInvalidValue(file, section.GetSetting("behavior"), "Has to be NOTHING.");
                        }
                        else
                            Logger.LogSettingNotFound(file, section.settings.Count == 0 ? "0" : section.settings[0].LineNumber.ToString(), "LightSource", "behavior");
                        */
                        if (ls_type != null)
                        {
                            string lightType = ls_type.ToLower();
                            if (lightType != "ambient" && lightType != "point" && lightType != "cone" && lightType != "directional")
                                Logger.LogInvalidValue(file, section.GetSetting("type"), "Has to be AMBIENT, POINT, CONE or DIRECTIONAL.");
                        }
                        else
                            Logger.LogSettingNotFound(file, section.settings.Count == 0 ? "0" : section.settings[0].LineNumber.ToString(), "LightSource", "type");
                        break;
                    case "object":
                        nick = Util.TryGetStrSetting(section, "nickname");
                        string o_pilot = Util.TryGetStrSetting(section, "pilot");
                        if (o_pilot != null && !MissionFileChecks.PilotExists(o_pilot))
                            Logger.LogInvalidValue(file, section.GetSetting("pilot"), "Pilot doesn't exist!");
                        Util.CheckIDName(section, "ids_name", nick, file);
                        Util.CheckIDInfo(section, "ids_info", nick, file);

                        string o_prevRing = null;
                        string o_nextRing = null;

                        foreach (var setting in section.settings)
                        {
                            if(setting.settingName == "260800" && file.ToLower().EndsWith("iw01.ini")) // fix for a error in the original file
                                continue;

                            if(setting.NumValues() == 0)
                                continue;

                            val = setting.Str(0);

                            string set = setting.settingName.ToLower();

                            if (set == "prev_ring")
                                o_prevRing = val;
                            else if (set == "next_ring")
                                o_nextRing = val;

                            switch (set)
                            {
                                case "voice":
                                    if (!AudioChecks.VoiceExists(val))
                                        Logger.LogInvalidValue(file, setting, "Voice not found!");
                                    break;
                                case "tradelane_space_name":
                                    uint ids;
                                    if (!uint.TryParse(val, out ids))
                                        Logger.LogInvalidValue(file, setting, "Has to be a number!");
                                    else if (!Infocards.IsName(ids) && !Infocards.IsInUnloadedDll(ids))
                                        Logger.LogIDError(file, setting);
                                    break;
                                case "star":
                                    if (!SolarChecks.StarExists(val))
                                        Logger.LogInvalidValue(file, setting, "Star not found!");
                                    break;
                                case "space_costume":
                                    if (!Util.CheckNumberOfArgs(setting, file, 2, 3))
                                        break;

                                    string head = setting.Str(0);
                                    string body = setting.Str(1);
                                    string acces = null;
                                    if (setting.NumValues() == 3)
                                        acces = setting.Str(2);

                                    if (head.Length != 0 && !CharacterChecks.BodypartExists(head, "head") )
                                        Logger.LogInvalidValue(file, setting, "Head not found!", head);
                                    if (!CharacterChecks.BodypartExists(body, "body"))
                                        Logger.LogInvalidValue(file, setting, "Body not found!", body);
                                    if (acces != null && !CharacterChecks.BodypartExists(acces, "accessory"))
                                        Logger.LogInvalidValue(file, setting, "Accessory not found!", acces);
                                    break;
                                case "pos":
                                case "rotate":
                                case "spin":
                                    Util.CheckNumberOfArgs(setting, file, 3);
                                    break;
                                case "ring":
                                    if (!Util.CheckNumberOfArgs(setting, file, 2))
                                        break;
                                    string ring_zone = setting.Str(0);
                                    string ring_file = setting.Str(1);
                                    CheckRingFile(ring_file, sysnick, file, setting);
                                    if(!ZoneExists(sysnick, ring_zone))
                                        Logger.LogInvalidValue(file, setting, "Zone doesn't exist!", ring_zone);
                                    break;
                                case "reputation":
                                case "faction":
                                    if (!FactionExists(val))
                                        Logger.LogInvalidValue(file, setting, "Faction doesn't exist!");
                                    break;
                                case "parent":
                                case "prev_ring":
                                case "next_ring":
                                    if(!ObjectExists(sysnick, val))
                                        Logger.LogInvalidValue(file, setting, "Object doesn't exist!");
                                    break;
                                case "msg_id_prefix":
                                    //if(!AudioChecks.VoiceMsgExists(val)) // TODO spams like hell because the messages are defined as "gcs_refer_system_hi02-", but "gcs_refer_system_hi02" is referenced 
                                    //    Logger.LogInvalidValue(file, setting, "Msg doesn't exist!");
                                    break;
                                case "loadout":
                                    if(!LoadoutChecks.LoadoutExists(val))
                                        Logger.LogInvalidValue(file, setting, "Loadout doesn't exist!");
                                    break;
                                case "jump_effect":
                                    if(!FXChecks.EffectExists(val))
                                        Logger.LogInvalidValue(file, setting, "Effect doesn't exist!");
                                    break;
                                case "goto":
                                    if(!Util.CheckNumberOfArgs(setting, file, 3))
                                        break;
                                    string goto_sys = setting.Str(0);
                                    string goto_obj = setting.Str(1);
                                    string goto_eff = setting.Str(2);
                                    string[] back;
                                    if(!SystemExists(goto_sys))
                                        Logger.LogInvalidValue(file, setting, "System doesn't exist!", goto_sys);
                                    else if(!ObjectExists(goto_sys, goto_obj))
                                        Logger.LogInvalidValue(file, setting, "Object doesn't exist!", goto_obj);
                                    // The jump gate or hole on the other side has to lead back to this one
                                    else if (nick != null && (!jumpTargets.TryGetValue(goto_sys.ToLower() + ":" + goto_obj.ToLower(), out back)
                                        || back[0] != sysnick.ToLower() || back[1] != nick.ToLower()))
                                        Logger.LogInvalidValue(file, setting, "The target doesn't lead back to this object!", goto_obj);
                                    if(!FXChecks.EffectExists(goto_eff))
                                        Logger.LogInvalidValue(file, setting, "Effect doesn't exist!", goto_eff);
                                    break;
                                case "base":
                                case "dock_with":
                                    if(!BaseExists(val))
                                        Logger.LogInvalidValue(file, setting, "Base doesn't exist!");
                                    break;
                                case "behavior":
                                    if (!LeaderStateGraphExists(val))
                                        Logger.LogInvalidValue(file, setting, "Invalid behavior! Has to be a leader state graph from AI\\state_graph.db.");
                                    break;
                                case "archetype":
                                    if(!SolarChecks.ArchetypeExists(val))
                                        Logger.LogInvalidValue(file, setting, "Archetype doesn't exist!");
                                    break;
                            }
                        }
                        
                        if(o_prevRing == o_nextRing && o_prevRing != null)
                            Logger.LogInvalidValue(file, section.GetSetting("prev_ring"), "prev_ring equals next_ring!");
                        if (nick != null && o_prevRing != null && o_prevRing.ToLower() == nick.ToLower())
                            Logger.LogInvalidValue(file, section.GetSetting("prev_ring"), "The ring points to itself!");
                        if (nick != null && o_nextRing != null && o_nextRing.ToLower() == nick.ToLower())
                            Logger.LogInvalidValue(file, section.GetSetting("next_ring"), "The ring points to itself!");
                        break;
                    case "ambient": // TODO [Ambient] color 3 args
                        break;
                    case "field":
                        break;
                    case "asteroidbillboards":
                        break;
                    default:
                        Logger.WLog(string.Format("Unexpected section {0} in file {1}", section.sectionName, file));
                        break;
                }
            }
        }

        /// <summary>
        /// Loads an ini file (relative to DATA) referenced by <paramref name="refSetting"/> in <paramref name="refFile"/>.
        /// Logs a missing file and returns null if it doesn't exist or is a vanilla file that isn't present.
        /// </summary>
        private static FLDataFile LoadReferencedIni(string file, string refFile, FLDataFile.Setting refSetting)
        {
            if (Checker.FileExistsOnDisk(file))
                return new FLDataFile(Path.Combine(Checker.flDataPath, file), false);
            if (!Checker.FileExists(file))
                Logger.LogFileNotFound(refFile, refSetting, file);
            return null;
        }

        private static void CheckNebulaFile(string file, string sys, string refFile, FLDataFile.Setting refSetting)
        {
            FLDataFile ini = LoadReferencedIni(file, refFile, refSetting);
            if (ini == null)
                return;
            string[] shapes = new string[0];
            if (ini.SettingExists("TexturePanels", "file"))
            {
                FLDataFile.Setting shapesSet = ini.GetSetting("TexturePanels", "file");
                shapes = Util.ParseShapeFile(shapesSet.Str(0), shapesSet, file);
            }

            CheckFieldFile(ini, shapes, file, sys);
        }

        private static void CheckEncounterFile(string file)
        {
            // TODO
        }

        private static void CheckAsteroidFile(string file, string sys, string refFile, FLDataFile.Setting refSetting)
        {
/* TODO
[TexturePanels]
file = solar\asteroids\rock_shapes.ini

[DynamicAsteroids]
asteroid = DAsteroid_mineable_small1

[Cube]
asteroid = mine_spike_minedout

[LootableZone]
asteroid_loot_container = lootcrate_ast_loot_gold
asteroid_loot_commodity = commodity_gold*/
            FLDataFile ini = LoadReferencedIni(file, refFile, refSetting);
            if (ini == null)
                return;
            string[] shapes = new string[0];
            if (ini.SettingExists("TexturePanels", "file"))
            {
                FLDataFile.Setting shapesSet = ini.GetSetting("TexturePanels", "file");
                shapes = Util.ParseShapeFile(shapesSet.Str(0), shapesSet, file);
            }

            CheckFieldFile(ini, shapes, file, sys);
        }

        private static void CheckRingFile(string file, string sys, string refFile, FLDataFile.Setting refSetting)
        {
            // TODO
            FLDataFile ini = LoadReferencedIni(file, refFile, refSetting);
            if (ini == null)
                return;
            string[] shapes = new string[0];
            if (ini.SettingExists("TexturePanels", "file"))
            {
                FLDataFile.Setting shapesSet = ini.GetSetting("TexturePanels", "file");
                shapes = Util.ParseShapeFile(shapesSet.Str(0), shapesSet, file);
            }

            CheckFieldFile(ini, shapes, file, sys);
        }

        /// <summary>
        /// True if <paramref name="shape"/> is set but not defined. <paramref name="shapes"/> is null if the
        /// defined shapes are unknown, in which case nothing is reported.
        /// </summary>
        private static bool ShapeMissing(string[] shapes, string shape)
        {
            return shape != null && shapes != null && !shapes.Contains(shape.ToLower());
        }

        private static void CheckFieldFile(FLDataFile ini, string[] shapes, string file, string sys)
        {
            foreach (var sec in ini.sections)
            {
                switch (sec.sectionName.ToLower())
                {
                    case "properties":
                        string pro_flag = Util.TryGetStrSetting(sec, "flag");
                        // TODO
                        /* rock_objects gas_danger_objects mine_danger_objects danger_density_med danger_density_low danger_density_high Object_density_low Object_density_med Object_density_high nebula*/
                        break;
                    case "exclusion zones":
                        // One section lists many exclusion zones, each with its own optional settings
                        foreach (var setting in sec.settings)
                        {
                            if (setting.NumValues() == 0)
                                continue;

                            switch (setting.settingName.ToLower())
                            {
                                case "exclusion":
                                    if (!ZoneExists(sys, setting.Str(0)))
                                        Logger.LogInvalidValue(file, setting, "Zone not found!");
                                    break;
                                case "zone_shell":
                                    Util.CheckFileExists(setting, file, Checker.flDataPath);
                                    break;
                            }
                        }
                        break;
                    case "band":
                    case "asteroidbillboards":
                    case "exterior":
                        string ext_shape = Util.TryGetStrSetting(sec, "shape");
                        string ext_fillshape = Util.TryGetStrSetting(sec, "fill_shape");
                        string ext_detshape = Util.TryGetStrSetting(sec, "detail_shape");
                        if (ShapeMissing(shapes, ext_shape))
                            Logger.LogInvalidValue(file, sec.GetSetting("shape"), "Shape not found!");
                        if (ShapeMissing(shapes, ext_fillshape))
                            Logger.LogInvalidValue(file, sec.GetSetting("fill_shape"), "Shape not found!");
                        if (ShapeMissing(shapes, ext_detshape))
                            Logger.LogInvalidValue(file, sec.GetSetting("detail_shape"), "Shape not found!");
                        break;
                    case "clouds":
                        string cl_shape = Util.TryGetStrSetting(sec, "puff_shape");
                        if (ShapeMissing(shapes, cl_shape))
                            Logger.LogInvalidValue(file, sec.GetSetting("puff_shape"), "Shape not found!");
                        break;
                }
            }
        }

        private const string stateGraphFile = @"AI\state_graph.db";
        // The leader state graphs of vanilla's AI\state_graph.db
        private static readonly string[] vanillaLeaderStateGraphs = { "nothing", "player", "fighter", "transport", "gunboat", "cruiser" };
        private static HashSet<string> leaderStateGraphs;

        /// <summary>
        /// True if AI\state_graph.db has a leader state graph with this name. Content.dll looks up the behavior of
        /// a system object with pub::StateGraph::get_state_graph(name, LEADER), which ignores the case.
        /// </summary>
        public static bool LeaderStateGraphExists(string name)
        {
            if (leaderStateGraphs == null)
            {
                // The file lists each state graph as its name, followed by its type (LEADER or ESCORT) on the
                // next line, followed by its numbers
                string[] names = vanillaLeaderStateGraphs;
                if (Checker.FileExistsOnDisk(stateGraphFile))
                {
                    string[] lines = File.ReadAllLines(Path.Combine(Checker.flDataPath, stateGraphFile)).Select(l => l.Trim()).ToArray();
                    names = lines.Where((l, i) => i + 1 < lines.Length && lines[i + 1].ToUpper() == "LEADER").Select(l => l.ToLower()).ToArray();
                }
                leaderStateGraphs = new HashSet<string>(names);
            }
            return leaderStateGraphs.Contains(name.Trim().ToLower());
        }

        public static void ParseUniverseFolder()
        {
            Logger.ILog("Parsing Universe-Folder");
            leaderStateGraphs = null;
            universeFiles = Checker.flIni.GetSettings("Data", "universe").Select(s => s.Str(0)).ToArray();
            List<string> systemNicks = new List<string>();
            var baseNicks = new Dictionary<string, List<string>>();
            var objectNicks = new Dictionary<string, Dictionary<string, List<string>>>();
            baseFiles = new Dictionary<string, string>();
            systemConnections = new Dictionary<string, HashSet<string>>();
            jumpTargets = new Dictionary<string, string[]>();

            foreach (var universeFile in universeFiles)
            {
                ParseUniverseFile(universeFile, ref systemNicks, ref baseNicks, ref objectNicks);
            }

            UniverseChecks.baseNicks = baseNicks.Select(s => new KeyValuePair<string, string[]>(s.Key, s.Value.ToArray())).ToDictionary(s => s.Key, s => s.Value);
            allBaseNicks = baseNicks.SelectMany(s => s.Value).ToArray();
            UniverseChecks.systemNicks = systemNicks.ToArray();

            UniverseChecks.objectNicks = objectNicks.ToDictionary(o => o.Key, o => o.Value.ToDictionary(s => s.Key, s => s.Value.ToArray()));

            // initialworld.ini isn't referenced by freelancer.ini, but it's always loaded by the game
            factionNicks = null;
            FLDataFile initialWorld = Checker.LoadIni("initialworld.ini");
            if (initialWorld != null)
            {
                factionNicks = new HashSet<string>(initialWorld.GetSettings("Group", "nickname")
                    .Where(s => s.NumValues() > 0).Select(s => s.Str(0).ToLower()));
            }

            Logger.ILog("Finished Universe-Folder");
        }

        private static void ParseUniverseFile(string file, ref List<string> systemNicks, ref Dictionary<string, List<string>> baseNicks, ref Dictionary<string, Dictionary<string, List<string>>> objectNicks)
        {
            // Missing files are reported by Checker.Parse
            FLDataFile ini = Checker.LoadIni(file);
            if (ini == null)
                return;

            foreach (var section in ini.sections)
            {
                string nick;
                switch (section.sectionName.ToLower())
                {
                    case "base":
                        nick = Util.TryGetStrSetting(section, "nickname");
                        string sys = Util.TryGetStrSetting(section, "system");
                        string bfile = Util.TryGetStrSetting(section, "file");

                        if (nick == null || sys == null)
                        {
                            if (nick == null)
                                Logger.LogSettingNotFound(file, section.settings.Count == 0 ? "0" : section.settings[0].LineNumber.ToString(), "Base", "nickname");
                            if (sys == null)
                                Logger.LogSettingNotFound(file, section.settings.Count == 0 ? "0" : section.settings[0].LineNumber.ToString(), "Base", "system");
                            if (bfile == null)
                                Logger.LogSettingNotFound(file, section.settings.Count == 0 ? "0" : section.settings[0].LineNumber.ToString(), "Base", "file");
                            continue;
                        }
                        if(!baseNicks.ContainsKey(sys))
                            baseNicks[sys] = new List<string>();
                        baseNicks[sys].Add(nick.ToLower());

                        if (bfile == null)
                        {
                            Logger.LogSettingNotFound(file, nick, "Base", "file");
                            break;
                        }
                        if (!Checker.FileExists(bfile))
                        {
                            Logger.LogFileNotFound(file, section.GetSetting("file"));
                            break;
                        }
                        if (!Checker.FileExistsOnDisk(bfile))
                            break; // Vanilla files that aren't present can't be checked
                        Util.AddUnique(baseFiles, nick.ToLower(), bfile, file, section, "base");
                        break;
                    case "system":
                        nick = Util.TryGetStrSetting(section, "nickname");
                        string sfile = Util.TryGetStrSetting(section, "file");

                        if (sfile == null || nick == null)
                        {
                            if(sfile == null)
                                Logger.LogSettingNotFound(file, section.settings.Count == 0 ? "0" : section.settings[0].LineNumber.ToString(), "System", "file");
                            if (nick == null)
                                Logger.LogSettingNotFound(file, section.settings.Count == 0 ? "0" : section.settings[0].LineNumber.ToString(), "System", "nickname");
                            continue;
                        }

                        nick = nick.ToLower();
                        systemNicks.Add(nick);
                        if(!objectNicks.ContainsKey(nick))
                            objectNicks[nick] = new Dictionary<string, List<string>>();
                        ParseSystemFile(sfile, nick, ref objectNicks);
                        break;
                    case "time":
                        break;
                }
            }
        }

        private static void ParseSystemFile(string file, string system, ref Dictionary<string, Dictionary<string, List<string>>> objectNicks)
        {
            // Missing files are reported by CheckSystemEntry
            FLDataFile ini = Checker.LoadIni(Path.Combine("universe", file));
            if (ini == null)
                return;

            foreach (var section in ini.sections)
            {
                section.sectionName = section.sectionName.ToLower();
                switch (section.sectionName)
                {
                    case "encounterparameters":
                    case "lightsource":
                    case "object":
                    case "zone":
                        string nick = Util.TryGetStrSetting(section, "nickname");
                        if (nick == null)
                        {
                            Logger.LogSettingNotFound(file, Util.GetSectionIdentifier(section), section.sectionName, "nickname");
                            break;
                        }
                        if (!objectNicks[system].ContainsKey(section.sectionName))
                            objectNicks[system][section.sectionName] = new List<string>();
                        objectNicks[system][section.sectionName].Add(nick.ToLower());

                        // goto = system, object, effect
                        var gotoSetting = section.sectionName == "object" ? Util.TryGetSetting(section, "goto") : null;
                        if (gotoSetting != null && gotoSetting.NumValues() > 0)
                        {
                            string gotoSystem = gotoSetting.Str(0).Trim().ToLower();
                            if (!systemConnections.ContainsKey(system))
                                systemConnections[system] = new HashSet<string>();
                            systemConnections[system].Add(gotoSystem);
                            if (gotoSetting.NumValues() > 1)
                                jumpTargets[system + ":" + nick.ToLower()] = new[] { gotoSystem, gotoSetting.Str(1).Trim().ToLower() };
                        }
                        break;

                    case "field":
                    case "ambient":
                    case "asteroidbillboards":

                    case "systeminfo":
                    case "background":
                    case "archetype":
                    case "texturepanels":
                    case "dust":
                    case "music":
                    case "asteroids":
                    case "nebula":
                        break;
                }
            }
        }

        /// <summary>
        /// True if the value is a known population type or a faction nickname. If the factions are unknown
        /// (initialworld.ini isn't present), any value that isn't a known population type is accepted.
        /// </summary>
        private static bool PopTypeValid(string popType)
        {
            popType = popType.ToLower();
            return popTypes.Contains(popType) || factionNicks == null || factionNicks.Contains(popType);
        }

        /// <summary>
        /// True if a faction ([Group] in initialworld.ini) with this nickname exists. If the factions are unknown
        /// (initialworld.ini isn't present), every faction is accepted.
        /// </summary>
        public static bool FactionExists(string nick)
        {
            return factionNicks == null || factionNicks.Contains(nick.ToLower());
        }

        public static bool SystemExists(string nick)
        {
            nick = nick.ToLower();
            return systemNicks.Contains(nick);
        }

        public static bool ObjectExists(string sys, string nick)
        {
            return ThingExistsInSystem(sys, nick, "object");
        }

        public static bool ZoneExists(string sys, string nick)
        {
            return ThingExistsInSystem(sys, nick, "zone");
        }

        public static bool ThingExistsInSystem(string sys, string nick, string name)
        {
            Dictionary<string, string[]> things;
            string[] nicks;
            // Systems without e.g. any zone don't have an entry for that type at all.
            return objectNicks.TryGetValue(sys.ToLower(), out things)
                && things.TryGetValue(name, out nicks)
                && nicks.Contains(nick.ToLower());
        }

        public static bool BaseExists(string nick)
        {
            nick = nick.ToLower();
            return allBaseNicks.Contains(nick);
        }
    }
}
