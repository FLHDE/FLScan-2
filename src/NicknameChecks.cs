using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

using FLScanIE.Util_Functions;
using FLScanIE.Logging;

namespace FLScanIE
{
    /// <summary>
    /// Checks nicknames across files: nicknames that are defined twice, and different nicknames with the same hash.
    /// </summary>
    public static class NicknameChecks
    {
        private static readonly uint[] hashTable = CreateHashTable();

        private static uint[] CreateHashTable()
        {
            const uint polynomial = 0xA001u << 14;
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint x = i;
                for (int bit = 0; bit < 8; bit++)
                    x = (x & 1) != 0 ? (x >> 1) ^ polynomial : x >> 1;
                table[i] = x;
            }
            return table;
        }

        /// <summary>
        /// The hash the game uses for nicknames (CreateID in common.dll). It ignores the case of ASCII letters.
        /// </summary>
        public static uint CreateID(string nickname)
        {
            uint h = 0;
            foreach (char ch in nickname)
            {
                uint c = ch >= 'A' && ch <= 'Z' ? (uint)(ch + 32) : (uint)(ch & 0xFF);
                h = (h >> 8) ^ hashTable[(h ^ c) & 0xFF];
            }
            h = (h >> 24) | ((h >> 8) & 0x0000FF00) | ((h << 8) & 0x00FF0000) | (h << 24);
            return (h >> 2) | 0x80000000;
        }

        public static void CheckNicknames()
        {
            Logger.ILog("Checking nicknames");
            Util.RunCheck(CheckHashCollisions, "nickname hashes");
            Util.RunCheck(CheckDuplicates, "duplicate nicknames");
            Logger.ILog("Finished nicknames");
        }

        /// <summary>
        /// An INI file relative to DATA, and whether it's present (otherwise it's the copy of a vanilla file).
        /// </summary>
        private class IniFile
        {
            public string Path;
            public FLDataFile Ini;
            public bool OnDisk;
        }

