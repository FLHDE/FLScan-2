using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

using FLScanIE.Util_Functions;
using FLScanIE.Logging;

namespace FLScanIE
{
    // TODO: rings, nebula, blackhole, asteroids folders
    public static class SolarChecks
    {
        private static string[] solarFiles;
        private static string[] solarNicknames;
        public static Dictionary<string, UtfFile> solarUTF;

        private static string[] asteroidFiles;
        private static string[] asteroidNicknames;

        private static string[] starFiles;
        private static string[] starNicknames;
        private static string[] star_GlowNicknames;
        private static string[] star_lensFlareNicknames;
        private static string[] star_lensGlowNicknames;
        private static string[] star_spineNicknames;

        private static string[] allArchetypes;

        // The docking types of common.dll (compared case-insensitively)
        private static readonly HashSet<string> dockTypes = new HashSet<string>(new[] { "berth", "moor_small", "moor_medium",
            "moor_large", "ring", "pad", "jump", "airlock" }, StringComparer.OrdinalIgnoreCase);

        public static void CheckSolarFolder()
        {
            Logger.ILog("Checking Solar-Folder");
            Util.RunChecks(CheckSolarFile, solarFiles);
            Util.RunChecks(CheckAsteroidFile, asteroidFiles);
            Util.RunChecks(CheckStarFile, starFiles);
            Logger.ILog("Finished Solar-Folder");
        }

        private static void CheckSolarFile(string file)
        {
            FLDataFile ini = new FLDataFile(Path.Combine(Checker.flDataPath, file), true);

            var solars = ini.GetSettings("Solar");
            var collisionGroups = ini.GetSettings("CollisionGroup");

            foreach (var solar in solars)
            {
                if(solar.settingName == "ids_name")
                    Util.CheckIDName(solar, solar.LineNumber, file);
                if (solar.settingName == "ids_info")
                    Util.CheckIDInfo(solar, solar.LineNumber, file);
                if (solar.settingName.ToLower() == "type")
                    Util.CheckObjectType(file, solar);
                switch (solar.settingName)
                {
                    case "DA_archetype":
                    case "attachment_archetype":
                    case "material_library":
                        if (!Checker.FileExists(solar.Str(0)))
                            Logger.LogFileNotFound(file, solar);
                        break;
                    case "fuse":
                        if (!FXChecks.FuseExists(solar.Str(0)))
                            Logger.LogInvalidValue(file, solar, "Fuse doesn't exist!");
                        break;
                    case "explosion_arch":
                        if (!FXChecks.ExplosionExists(solar.Str(0)))
                            Logger.LogInvalidValue(file, solar, "Explosion doesn't exist!");
                        break;
                    case "surface_hit_effects":
                        if (!Util.CheckNumberOfArgs(solar, file, 2, 3, 4))
                            break;
                        for (int i = 1; i < solar.NumValues(); i++)
                            if (!FXChecks.EffectExists(solar.Str(i)))
                                Logger.LogInvalidValue(file, solar, "Effect doesn't exist!", solar.Str(i));
                        break;
                }
            }

            // docking_sphere = type, hardpoint, radius[, animation]. common.dll uses type 0 (no docking) for an unknown
            // type, and the dock needs the hardpoint on the model.
            foreach (var section in ini.sections.Where(sec => sec.sectionName.ToLower() == "solar"))
            {
                string nick = Util.TryGetStrSetting(section, "nickname");
                UtfFile utf;
                if (nick == null || !solarUTF.TryGetValue(nick, out utf))
                    utf = null;
                foreach (var sphere in section.GetSettings("docking_sphere"))
                {
                    if (!Util.CheckNumberOfArgs(sphere, file, 3, 4))
                        continue;
                    if (!dockTypes.Contains(sphere.Str(0).Trim()))
                        Logger.LogInvalidValue(file, sphere, "Unknown docking type! Has to be berth, moor_small, moor_medium, moor_large, ring, pad, jump or airlock.", sphere.Str(0));
                    if (utf != null && !utf.HardpointExists(sphere.Str(1).Trim()))
                        Logger.LogInvalidValue(file, sphere, "Hardpoint doesn't exist in the model!", sphere.Str(1));
                }
            }

            foreach (var collisionGroup in collisionGroups)
            {
                switch (collisionGroup.settingName)
                {
                    case "fuse":
                        if (!FXChecks.FuseExists(collisionGroup.Str(0)))
                            Logger.LogInvalidValue(file, collisionGroup, "Fuse doesn't exist!");
                        break;
                    case "debris_type":
                        if (!FXChecks.DebrisExists(collisionGroup.Str(0)))
                            Logger.LogInvalidValue(file, collisionGroup, "Debris type doesn't exist!");
                        break;
                }
            }
        }

