using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using FLScanIE.Logging;

namespace FLScanIE.Util_Functions
{
    public static class Util
    {
        public static string[][] GetNicks(string flini_section, string flini_name, string section, string name)
        {
            return GetNicks(new[] { flini_section }, new[] { flini_name }, new[] { section }, new[] { name });
        }

        public static string[][] GetNicks(string flini_section, string[] flini_names, string section, string name)
        {
            return GetNicks(new[] { flini_section }, flini_names, new[] { section }, new[] { name });
        }

        public static string[][] GetNicks(string flini_section, string flini_name, string[] sections, string name)
        {
            return GetNicks( new[] { flini_section }, new[] { flini_name }, sections, new[] { name });
        }

        public static string[][] GetNicks(string[] flini_sections, string[] flini_names, string[] sections, string[] names)
        {
            var inis = flini_sections.SelectMany(ss => Checker.flIni.GetSettings(ss).Where(s => flini_names.Contains(s.settingName)).Select(s => s.Str(0))).ToArray();
            var nicks = new List<string>();

            foreach (var ini in inis)
            {
                // Missing files are reported by Checker.Parse
                FLDataFile file = Checker.LoadIni(ini);
                if (file == null)
                    continue;

                foreach (var section in sections)
                    nicks.AddRange(file.GetSettings(section).Where(s => names.Contains(s.settingName)).Select(s => s.Str(0)));
            }

            return new string[][] { inis, nicks.ToArray() };
        }

        public static bool CheckNumberOfArgs(FLDataFile.Section section, string file, string setting, int num)
        {
            if (section.SettingExists(setting))
                return CheckNumberOfArgs(section.GetSetting(setting), file, num);
            return false;
        }

        public static bool CheckNumberOfArgs(FLDataFile.Setting setting, string file, params int[] nums)
        {
            if (!nums.Contains(setting.NumValues()))
            {
                Logger.LogArgCount(file, setting, nums);
                return false;
            }
            return true;
        }

        public static bool CheckFileExists(FLDataFile.Section section, string file, string setting, string prefix)
        {
            if (section.SettingExists(setting))
                return CheckFileExists(section.GetSetting(setting), file, prefix);
            return false;
        }

        public static bool CheckFileExists(FLDataFile.Setting setting, string file, string prefix)
        {
            if (setting.NumValues() == 1 && !File.Exists(Path.Combine(prefix, setting.Str(0))))
            {
                // Check whether the referenced file (not the ini referencing it) is a vanilla file
                if (Checker.HasVanillaFile(Path.Combine(prefix, setting.Str(0))))
                    return true;

                Logger.LogFileNotFound(file, setting);
                return false;
            }
            return true;
        }

        public static bool CheckIDName(FLDataFile.Setting setting, object identifier, string file)
        {
            return CheckID(setting, identifier, file, false);
        }

        public static bool CheckIDInfo(FLDataFile.Setting setting, object identifier, string file)
        {
            return CheckID(setting, identifier, file, true);
        }

        public static bool CheckIDName(FLDataFile.Section section, string setting, object identifier, string file)
        {
            return CheckID(section, setting, identifier, file, false);
        }

        public static bool CheckIDInfo(FLDataFile.Section section, string setting, object identifier, string file)
        {
            return CheckID(section, setting, identifier, file, true);
        }

        public static bool CheckID(FLDataFile.Section section, string setting, object identifier, string file, bool infocard)
        {
            if (section.SettingExists(setting))
            {
                return CheckID(section.GetSetting(setting), identifier, file, infocard);
            }
            else
            {
                //Logger.LogSettingNotFound(file, identifier, section.sectionName, "ids_info");
                return false;
            }
            //return true; unused
        }

        public static bool CheckID(FLDataFile.Setting setting, object identifier, string file, bool infocard)
        {
            string str_ids = setting.Str(0);

            if (str_ids != null)
            {
                uint ids;
                if (!uint.TryParse(str_ids, out ids))
                {
                    Logger.LogInvalidValue(file, setting, "Has to be a number!");
                    return false;
                }
                // 0 means "no name/infocard". IDs of a DLL that couldn't be loaded can't be checked (already reported).
                else if (ids != 0 && !Infocards.IsInUnloadedDll(ids))
                {
                    // Names have to be strings. Infocards are usually XML infocards, but some refer to strings.
                    bool found = infocard ? Infocards.IsInfocard(ids) || Infocards.IsName(ids) : Infocards.IsName(ids);
                    if (!found)
                    {
                        Logger.LogIDError(file, setting);
                        return false;
                    }
                }
            }
            else
            {
                //Logger.LogSettingNotFound(file, identifier, section.sectionName, "ids_info");
                return false;
            }
            return true;
        }

        public struct NickUtfTuple
        {
            public string Nick;
            public UtfFile UtfFile;
        }

        public static NickUtfTuple ParseNickUTF(string flDataPath, string file, FLDataFile.Section section)
        {
            return ParseNickUTF(flDataPath, file, section, true);
        }

        public static NickUtfTuple ParseNickUTF(string flDataPath, string file, FLDataFile.Section section, bool errorUtf)
        {
            return ParseNickUTF(flDataPath, file, section, "nickname", DaArchetypeKey(section), errorUtf);
        }

