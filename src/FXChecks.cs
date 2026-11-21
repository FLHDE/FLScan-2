using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

using FLScanIE.Util_Functions;
using FLScanIE.Logging;

namespace FLScanIE
{
    // TODO: effect_shapes [BeamBolt] [BeamSpear]
    public static class FXChecks
    {
        private static string[] effectFiles;
        private static string[] effectNicks;

        private static string[] explosionFiles;
        private static string[] explosionNicks;

        private static string[] fuseFiles;
        private static string[] fuseNicks;

        private static string[] effectTypes;
        private static string[] debrisNicknames;
        // The [IGraph] nicknames of the igraph files (lowercase), or null if none of them could be read
        private static HashSet<string> graphNicks;
        // The [LightAnim] names of fx\lightanim.ini (lowercase), or null if the file isn't on disk
        private static HashSet<string> lightAnimNames;

        public static void CheckFX()
        {
            Logger.ILog("Checking FX-Folder");
            Util.RunChecks(CheckEffectFile, effectFiles);
            Util.RunChecks(CheckExplosionFile, explosionFiles);
            Util.RunChecks(CheckFuseFile, fuseFiles);
            Logger.ILog("Finished FX-Folder");
        }

        private static void CheckEffectFile(string file)
        {
            FLDataFile ini = new FLDataFile(Path.Combine(Checker.flDataPath, file), true);
            var effects = ini.GetSettings("Effect", null).Union(ini.GetSettings("JumpGateEffect")).Union(ini.GetSettings("JumpShipEffect"));
            var visEffects = ini.sections.Where(s => s.sectionName.ToLower() == "viseffect").ToArray();

            foreach (var effect in effects)
            {
                switch (effect.settingName)
                {
                    case "vis_generic":
                    case "vis_effect":
                    case "glow_ring_effect":
                        foreach (var value in effect.values.Select(v => v.ToString()))
                        {
                            if (!EffectExists(value))
                                Logger.LogInvalidValue(file, effect, "Effect doesn't exist!", value);
                        }
                        break;
                    case "effect_type":
                        if (!EffectTypeExists(effect.Str(0)))
                            Logger.LogInvalidValue(file, effect, "Effect-type doesn't exist!");
                        break;
                    case "snd_effect":
                        if (!AudioChecks.SoundExists(effect.Str(0)))
                            Logger.LogInvalidValue(file, effect, "Sound doesn't exist!");
                        break;
                    case "jump_tunnel_effect":
                    case "jump_tunnel":
                    case "jump_out_effect":
                    case "jump_in_effect":
                        if (!EffectExists(effect.Str(0)))
                            Logger.LogInvalidValue(file, effect, "Effect doesn't exist!");
                        break;
                }
            }

            foreach (var visEffect in visEffects)
            {
                CheckVisEffect(visEffect, file);
            }
        }

        private static void CheckVisEffect(FLDataFile.Section visEffect, string file)
        {
            string alchemy = Util.TryGetStrSetting(visEffect, "alchemy");
            if (alchemy != null && !Checker.FileExists(alchemy))
                Logger.LogFileNotFound(file, visEffect.GetSetting("alchemy"));

            // A VisEffect usually has several textures entries
            foreach (var textures in visEffect.GetSettings("textures"))
            {
                if (textures.NumValues() > 0 && !Checker.FileExists(textures.Str(0)))
                    Logger.LogFileNotFound(file, textures);
            }

            if (visEffect.SettingExists("effect_crc"))
            {
                Int64 effectCrc;
                if (!Int64.TryParse(visEffect.GetSetting("effect_crc").Str(0), out effectCrc))
                    Logger.LogInvalidValue(file, visEffect.GetSetting("effect_crc"), "Has to be a number!");

                if(alchemy == null)
                    Logger.LogInvalidValue(file, visEffect.GetSetting("effect_crc"), "effect_crc exists but alchemy doesn't!");
                else
                {
                    // TODO: check hash
                }
            }
        }