        private static void CheckStarFile(string file)
        {
            FLDataFile ini = new FLDataFile(Path.Combine(Checker.flDataPath, file), true);

            foreach (var texFile in ini.GetSettings("Texture", "file"))
            {
                if (!Checker.FileExists(texFile.Str(0)))
                    Logger.LogFileNotFound(file, texFile);
            }

            string[] shapes = ini.GetSettings("Texture", "tex_shape").Select(s => s.Str(0)).ToArray();

            var stars = ini.GetSettings("Star");
            var other = ini.GetSettings("star_glow", "tex_shape").Union(ini.GetSettings("lens_flare", "tex_shape")).
                             Union(ini.GetSettings("lens_glow", "tex_shape")).Union(ini.GetSettings("spines", "tex_shape"));

            foreach (var star in stars)
            {
                switch (star.settingName)
                {
                    case "star_glow":
                    case "star_center":
                        if (!star_GlowNicknames.Contains(star.Str(0).ToLower()))
                            Logger.LogInvalidValue(file, star, "Starglow doesn't exist!");
                        break;
                    case "lens_glow":
                        if (!star_lensGlowNicknames.Contains(star.Str(0).ToLower()))
                            Logger.LogInvalidValue(file, star, "Lens glow doesn't exist!");
                        break;
                    case "lens_flare":
                        if (!star_lensFlareNicknames.Contains(star.Str(0).ToLower()))
                            Logger.LogInvalidValue(file, star, "Lens flare doesn't exist!");
                        break;
                    case "spines":
                        if (!star_spineNicknames.Contains(star.Str(0).ToLower()))
                            Logger.LogInvalidValue(file, star, "Spines doesn't exist!");
                        break;
                }
            }

            foreach (var setting in other)
            {
                if (!shapes.Contains(setting.Str(0)))
                    Logger.LogInvalidValue(file, setting, "Shape doesn't exist!");
            }
        }

        private static void CheckAsteroidFile(string file)
        {
            FLDataFile ini = new FLDataFile(Path.Combine(Checker.flDataPath, file), true);
            var asteroids = ini.GetSettings("DynamicAsteroid").Union(ini.GetSettings("Asteroid"));

            foreach (var asteroid in asteroids)
            {
                if (asteroid.settingName.ToLower() == "type")
                    Util.CheckObjectType(file, asteroid);
                switch (asteroid.settingName)
                {
                    case "DA_archetype":
                    case "attachment_archetype":
                    case "material_library":
                        if (!Checker.FileExists(asteroid.Str(0)))
                            Logger.LogFileNotFound(file, asteroid);
                        break;
                    case "explosion_arch":
                        if (!FXChecks.ExplosionExists(asteroid.Str(0)))
                            Logger.LogInvalidValue(file, asteroid, "Explosion doesn't exist!");
                        break;
                    case "particle_effect":
                        if (!FXChecks.EffectExists(asteroid.Str(0)))
                            Logger.LogInvalidValue(file, asteroid, "Effect doesn't exist!");
                        break;
                }
            }
        }

