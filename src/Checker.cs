using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FLScanIE.Logging;
using FLScanIE.Util_Functions;

namespace FLScanIE
{
    [Flags]
    public enum Checks
    {
        None = 0,
        Audio = 1,
        Character = 2,
        Equipment = 4,
        Fonts = 8,
        Fx = 16,
        Loadouts = 32,
        Missions = 64,
        PetalDb = 128,
        Ship = 256,
        Solar = 512,
        Universe = 1024
    }

    public static class Checker
    {
        public static Checks Checks;
        public static FLDataFile flIni;
        public static string flDataPath;
        private static bool disableUTF;
        private static bool assumeVanillaFilesExist;
        private static HashSet<string> vanillaFiles;

        public static FLDataFile confIni;

        public static bool DisableUTF
        {
            get { return disableUTF; }
            set { disableUTF = Properties.Settings.Default.setDisableUTF = value; }
        }

        public static bool AssumeVanillaFilesExist
        {
            get { return assumeVanillaFilesExist; }
            set { assumeVanillaFilesExist = Properties.Settings.Default.setAssumeVanillaFilesExist = value; }
        }

        static Checker()
        {
            confIni = new FLDataFile(GetAppFilePath("conf.ini"), false);
        }

        /// <summary>
        /// Returns the path of a file that is shipped next to the executable, independent of the working directory.
        /// </summary>
        private static string GetAppFilePath(string fileName)
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
        }

        public static bool Parse(string flPath)
        {
            if (assumeVanillaFilesExist && !ParseVanillaFiles())
                return false;

            string flExePath = Path.Combine(flPath, "EXE");
            try
            {
                flIni = new FLDataFile(flExePath + Path.DirectorySeparatorChar + "Freelancer.ini", true);
            }
            catch (Exception ex)
            {
                Logger.ELog("Error '" + ex.Message + "' when parsing '" + flExePath);
                return false;
            }
            flDataPath = Path.GetFullPath(Path.Combine(flExePath, flIni.GetSetting("Freelancer", "data path").Str(0)));

            try
            {
                CharacterChecks.ParseCharacterFolder();
                PetalDbChecks.ParsePetalDb();
                AudioChecks.ParseAudioFolder();
                FXChecks.ParseFXFolder();
                ShipChecks.ParseShipFolder();
                LoadoutChecks.ParseLoadouts();
                SolarChecks.ParseSolarFolder();
                Infocards.Load();
                EquipmentChecks.ParseEquipmentFolder();
                UniverseChecks.ParseUniverseFolder();
                FontChecks.ParseFontFoler();
            }
            catch (Exception ex)
            {
                Logger.ELog("An error happend while parsing. The scan can't continue. '" + ex.Message + "'" + ex.StackTrace);
                return false;
            }
            return true;
        }

        public static void Check()
        {
            if (ShouldCheck(Checks.Character))
                Util.RunCheck(CharacterChecks.CheckCharacterFolder, "characters");
            if (ShouldCheck(Checks.Universe))
                Util.RunCheck(UniverseChecks.CheckUniverseFolder, "universe");
            if (ShouldCheck(Checks.Audio))
                Util.RunCheck(AudioChecks.CheckAudioFolder, "audio");
            if (ShouldCheck(Checks.Fx))
                Util.RunCheck(FXChecks.CheckFX, "fx");
            if (ShouldCheck(Checks.Loadouts))
                Util.RunCheck(LoadoutChecks.CheckLoadouts, "loadouts");
            if (ShouldCheck(Checks.Solar))
                Util.RunCheck(SolarChecks.CheckSolarFolder, "solar");
            if (ShouldCheck(Checks.Ship))
                Util.RunCheck(ShipChecks.CheckShipFolder, "ships");
            if (ShouldCheck(Checks.Equipment))
                Util.RunCheck(EquipmentChecks.CheckEquipmentFolder, "equip");
            if (ShouldCheck(Checks.Missions))
                Util.RunCheck(MissionChecks.CheckForMissingFormations, "missions");
            if (ShouldCheck(Checks.Fonts))
                Util.RunCheck(FontChecks.CheckFontFolder, "fonts");
        }

        private static bool ShouldCheck(Checks check)
        {
            return (Checks & check) != Checks.None;
        }

        private static bool ParseVanillaFiles()
        {
            string vanillaFilesPath = GetAppFilePath("vanilla_files.txt");
            if (!File.Exists(vanillaFilesPath))
            {
                Logger.ELog("Vanilla file list not found: " + vanillaFilesPath);
                return false;
            }

            vanillaFiles = new HashSet<string>();

            string[] files = File.ReadAllLines(vanillaFilesPath);
            foreach (string file in files)
            {
                vanillaFiles.Add(file.Trim().ToLowerInvariant());
            }
            return true;
        }

        /// <summary>
        /// True if the file (relative to the DATA folder) is physically present. Unlike <see cref="FileExists"/>,
        /// this doesn't count vanilla files that are only assumed to exist, so use it before opening a file.
        /// </summary>
        public static bool FileExistsOnDisk(string relFilePath)
        {
            try
            {
                return File.Exists(Path.Combine(flDataPath, relFilePath));
            }
            catch (ArgumentException)
            {
                // The path contains invalid characters, so it can't exist
                return false;
            }
        }

        public static bool FileExists(string relFilePath)
        {
            return FileExistsOnDisk(relFilePath) || HasVanillaFile(relFilePath);
        }

        internal static bool HasVanillaFile(string relFilePath)
        {
            if (!assumeVanillaFilesExist)
                return false;
            if (vanillaFiles.Contains(relFilePath.ToLowerInvariant()))
                return true;

            string absPath = relFilePath.ToLowerInvariant().Replace(flDataPath.ToLowerInvariant(), string.Empty).TrimStart('\\');
            return vanillaFiles.Contains(absPath);
        }
    }
}