        private static void CheckExplosionFile(string file)
        {
            FLDataFile ini = new FLDataFile(Path.Combine(Checker.flDataPath, file), true);
            var effects = ini.GetSettings("explosion", null);
            var debris = ini.GetSettings("Debris", null);

            foreach (var effect in effects)
            {
                switch (effect.settingName)
                {
                    case "effect":
                        if (!EffectExists(effect.Str(0)))
                            Logger.LogInvalidValue(file, effect, "Effect doesn't exist!");
                        break;
                    case "debris_type":
                        if (!DebrisExists(effect.Str(0)))
                            Logger.LogInvalidValue(file, effect, "Debris doesn't exist!");
                        break;
                    case "process":
                        string str = effect.Str(0).ToLower();
                        if (str != "shatter" && str != "disappear" && str != "none")
                            Logger.LogInvalidValue(file, effect, "Has to be shatter, disappear or none.");
                        break;
                    case "innards_debris_object": // TODO: is any object useable here? or just [Simple] ones? There are simples in explosion.ini and in shiparch.ini, are both useable?
                        break;
                }
            }

            foreach (var debr in debris)
            {
                switch (debr.settingName)
                {
                    case "explosion":
                        if (!ExplosionExists(debr.Str(0)))
                            Logger.LogInvalidValue(file, debr, "Explosion doesn't exist!");
                        break;
                    case "trail":
                        if (!EffectExists(debr.Str(0)))
                            Logger.LogInvalidValue(file, debr, "Effect doesn't exist!");
                        break;
                    case "death_method": // The client keeps exploding for other values
                        if (debr.Str(0).ToLower() != "exploding" && debr.Str(0).ToLower() != "vanishing")
                            Logger.LogInvalidValue(file, debr, "Has to be exploding or vanishing!");
                        break;
                }
            }
        }

        private static void CheckFuseFile(string file)
        {
            FLDataFile ini = new FLDataFile(Path.Combine(Checker.flDataPath, file), true);
            var fuseEffects = ini.GetSettings("start_effect", "effect").Union(ini.GetSettings("start_cam_particles", "effect"));

            foreach (var fuseEffect in fuseEffects)
            {
                if (!EffectExists(fuseEffect.Str(0)))
                    Logger.LogInvalidValue(file, fuseEffect, "Effect doesn't exist!");
            }

            foreach (var setting in ini.GetSettings("start_explosion", "explosion").Where(s => s.NumValues() > 0))
            {
                if (!ExplosionExists(setting.Str(0)))
                    Logger.LogInvalidValue(file, setting, "Explosion doesn't exist!");
            }
            foreach (var setting in ini.GetSettings("ignite_fuse", "fuse").Where(s => s.NumValues() > 0))
            {
                if (!FuseExists(setting.Str(0)))
                    Logger.LogInvalidValue(file, setting, "Fuse doesn't exist!");
            }
            // The server compares fate case-sensitively and keeps the default for other values
            foreach (var setting in ini.GetSettings("destroy_group", "fate").Where(s => s.NumValues() > 0))
            {
                if (setting.Str(0) != "debris" && setting.Str(0) != "disappear")
                    Logger.LogInvalidValue(file, setting, "Has to be debris or disappear (lowercase)!");
            }
            foreach (var setting in ini.GetSettings("destroy_hp_attachment", "fate").Where(s => s.NumValues() > 0))
            {
                if (setting.Str(0) != "debris" && setting.Str(0) != "disappear" && setting.Str(0) != "loot")
                    Logger.LogInvalidValue(file, setting, "Has to be debris, disappear or loot (lowercase)!");
            }
            // Anything but absolute is relative
            foreach (var setting in ini.sections.SelectMany(sec => sec.settings).Where(s => s.settingName.ToLower() == "damage_type" && s.NumValues() > 0))
            {
                if (setting.Str(0).ToLower() != "absolute" && setting.Str(0).ToLower() != "relative")
                    Logger.LogInvalidValue(file, setting, "Has to be absolute or relative! The game uses relative otherwise.", LogLevel.warning);
            }
        }
        