        public static void ParseSolarFolder()
        {
            Logger.ILog("Parsing Solar-Folder");
            SolarChecks.solarFiles = Checker.flIni.GetSettings("Data", "solar").Select(s => s.Str(0)).ToArray();
            List<string> solarNicknames = new List<string>();
            // Case-insensitive, because nicknames are referenced with any casing
            solarUTF = new Dictionary<string, UtfFile>(StringComparer.OrdinalIgnoreCase);

            foreach (var solarFile in SolarChecks.solarFiles)
            {
                // Missing files are reported by Checker.Parse
                FLDataFile flDataFile = Checker.LoadIni(solarFile);
                if (flDataFile == null)
                    continue;

                foreach (var section in flDataFile.sections)
                {
                    switch (section.sectionName)
                    {
                        case "Solar":
                            Util.NickUtfTuple retn = Util.ParseNickUTF(Checker.flDataPath, solarFile, section);
                            if (retn.Nick == null)
                                break;
                            if (solarUTF.ContainsKey(retn.Nick))
                            {
                                Logger.LogDublicateValue(solarFile, section.GetSetting("nickname"), "Duplicate solar nickname! The game redefines the first solar with this one, and reports an error for each [CollisionGroup] after it.");
                                break;
                            }
                            solarNicknames.Add(retn.Nick);
                            solarUTF.Add(retn.Nick, retn.UtfFile);
                            break;
                    }
                }
            }

            SolarChecks.solarNicknames = solarNicknames.Select(n => n.ToLower()).ToArray();

            string[][] asteroids = Util.GetNicks("Data", "asteroids", new[] { "DynamicAsteroid", "Asteroid", "AsteroidMine" }, "nickname");
            SolarChecks.asteroidFiles = asteroids[0];
            SolarChecks.asteroidNicknames = asteroids[1].Select(n => n.ToLower()).ToArray();

            List<string> starNicknames = new List<string>();
            List<string> star_GlowNicknames = new List<string>();
            List<string> star_lensFlareNicknames = new List<string>();
            List<string> star_lensGlowNicknames = new List<string>();
            List<string> star_spineNicknames = new List<string>();

            // stararch.ini isn't referenced by freelancer.ini, but it's always loaded by the game.
            SolarChecks.starFiles = Checker.flIni.GetSettings("Data", "stars").Select(s => s.Str(0))
                .Union(new[] { "solar\\stararch.ini" }, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (var starIni in SolarChecks.starFiles)
            {
                // Missing files from freelancer.ini are reported by Checker.Parse
                FLDataFile ini = Checker.LoadIni(starIni);
                if (ini == null)
                    continue;

                starNicknames.AddRange(ini.GetSettings("star", "nickname").Select(s => s.Str(0)));
                star_GlowNicknames.AddRange(ini.GetSettings("star_glow", "nickname").Select(s => s.Str(0)));
                star_lensFlareNicknames.AddRange(ini.GetSettings("lens_flare", "nickname").Select(s => s.Str(0)));
                star_lensGlowNicknames.AddRange(ini.GetSettings("lens_glow", "nickname").Select(s => s.Str(0)));
                star_spineNicknames.AddRange(ini.GetSettings("spines", "nickname").Select(s => s.Str(0)));
            }

            SolarChecks.starNicknames = starNicknames.Select(n => n.ToLower()).ToArray();
            SolarChecks.star_GlowNicknames = star_GlowNicknames.Select(n => n.ToLower()).ToArray();
            SolarChecks.star_lensFlareNicknames = star_lensFlareNicknames.Select(n => n.ToLower()).ToArray();
            SolarChecks.star_lensGlowNicknames = star_lensGlowNicknames.Select(n => n.ToLower()).ToArray();
            SolarChecks.star_spineNicknames = star_spineNicknames.Select(n => n.ToLower()).ToArray();

            SolarChecks.allArchetypes = SolarChecks.solarNicknames.Union(SolarChecks.starNicknames).Union(SolarChecks.asteroidNicknames).ToArray();

            Logger.ILog(string.Format("Finished Solar-Folder, found {0} solars ({1} models loaded), {2} asteroids and {3} stars",
                SolarChecks.solarNicknames.Length, solarUTF.Values.Count(u => u != null), asteroidNicknames.Length, SolarChecks.starNicknames.Length));
        }

        public static bool SolarExists(string nick)
        {
            nick = nick.ToLower();
            return solarNicknames.Contains(nick);
        }

        public static bool AsteroidExists(string nick)
        {
            nick = nick.ToLower();
            return asteroidNicknames.Contains(nick);
        }

        public static bool StarExists(string nick)
        {
            nick = nick.ToLower();
            return starNicknames.Contains(nick);
        }

        public static bool ArchetypeExists(string nick)
        {
            nick = nick.ToLower();
            return allArchetypes.Contains(nick);
        }
    }
}