        // The object types of DecodeType in common.dll (compared case-insensitively)
        private static readonly HashSet<string> objectTypes = new HashSet<string>(new[] { "NONE", "MOON", "PLANET", "SUN",
            "ASTEROID", "BLACKHOLE", "SATELLITE", "DOCKING_RING", "JUMP_GATE", "JUMP_HOLE", "WEAPONS_PLATFORM",
            "DESTROYABLE_DEPOT", "NON_TARGETABLE", "MISSION_SATELLITE", "AIRLOCK_GATE", "TRADELANE_RING", "STATION",
            "WAYPOINT", "FIGHTER", "FREIGHTER", "GUNBOAT", "CRUISER", "TRANSPORT", "CAPITAL", "MINING", "GUIDED", "MINE",
            "LOOT", "BULLET" }, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Checks the type of an archetype (the key is read for every archetype). The game warns about an unknown
        /// type and uses NONE.
        /// </summary>
        public static void CheckObjectType(string file, FLDataFile.Setting setting)
        {
            if (setting.NumValues() > 0 && !objectTypes.Contains(setting.Str(0).Trim()))
                Logger.LogInvalidValue(file, setting, "Unknown object type! The game warns and uses NONE.");
        }

        /// <summary>
        /// The key that gives the model of an archetype: DA_archetype, or its alias attachment_archetype. Common.dll
        /// reads both into the same field, so the last one is used.
        /// </summary>
        public static string DaArchetypeKey(FLDataFile.Section section)
        {
            var last = section.settings.LastOrDefault(s => s.settingName.ToLowerInvariant() == "da_archetype"
                || s.settingName.ToLowerInvariant() == "attachment_archetype");
            return last == null ? "DA_archetype" : last.settingName;
        }

        public static NickUtfTuple ParseNickUTF(string flDataPath, string file, FLDataFile.Section section, string nick, string utf)
        {
            return ParseNickUTF(flDataPath, file, section, nick, utf, true);
        }

        public static NickUtfTuple ParseNickUTF(string flDataPath, string file, FLDataFile.Section section, string nick, string utf, bool errorUtf)
        {
            var retn = new NickUtfTuple();

            retn.Nick = TryGetStrSetting(section, nick);
            if (retn.Nick == null)
            {
                // Callers skip sections without a nickname.
                Logger.LogSettingNotFound(file, GetSectionIdentifier(section), section.sectionName, nick);
                return retn;
            }

            string relUtfFile = TryGetStrSetting(section, utf);
            if (relUtfFile != null)
            {
                if (Checker.FileExistsOnDisk(relUtfFile))
                {
                    if (!Checker.DisableUTF)
                    {
                        string utfFile = Path.Combine(flDataPath, relUtfFile);
                        try
                        {
                            retn.UtfFile = new UtfFile(utfFile);
                        }
                        catch (Exception ex)
                        {
                            Logger.ELog("Could not parse utf file! " + ex.Message + ", referenced at: " + section.GetSetting(utf) + ", utf-file: " + utfFile);
                        }
                    }
                }
                else if (Checker.HasVanillaFile(relUtfFile))
                {
                    // A vanilla model that isn't present: use its known hardpoints, if any
                    string[] hardpoints = Checker.GetVanillaHardpoints(relUtfFile);
                    if (!Checker.DisableUTF && hardpoints != null)
                        retn.UtfFile = UtfFile.FromHardpoints(relUtfFile, hardpoints);
                }
                else
                    Logger.LogFileNotFound(file, section.GetSetting(utf));
            }
            else if(errorUtf)
                Logger.LogSettingNotFound(file, retn.Nick, section.sectionName, utf);

            return retn;
        }

        public static FLDataFile.Setting TryGetSetting(FLDataFile.Section section, string name)
        {
            if (section.SettingExists(name))
                return section.GetSetting(name);
            return null;
        }

        /// <summary>
        /// Returns the first value of the setting, or null if the setting doesn't exist or has no value.
        /// </summary>
        public static string TryGetStrSetting(FLDataFile.Section section, string name)
        {
            var setting = TryGetSetting(section, name);
            return setting == null || setting.NumValues() == 0 ? null : setting.Str(0);
        }

        /// <summary>
        /// Returns something that helps the user find a section in log messages: the line number of its first setting.
        /// </summary>
        public static string GetSectionIdentifier(FLDataFile.Section section)
        {
            return section.settings.Count == 0 ? "0" : section.settings[0].LineNumber.ToString();
        }

        /// <summary>
        /// Adds a value to a dictionary. If the key already exists, a duplicate error is logged instead of throwing.
        /// </summary>
        public static void AddUnique<T>(Dictionary<string, T> dict, string key, T value, string file, FLDataFile.Section section, string what)
        {
            if (dict.ContainsKey(key))
            {
                var setting = TryGetSetting(section, "nickname");
                if (setting != null)
                    Logger.LogDublicateValue(file, setting, "Duplicate " + what + " nickname!");
                else
                    Logger.ELog(string.Format("{0}: Duplicate {1} nickname {2}", file, what, key));
                return;
            }
            dict.Add(key, value);
        }

        public delegate void CheckDelegate();
        public static void RunCheck(CheckDelegate check, string name)
        {
#if DEBUG
            check.Invoke();
#else
            try { check.Invoke(); }
            catch (Exception ex) { Logger.FLog("An error happend while checking " + name + " '" + ex.Message + "'\n" + ex.StackTrace); }
#endif
        }

        public delegate void CheckFileDelegate(string file);
        public static void RunCheck(CheckFileDelegate check, string file)
        {
#if DEBUG
            check.Invoke(file);
#else
            try { check.Invoke(file); }
            catch (Exception ex) { Logger.FLog("An error happend while checking file: " + file + " '" + ex.Message + "'\n" + ex.StackTrace); }
#endif
        }

        /// <summary>
        /// Runs a check on each file (relative to DATA). Files that aren't present are skipped: vanilla files can't
        /// be checked, and missing files are reported where they are referenced.
        /// </summary>
        /// <summary>
        /// Checks inherit in the archetype sections (lowercase names) of these files, read in this order the way
        /// common.dll reads them: the donor is looked up among the archetypes read so far (the game warns that
        /// inherit failed otherwise), and its values are copied over everything read before inherit, except the
        /// nickname.
        /// </summary>
        public static void CheckArchetypeInherit(IEnumerable<string> files, string[] sectionNames, string what, Func<string, bool> exists)
        {
            var defined = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in files)
            {
                FLDataFile ini = Checker.LoadIni(file);
                if (ini == null)
                    continue; // Reported by Checker.Parse
                bool onDisk = Checker.FileExistsOnDisk(file);
                foreach (var section in ini.sections.Where(sec => sectionNames.Contains(sec.sectionName.ToLower())))
                {
                    bool gotOtherKeys = false;
                    foreach (var setting in section.settings.Where(st => st.NumValues() > 0))
                    {
                        string key = setting.settingName.ToLower();
                        if (key != "inherit")
                        {
                            if (key != "nickname")
                                gotOtherKeys = true;
                            continue;
                        }
                        if (!onDisk)
                            continue;
                        string donor = setting.Str(0).Trim();
                        if (!defined.Contains(donor))
                            Logger.LogInvalidValue(file, setting, exists(donor)
                                ? "Has to be defined before this " + what + "! The game warns that inherit failed otherwise."
                                : what.Substring(0, 1).ToUpper() + what.Substring(1) + " doesn't exist! The game warns that inherit failed.");
                        else if (gotOtherKeys)
                            Logger.LogInvalidValue(file, setting, "Has to come first! The game copies the " + what + " over the values before it.", LogLevel.warning);
                    }
                    string nick = TryGetStrSetting(section, "nickname");
                    if (nick != null)
                        defined.Add(nick.Trim());
                }
            }
        }