        public static void ParseFXFolder()
        {
            Logger.ILog("Parsing FX-Folder");

            string[][] effects = Util.GetNicks(new[] { "Data" }, new[] { "effects", "jump_effect", "gate_tunnels" }, new[] { "effect", "VisEffect", "JumpGateEffect", "gate_tunnel" }, new[] { "nickname" });
            FXChecks.effectFiles = effects[0];
            FXChecks.effectNicks = effects[1].Select(n => n.ToLower()).ToArray();
            FXChecks.effectTypes = Util.GetNicks("Data", "effects", "EffectType", "nickname")[1].Select(n => n.ToLower()).ToArray();

            string[][] explosions = Util.GetNicks("Data", "explosions", "explosion", "nickname");
            FXChecks.explosionFiles = explosions[0];
            FXChecks.explosionNicks = explosions[1].Select(n => n.ToLower()).ToArray();
            FXChecks.debrisNicknames = Util.GetNicks("Data", new[] { "explosions", "debris" }, "Debris", "nickname")[1].Select(n => n.ToLower()).ToArray();

            string[][] fuses = Util.GetNicks("Data", "fuses", "fuse", "name");
            FXChecks.fuseFiles = fuses[0];
            FXChecks.fuseNicks = fuses[1].Select(n => n.ToLower()).ToArray();

            // The graphs (e.g. of light curves). Missing files are reported by Checker.Parse.
            graphNicks = null;
            foreach (var igraphFile in Checker.flIni.GetSettings("Data", "igraph").Where(s => s.NumValues() > 0))
            {
                FLDataFile ini = Checker.LoadIni(igraphFile.Str(0));
                if (ini == null)
                    continue;
                if (graphNicks == null)
                    graphNicks = new HashSet<string>();
                foreach (var nick in ini.GetSettings("IGraph", "nickname").Where(s => s.NumValues() > 0))
                    graphNicks.Add(nick.Str(0).Trim().ToLower());
            }

            // The client always loads fx\lightanim.ini; it isn't in freelancer.ini. Names are cut to 31 characters.
            lightAnimNames = null;
            if (Checker.FileExistsOnDisk(@"FX\lightanim.ini"))
            {
                FLDataFile ini = Checker.LoadIni(@"FX\lightanim.ini");
                if (ini != null)
                    lightAnimNames = new HashSet<string>(ini.GetSettings("LightAnim", "name").Where(s => s.NumValues() > 0)
                        .Select(s => Truncate(s.Str(0).Trim().ToLower(), 31)));
            }

            Logger.ILog(string.Format("Finished FX-Folder, found {0} effects, {1} explosions and {2} fuses", effectNicks.Length, explosionNicks.Length, fuseNicks.Length));
        }

        public static void AddExplosions(IEnumerable<string> explosions)
        {
            FXChecks.explosionNicks = FXChecks.explosionNicks.Union(explosions).ToArray();
        }

        /// <summary>
        /// True if an [IGraph] of the igraph files has this nickname (the game ignores the case), or if they
        /// couldn't be read.
        /// </summary>
        public static bool GraphExists(string nick)
        {
            return graphNicks == null || graphNicks.Contains(nick.Trim().ToLower());
        }

        private static string Truncate(string s, int length)
        {
            return s.Length > length ? s.Substring(0, length) : s;
        }

        /// <summary>
        /// True if fx\lightanim.ini has a [LightAnim] with this name (the client ignores the case and uses at most 31
        /// characters), or if the file isn't on disk.
        /// </summary>
        public static bool LightAnimExists(string name)
        {
            return lightAnimNames == null || lightAnimNames.Contains(Truncate(name.Trim().ToLower(), 31));
        }

        public static bool EffectExists(string nick)
        {
            nick = nick.ToLower();
            return effectNicks.Contains(nick);
        }

        public static bool ExplosionExists(string nick)
        {
            nick = nick.ToLower();
            return explosionNicks.Contains(nick);
        }

        public static bool FuseExists(string nick)
        {
            nick = nick.ToLower();
            return fuseNicks.Contains(nick);
        }

        public static bool EffectTypeExists(string nick)
        {
            nick = nick.ToLower();
            return effectTypes.Contains(nick);
        }

        public static bool DebrisExists(string nick)
        {
            nick = nick.ToLower();
            return debrisNicknames.Contains(nick);
        }
    }
}