        /// <summary>
        /// Loads a file relative to DATA. A vanilla file that isn't present is read from the vanilla definitions.
        /// Returns null if it doesn't exist, or can't be read (that's reported by the checks of the file).
        /// </summary>
        private static IniFile Load(string relPath)
        {
            try
            {
                FLDataFile ini = Checker.LoadIni(relPath);
                return ini == null ? null : new IniFile { Path = relPath, Ini = ini, OnDisk = !ini.IsVanillaCopy };
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static IEnumerable<IniFile> Load(IEnumerable<string> relPaths)
        {
            return relPaths.Select(p => Load(p)).Where(f => f != null);
        }

        private static string DescribeLocation(IniFile file, FLDataFile.Setting setting)
        {
            return file.OnDisk ? file.Path + ":" + setting.LineNumber : file.Path + " (vanilla)";
        }

        /// <summary>
        /// The game refers to everything by the hash of its nickname, so two different nicknames with the same hash
        /// are the same thing to it. Mission scripts are left out: their nicknames are only used within the mission.
        /// </summary>
        private static void CheckHashCollisions()
        {
            string dataPath = Checker.flDataPath.TrimEnd('\\', '/') + "\\";
            var files = new List<string>();
            var present = new HashSet<string>();
            foreach (string path in Directory.GetFiles(Checker.flDataPath, "*.ini", SearchOption.AllDirectories))
            {
                string rel = path.Substring(dataPath.Length);
                files.Add(rel);
                present.Add(rel.ToLowerInvariant());
            }
            files.AddRange(Checker.VanillaIniFiles.Where(f => !present.Contains(f)));

            var missionScripts = new HashSet<string>(MissionChecks.Missions.Select(m => ("missions\\" + m + "\\" + m + ".ini").ToLowerInvariant()));

            // The first nickname with each hash, where it's defined, and the collisions that have been reported
            var nicknames = new Dictionary<uint, KeyValuePair<IniFile, FLDataFile.Setting>>();
            var reported = new HashSet<string>();
            foreach (string rel in files)
            {
                if (missionScripts.Contains(rel.ToLowerInvariant()))
                    continue;
                IniFile file = Load(rel);
                if (file == null)
                    continue;

                foreach (var setting in file.Ini.sections.SelectMany(s => s.settings))
                {
                    if (setting.NumValues() == 0 || setting.settingName.ToLowerInvariant() != "nickname")
                        continue;
                    string nick = setting.Str(0).Trim();
                    if (nick.Length == 0)
                        continue;

                    uint hash = CreateID(nick);
                    KeyValuePair<IniFile, FLDataFile.Setting> first;
                    if (!nicknames.TryGetValue(hash, out first))
                    {
                        nicknames[hash] = new KeyValuePair<IniFile, FLDataFile.Setting>(file, setting);
                        continue;
                    }

                    string firstNick = first.Value.Str(0).Trim();
                    // The same nickname (e.g. a good and its equipment) is fine; duplicates are checked separately.
                    // A collision between two vanilla files that aren't present can't be fixed by the mod.
                    if (firstNick.ToLowerInvariant() == nick.ToLowerInvariant() || !file.OnDisk && !first.Key.OnDisk)
                        continue;
                    string pair = string.Join("|", new[] { firstNick.ToLowerInvariant(), nick.ToLowerInvariant() }.OrderBy(n => n).ToArray());
                    if (!reported.Add(pair))
                        continue;

                    if (file.OnDisk)
                        Logger.LogInvalidValue(file.Path, setting, string.Format("Has the same hash ({0}) as the nickname {1} at {2}!",
                            hash, firstNick, DescribeLocation(first.Key, first.Value)));
                    else
                        Logger.LogInvalidValue(first.Key.Path, first.Value, string.Format("Has the same hash ({0}) as the nickname {1} at {2}!",
                            hash, nick, DescribeLocation(file, setting)));
                }
            }
        }

        private static string[] DataFiles(string key)
        {
            return Checker.flIni.GetSettings("Data", key).Where(s => s.NumValues() > 0).Select(s => s.Str(0)).ToArray();
        }

        private const string UsesFirst = "The game uses the first definition, at {1}";

        private static void CheckDuplicates()
        {
            // common.dll keeps the first equipment and good, and a later loadout replaces the items of the earlier one.
            // The client redefines a sound with a later one (and warns).
            CheckDuplicates("equipment", Load(DataFiles("equipment")), null, false, UsesFirst);
            CheckDuplicates("good", Load(DataFiles("goods")), new[] { "good" }, false, UsesFirst);
            CheckDuplicates("loadout", Load(DataFiles("loadouts")), new[] { "loadout" }, false, "The game replaces the loadout at {1} with this one");
            CheckDuplicates("sound", Load(DataFiles("sounds")), new[] { "sound" }, false, "The game warns and redefines the sound at {1} with this one");
            CheckDuplicates("faction", Load(new[] { "initialworld.ini" }), new[] { "group" }, false, null);
            // Each kind of pilot block has its own nicknames. The game keeps the first block or pilot with a
            // nickname (std::map::insert in content.dll), and population pilots are loaded before story pilots.
            CheckDuplicates("pilot", Load(new[] { @"MISSIONS\pilots_population.ini", @"MISSIONS\pilots_story.ini" }), null, true, UsesFirst);

            foreach (string universeFile in DataFiles("universe"))
            {
                IniFile universe = Load(universeFile);
                if (universe == null)
                    continue;
                CheckDuplicates("system", new[] { universe }, new[] { "system" }, false, null);

                // Objects and zones of a system are referenced the same way, so they share their nicknames
                foreach (var systemFile in universe.Ini.GetSettings("System", "file").Where(s => s.NumValues() > 0))
                {
                    IniFile system = Load(Path.Combine("universe", systemFile.Str(0)));
                    if (system != null)
                        CheckDuplicates("object or zone", new[] { system }, new[] { "object", "zone" }, false, null);
                }
            }

            foreach (string mission in MissionChecks.Missions)
            {
                string file = @"MISSIONS\" + mission + @"\" + mission + ".ini";
                if (Checker.FileExistsOnDisk(file))
                    // content.dll keeps the first [ObjList] (std::map::insert) and finds MsnShips and MsnFormations
                    // by searching its list of them from the start, so the first definition is used
                    CheckDuplicates("mission", Load(new[] { file }), null, true, UsesFirst);
            }
        }

        /// <summary>
        /// Reports nicknames that are defined more than once in the sections (lowercase names, or all sections if
        /// null) of these files. If perSectionType is true, each kind of section has its own nicknames. A duplicate
        /// that is an identical copy is a warning, otherwise it's an error: the game only uses one of them. usage says
        /// what the game does with the duplicate ({1} is where the first one is), or is null if that isn't known.
        /// </summary>
        private static void CheckDuplicates(string what, IEnumerable<IniFile> files, string[] sectionNames, bool perSectionType, string usage)
        {
            var first = new Dictionary<string, KeyValuePair<IniFile, FLDataFile.Section>>();
            foreach (IniFile file in files)
            {
                foreach (var section in file.Ini.sections)
                {
                    string type = section.sectionName.ToLowerInvariant();
                    if (sectionNames != null && !sectionNames.Contains(type))
                        continue;
                    var nickSetting = Util.TryGetSetting(section, "nickname");
                    if (nickSetting == null || nickSetting.NumValues() == 0)
                        continue;
                    string key = nickSetting.Str(0).Trim().ToLowerInvariant();
                    if (perSectionType)
                        key = type + ":" + key;

                    KeyValuePair<IniFile, FLDataFile.Section> original;
                    if (!first.TryGetValue(key, out original))
                    {
                        first[key] = new KeyValuePair<IniFile, FLDataFile.Section>(file, section);
                        continue;
                    }
                    if (!file.OnDisk)
                        continue; // Duplicates in vanilla files that aren't present can't be fixed by the mod

                    string message = string.Format("Duplicate {0} nickname! " + (usage ?? "Also defined at {1}"), what, DescribeLocation(original.Key, Util.TryGetSetting(original.Value, "nickname")));
                    if (original.Key.OnDisk && SameSettings(original.Value, section))
                        Logger.LogDublicateValue(file.Path, nickSetting, message + " (an identical copy)", LogLevel.warning);
                    else
                        Logger.LogDublicateValue(file.Path, nickSetting, message);
                }
            }
        }

        private static bool SameSettings(FLDataFile.Section a, FLDataFile.Section b)
        {
            if (a.settings.Count != b.settings.Count)
                return false;
            for (int i = 0; i < a.settings.Count; i++)
            {
                FLDataFile.Setting x = a.settings[i], y = b.settings[i];
                if (x.settingName.ToLowerInvariant() != y.settingName.ToLowerInvariant() || x.NumValues() != y.NumValues())
                    return false;
                for (int v = 0; v < x.NumValues(); v++)
                {
                    if (x.Str(v).Trim().ToLowerInvariant() != y.Str(v).Trim().ToLowerInvariant())
                        return false;
                }
            }
            return true;
        }
    }
}