        public static void RunChecks(CheckFileDelegate check, IEnumerable<string> files)
        {
            foreach (var file in files)
            {
                if (Checker.FileExistsOnDisk(file))
                    RunCheck(check, file);
            }
        }

        public static string[] LoadConfig(string section)
        {
            var sec = Checker.confIni.GetSettings(section);
            var entries = new List<string>(sec.Count * 8);

            foreach (var set in sec)
            {
                string s = set.settingName.ToLower();
                if (!s.Contains("$"))
                {
                    entries.Add(s);
                }
                else
                {
                    try
                    {
                        int pStart, pEnd, pSep;
                        pStart = s.IndexOf('$');
                        pEnd = s.LastIndexOf('$');
                        pSep = s.IndexOf('-', pStart);

                        int start = int.Parse(s.Substring(pStart + 1, pSep - pStart - 1));
                        int end = int.Parse(s.Substring(pSep + 1, pEnd - pSep - 1));

                        string first = s.Substring(0, pStart);
                        string second = s.Substring(pEnd + 1);

                        for (int i = start; i <= end; i++)
                        {
                            entries.Add(string.Format("{0}{1}{2}", first, i, second));
                        }
                    }
                    catch
                    {
                        Logger.ELog("Ignored invalid config-line: " + set.LineNumber);
                    }
                }
            }
            return entries.ToArray();
        }

        /// <summary>
        /// Returns the lowercase shape names defined in a shape file, or null if they can't be determined
        /// (the file is missing, or it's a vanilla file that isn't present).
        /// </summary>
        public static string[] ParseShapeFile(string file, FLDataFile.Setting refSet, string refFile)
        {
            // A vanilla shape file that isn't present is read from the vanilla definitions
            FLDataFile ini = Checker.LoadIni(file, false);
            if (ini == null)
            {
                if (!Checker.FileExists(file))
                    Logger.LogFileNotFound(refFile, refSet);
                return null;
            }
            List<string> values = new List<string>();

            foreach (var set in ini.sections.SelectMany(s => s.settings))
            {
                string name = set.settingName.ToLower();

                if (name == "tex_shape" || name == "shape_name" || name == "name")
                {
                    string value = set.Str(0).ToLower();
                    values.Add(value);
                }
            }

            return values.ToArray();
        }
    }
}
