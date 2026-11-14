using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;

using FLScanIE.Util_Functions;
using FLScanIE.Logging;

namespace FLScanIE
{
    public static class EquipmentChecks
    {
        /*
TODO: there are references to [LightAnim]-nicknames, but lightanim.ini isn't referenced in freelancer.ini
         */

        private static string[] equipmentFiles;
        private static Dictionary<string, string[]> equipNicks;
        private static Dictionary<string, Dictionary<string, UtfFile>> equipUtf;
        private static Dictionary<string, string> shipHulls;
        private static string[] AllEquipNicks;

        private static string[] goodFiles;
        private static string[] goodNicknames;

        private static string[] marketFiles;
        public static Dictionary<string, string[]> marketEntries;

        private static string[] weaponModFiles;
        private static string[] weaponTypes;

        private static string[] hpTypes = Util.LoadConfig("HardpointTypes");
        // Known shield types (lowercase): the vanilla ones from conf.ini plus those used by [ShieldGenerator] sections
        private static string[] vanillaShieldTypes = Util.LoadConfig("ShieldTypes");
        private static HashSet<string> shieldTypes = new HashSet<string>();

        private static string[] GoodCategories = Util.LoadConfig("GoodCategories");

        public static void CheckEquipmentFolder()
        {
            Logger.ILog("Checking Equipment-Folder");
            var shipHulls = new Dictionary<string, string>();
            Util.RunChecks(CheckEquipmentFile, equipmentFiles);
            CheckEquipmentReadOrder();
            Util.RunChecks(CheckGoodFile, goodFiles);
            CheckGoodReadOrder();
            Util.RunChecks(CheckMarketFile, marketFiles);
            Util.RunChecks(CheckWeaponModFile, weaponModFiles);
            // commodities_per_faction.ini isn't referenced by freelancer.ini, but it's always loaded by the game
            Util.RunChecks(CheckFactionGoodFile, new[] { @"EQUIPMENT\commodities_per_faction.ini" });
            Logger.ILog("Finished Equipment-Folder");
        }

        /// <summary>
        /// Checks commodities_per_faction.ini: the goods each faction's ships drop as loot.
        /// </summary>
        private static void CheckFactionGoodFile(string file)
        {
            FLDataFile ini = new FLDataFile(Path.Combine(Checker.flDataPath, file), true);

            foreach (var setting in ini.sections.Where(s => s.sectionName.ToLower() == "factiongood").SelectMany(s => s.settings))
            {
                if (setting.NumValues() == 0)
                    continue;
                switch (setting.settingName.ToLower())
                {
                    case "faction":
                        if (!UniverseChecks.FactionExists(setting.Str(0)))
                            Logger.LogInvalidValue(file, setting, "Faction doesn't exist!");
                        break;
                    case "marketgood":
                        if (!GoodExists(setting.Str(0)))
                            Logger.LogInvalidValue(file, setting, "Good doesn't exist!", setting.Str(0));
                        break;
                }
            }
        }

        private static void CheckWeaponModFile(string file)
        {
            FLDataFile ini = new FLDataFile(Path.Combine(Checker.flDataPath, file), true);

            foreach (var setting in ini.sections.Where(s => s.sectionName.ToLower() == "weapontype").SelectMany(s => s.GetSettings("shield_mod")))
            {
                if (Util.CheckNumberOfArgs(setting, file, 2))
                {
                    string shieldType = setting.Str(0);
                    if (!ShieldTypeExists(shieldType))
                        Logger.LogInvalidValue(file, setting, "Shield-type not found!", shieldType);
                }
            }
        }

        /// <summary>
        /// The kinds of equipment that common.dll reads from the equipment files (lowercase). They all read the keys
        /// of an archetype (ids_name, ids_info, explosion_arch, DA_archetype) and the client reads material_library.
        /// </summary>
        public static readonly string[] EquipmentKinds = { "armor", "attachedfx", "internalfx", "power", "thruster", "shield",
            "shieldgenerator", "shieldbattery", "repairkit", "repairdroid", "countermeasure", "countermeasuredropper", "gun",
            "minedropper", "mine", "munition", "scanner", "tractor", "engine", "cloakingdevice", "tradelane", "cargopod",
            "lootcrate", "commodity", "light" };
        // The kinds that are attached equipment in common.dll, which also read separation_explosion, debris_type and HP_child
        private static readonly string[] attachedEquipmentKinds = { "shield", "shieldgenerator", "thruster", "gun", "minedropper",
            "countermeasuredropper", "cargopod", "cloakingdevice" };
        private static string[] hasLootAppearance = new string[] { "Commodity", "CounterMeasure", "RepairKit", "ShieldBattery", "Mine", "Munition" }.Select(s => s.ToLower()).ToArray();

        /// <summary>
        /// Checks a list of hardpoint types, like hp_gun_type. The game reads every value of every line, and skips the
        /// types it doesn't know.
        /// </summary>
        private static void CheckHpTypeList(string file, FLDataFile.Section section, string key)
        {
            foreach (var setting in section.GetSettings(key))
            {
                foreach (string hpType in setting.values.Select(v => v.ToString().Trim()).TakeWhile(v => v != ""))
                {
                    if (!HpTypeExists(hpType))
                        Logger.LogInvalidValue(file, setting, "Invalid hardpoint-type!", hpType);
                }
            }
        }

        /// <summary>
        /// Common.dll looks up inherit, loot_appearance and pod_appearance while it reads the equipment files, in
        /// the order of freelancer.ini, so they have to name equipment that is defined earlier. inherit copies
        /// nothing from equipment of another kind, and pod_appearance is ignored if it isn't a [CargoPod].
        /// </summary>
        private static void CheckEquipmentReadOrder()
        {
            // The kind of each equipment defined so far (the first definition is used)
            var defined = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in equipmentFiles)
            {
                FLDataFile ini = Checker.LoadIni(file);
                if (ini == null)
                    continue; // Reported by Checker.Parse
                foreach (var section in ini.sections)
                {
                    string kind = section.sectionName.ToLower();
                    if (!EquipmentKinds.Contains(kind))
                        continue;
                    foreach (var setting in section.settings.Where(st => st.NumValues() > 0))
                    {
                        string key = setting.settingName.ToLower();
                        if (key != "inherit" && key != "loot_appearance" && key != "pod_appearance")
                            continue;
                        string target = setting.Str(0);
                        string targetKind;
                        if (!defined.TryGetValue(target, out targetKind))
                        {
                            if (EquipExists(target))
                                Logger.LogInvalidValue(file, setting, "Has to be defined before this equipment! The game looks it up while it reads the file.");
                            else if (key == "inherit") // The other keys are reported by CheckEquipmentFile
                                Logger.LogInvalidValue(file, setting, "Equipment doesn't exist!");
                        }
                        else if (key == "inherit" && targetKind != kind)
                            Logger.LogInvalidValue(file, setting, "Is a [" + targetKind + "], so the game copies nothing from it!", LogLevel.warning);
                    }
                    string nick = Util.TryGetStrSetting(section, "nickname");
                    if (nick != null && !defined.ContainsKey(nick))
                        defined[nick] = kind;
                }
            }
        }

        /// <summary>
        /// Checks the goods the way common.dll reads them, in the order of freelancer.ini:
        /// - hull is looked up among the goods read so far, and the game crashes if it isn't one of them.
        /// - A commodity or equipment good without equipment crashes the game.
        /// - good_buy_price, bad_buy_price, good_sell_price and bad_sell_price multiply the price read so far, so
        ///   before price they are lost.
        /// - The game warns about a commodity or equipment good whose nickname isn't its equipment, and about [GoodType].
        /// </summary>
        private static void CheckGoodReadOrder()
        {
            var defined = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] priceFactors = { "good_buy_price", "bad_buy_price", "good_sell_price", "bad_sell_price" };
            foreach (string file in goodFiles)
            {
                FLDataFile ini = Checker.LoadIni(file);
                if (ini == null)
                    continue; // Reported by Checker.Parse
                foreach (var section in ini.sections)
                {
                    if (section.sectionName.ToLower() == "goodtype")
                    {
                        Logger.WLog(string.Format("{0}:{1}: [GoodType] is obsolete. The game ignores it.", file, Util.GetSectionIdentifier(section)));
                        continue;
                    }
                    if (section.sectionName.ToLower() != "good")
                        continue;

                    bool gotPrice = false;
                    foreach (var setting in section.settings.Where(st => st.NumValues() > 0))
                    {
                        string key = setting.settingName.ToLower();
                        if (key == "price")
                            gotPrice = true;
                        else if (!gotPrice && priceFactors.Contains(key))
                            Logger.LogInvalidValue(file, setting, "Has to come after price! It multiplies the price, which is 0 before that.");
                        else if (key == "hull" && !defined.Contains(setting.Str(0)))
                            Logger.LogInvalidValue(file, setting, GoodExists(setting.Str(0))
                                ? "Has to be defined before this good! The game crashes otherwise."
                                : "Good doesn't exist! The game crashes.");
                    }

                    string nick = Util.TryGetStrSetting(section, "nickname");
                    string category = Util.TryGetStrSetting(section, "category");
                    string equipment = Util.TryGetStrSetting(section, "equipment");
                    if (category != null && (category.ToLower() == "commodity" || category.ToLower() == "equipment"))
                    {
                        if (equipment == null)
                            Logger.ELog(string.Format("{0}:{1}: The {2} good {3} has no equipment! The game crashes.", file,
                                Util.GetSectionIdentifier(section), category.ToLower(), nick));
                        else if (nick != null && !nick.Equals(equipment, StringComparison.OrdinalIgnoreCase))
                            Logger.LogInvalidValue(file, section.GetSetting("equipment"), "Isn't the nickname of the good! The game warns about it.", LogLevel.warning);
                    }
                    if (nick != null)
                        defined.Add(nick);
                }
            }
        }

        private static void CheckEquipmentFile(string file)
        {
            FLDataFile ini = new FLDataFile(Path.Combine(Checker.flDataPath, file), true);

            foreach (var section in ini.sections)
            {
                string nick = Util.TryGetStrSetting(section, "nickname");
                if (nick != null)
                    nick = nick.ToLower();
                string cat = section.sectionName.ToLower();

                if (EquipmentKinds.Contains(cat))
                {
                    Util.CheckIDInfo(section, "ids_info", nick, file);
                    Util.CheckIDName(section, "ids_name", nick, file);

                    // A missing DA_archetype is reported by ParseEquipFile

                    // The client loads every material_library
                    foreach (var mat in section.GetSettings("material_library").Where(m => m.NumValues() > 0))
                    {
                        if (!Checker.FileExists(mat.Str(0)))
                            Logger.LogFileNotFound(file, mat);
                    }

                    string expl = Util.TryGetStrSetting(section, "explosion_arch");
                    if (expl != null && !FXChecks.ExplosionExists(expl))
                        Logger.LogInvalidValue(file, section.GetSetting("explosion_arch"), "Explosion doesn't exist!");

                    foreach (var type in section.GetSettings("type"))
                        Util.CheckObjectType(file, type);
                }
                // The launchers flash a light animation of fx\lightanim.ini when they fire (freelancer.exe)
                if (cat == "gun" || cat == "minedropper" || cat == "countermeasuredropper")
                {
                    foreach (var lightAnim in section.GetSettings("light_anim").Where(s => s.NumValues() > 0))
                    {
                        if (!FXChecks.LightAnimExists(lightAnim.Str(0)))
                            Logger.LogInvalidValue(file, lightAnim, "Light animation doesn't exist in fx\\lightanim.ini!");
                    }
                }
                if (hasLootAppearance.Contains(cat))
                {
                    string app = Util.TryGetStrSetting(section, "loot_appearance");
                    if (app != null && !EquipExists(app, "lootcrate"))
                        Logger.LogInvalidValue(file, section.GetSetting("loot_appearance"), "LootCrate doesn't exist!");
                }
                if (attachedEquipmentKinds.Contains(cat))
                {
                    // This refers to an [Explosion], e.g. sever_debris in fx\explosions.ini
                    string expl = Util.TryGetStrSetting(section, "separation_explosion");
                    if (expl != null && !FXChecks.ExplosionExists(expl))
                        Logger.LogInvalidValue(file, section.GetSetting("separation_explosion"), "Explosion doesn't exist!");

                    string debris = Util.TryGetStrSetting(section, "debris_type");
                    if (debris != null && !FXChecks.DebrisExists(debris))
                        Logger.LogInvalidValue(file, section.GetSetting("debris_type"), "Debris doesn't exist!");

                    string hp_child = Util.TryGetStrSetting(section, "hp_child");
                    if (hp_child != null && HasEquipUtf(cat, nick) && !equipUtf[cat][nick].HardpointExists(hp_child))
                        Logger.LogHardpoint(file, section.GetSetting("hp_child"));
                }

                switch (section.sectionName.ToLower())
                {
                    case "cargopod": // see general checks
                        break;
                    case "shield": // see general checks
                        break;
                    case "power": // see general checks
                        break;
                    case "repairkit": // see general checks
                        break;
                    case "shieldbattery": // see general checks
                        break;
                    case "lootcrate": // see general checks
                        break;
                    case "scanner": // see general checks
                        break;

                    case "countermeasure":
                        string cm_eff = Util.TryGetStrSetting(section, "const_effect");
                        string cm_snd = Util.TryGetStrSetting(section, "one_shot_sound");

                        if (cm_eff != null && !FXChecks.EffectExists(cm_eff))
                            Logger.LogInvalidValue(file, section.GetSetting("const_effect"), "Effect doesn't exist!");
                        if (cm_snd != null && !AudioChecks.SoundExists(cm_snd))
                            Logger.LogInvalidValue(file, section.GetSetting("one_shot_sound"), "Sound doesn't exist!");
                        break;
                    case "attachedfx":
                        string afx_particles = Util.TryGetStrSetting(section, "particles");
                        if (afx_particles != null && !FXChecks.EffectExists(afx_particles))
                            Logger.LogInvalidValue(file, section.GetSetting("particles"), "Effect doesn't exist!");
                        break;
                    case "commodity":
                        string comm_podApp = Util.TryGetStrSetting(section, "pod_appearance");
                        if (comm_podApp != null && !EquipExists(comm_podApp, "cargopod"))
                            Logger.LogInvalidValue(file, section.GetSetting("pod_appearance"), "CargoPod doesn't exist!");
                        break;
                    case "shieldgenerator":
                        string sh_collapse_snd = Util.TryGetStrSetting(section, "shield_collapse_sound");
                        string sh_rebuilt_snd = Util.TryGetStrSetting(section, "shield_rebuilt_sound");
                        string sh_collapse_eff = Util.TryGetStrSetting(section, "shield_collapse_particle");

                        if (sh_collapse_snd != null && !AudioChecks.SoundExists(sh_collapse_snd))
                            Logger.LogInvalidValue(file, section.GetSetting("shield_collapse_sound"), "Sound doesn't exist!");
                        if (sh_rebuilt_snd != null && !AudioChecks.SoundExists(sh_rebuilt_snd))
                            Logger.LogInvalidValue(file, section.GetSetting("shield_rebuilt_sound"), "Sound doesn't exist!");
                        if (sh_collapse_eff != null && !FXChecks.EffectExists(sh_collapse_eff))
                            Logger.LogInvalidValue(file, section.GetSetting("shield_collapse_particle"), "Effect doesn't exist!");
                        CheckHpTypeList(file, section, "hp_shield_type");
                        // shield_hit_effects = threshold, effect, ... (repeatable)
                        foreach (var hitEffects in section.GetSettings("shield_hit_effects"))
                        {
                            for (int i = 1; i < hitEffects.NumValues(); i++)
                            {
                                if (hitEffects.Str(i) != "" && !FXChecks.EffectExists(hitEffects.Str(i)))
                                    Logger.LogInvalidValue(file, hitEffects, "Effect doesn't exist!", hitEffects.Str(i));
                            }
                        }
                        break;
                    case "tradelane":
                        foreach (var setting in section.settings)
                        {
                            switch (setting.settingName)
                            {
                                // The effects of the player's and other ships, and of the ring (freelancer.exe)
                                case "tl_player_enter":
                                case "tl_player_travel":
                                case "tl_player_splash":
                                case "tl_player_exit":
                                case "tl_player_disrupt":
                                case "tl_ring_active":
                                case "tl_ship_disrupt":
                                case "tl_ship_enter":
                                case "tl_ship_splash":
                                case "tl_ship_exit":
                                case "tl_ship_travel":
                                    if(!FXChecks.EffectExists(setting.Str(0)))
                                        Logger.LogInvalidValue(file, setting, "Effect not found!");
                                    break;
                            }
                        }
                        break;
                    case "thruster":
                        string thr_hp_particles = Util.TryGetStrSetting(section, "hp_particles");
                        string thr_particles = Util.TryGetStrSetting(section, "particles");

                        if (thr_hp_particles != null && HasEquipUtf(cat, nick) && !equipUtf[cat][nick].HardpointExists(thr_hp_particles))
                            Logger.LogHardpoint(file, section.GetSetting("hp_particles"));
                        if (thr_particles != null && !FXChecks.EffectExists(thr_particles))
                            Logger.LogInvalidValue(file, section.GetSetting("particles"), "Effect doesn't exist!");
                        break;
                    case "engine":
                        foreach (var setting in section.settings)
                        {
                            if (setting.NumValues() == 0)
                                continue;
                            string val = setting.Str(0);
                            switch (setting.settingName)
                            {
                                case "character_loop_sound":
                                case "character_start_sound":
                                case "cruise_backfire_sound":
                                case "cruise_disrupt_sound":
                                case "cruise_loop_sound":
                                case "cruise_start_sound":
                                case "cruise_stop_sound":
                                case "engine_kill_sound":
                                case "rumble_sound":
                                    if (!AudioChecks.SoundExists(val))
                                        Logger.LogInvalidValue(file, setting, "Sound not found!");
                                    break;
                                case "trail_effect":
                                case "trail_effect_player":
                                case "cruise_disrupt_effect":
                                case "flame_effect":
                                    if (!FXChecks.EffectExists(val))
                                        Logger.LogInvalidValue(file, setting, "Effect not found!");
                                    break;
                            }
                        }
                        break;
                    case "light":
                        break; // inherit: see CheckEquipmentReadOrder
                    case "countermeasuredropper":
                        string cmd_particle = Util.TryGetStrSetting(section, "flash_particle_name");
                        string cmd_archtype = Util.TryGetStrSetting(section, "projectile_archetype");

                        if (cmd_particle != null && !FXChecks.EffectExists(cmd_particle))
                            Logger.LogInvalidValue(file, section.GetSetting("flash_particle_name"), "Effect not found!");
                        if (cmd_archtype != null && !EquipExists(cmd_archtype, "countermeasure"))
                            Logger.LogInvalidValue(file, section.GetSetting("projectile_archetype"), "CounterMeasure not found!");
                        break;
                    case "tractor":
                        string trac_op_eff = Util.TryGetStrSetting(section, "operating_effect");
                        string trac_snd = Util.TryGetStrSetting(section, "tractor_complete_snd");

                        if (trac_op_eff != null && !FXChecks.EffectExists(trac_op_eff))
                            Logger.LogInvalidValue(file, section.GetSetting("operating_effect"), "Effect doesn't exist!");
                        if (trac_snd != null && !AudioChecks.SoundExists(trac_snd))
                            Logger.LogInvalidValue(file, section.GetSetting("tractor_complete_snd"), "Sound doesn't exist!");
                        break;
                    case "cloakingdevice":
                        string cd_in_eff = Util.TryGetStrSetting(section, "cloakin_fx");
                        string cd_out_eff = Util.TryGetStrSetting(section, "cloakout_fx");

                        if (cd_in_eff != null && !FXChecks.EffectExists(cd_in_eff))
                            Logger.LogInvalidValue(file, section.GetSetting("cloakin_fx"), "Effect doesn't exist!");
                        if (cd_out_eff != null && !FXChecks.EffectExists(cd_out_eff))
                            Logger.LogInvalidValue(file, section.GetSetting("cloakout_fx"), "Effect doesn't exist!");
                        break;
                    case "explosion":
                        string expl_eff = Util.TryGetStrSetting(section, "effect");
                        if(expl_eff != null && !FXChecks.EffectExists(expl_eff))
                            Logger.LogInvalidValue(file, section.GetSetting("effect"), "Effect doesn't exist!");
                        break;
                    case "gun":
                    /*
[Gun] light_anim => [LightAnim] TODO
                     */
                        string gun_snd = Util.TryGetStrSetting(section, "dry_fire_sound");
                        string gun_particle = Util.TryGetStrSetting(section, "flash_particle_name");
                        string gun_projArch = Util.TryGetStrSetting(section, "projectile_archetype");

                        if (gun_snd != null && !AudioChecks.SoundExists(gun_snd))
                            Logger.LogInvalidValue(file, section.GetSetting("dry_fire_sound"), "Sound doesn't exist!");
                        if(gun_particle != null && !FXChecks.EffectExists(gun_particle))
                            Logger.LogInvalidValue(file, section.GetSetting("flash_particle_name"), "Effect doesn't exist!");
                        CheckHpTypeList(file, section, "hp_gun_type");
                        if (gun_projArch != null && !EquipExists(gun_projArch, "munition"))
                            Logger.LogInvalidValue(file, section.GetSetting("projectile_archetype"), "Munition doesn't exist!");

                        break;
                    case "mine":
                        string m_const_effect = Util.TryGetStrSetting(section, "const_effect");
                        string m_snd = Util.TryGetStrSetting(section, "one_shot_sound");

                        if (m_const_effect != null && !FXChecks.EffectExists(m_const_effect))
                            Logger.LogInvalidValue(file, section.GetSetting("const_effect"), "Effect doesn't exist!");
                        if (m_snd != null && !AudioChecks.SoundExists(m_snd))
                            Logger.LogInvalidValue(file, section.GetSetting("one_shot_sound"), "Sound doesn't exist!");
                        break;
                    case "minedropper":
                        string md_snd = Util.TryGetStrSetting(section, "dry_fire_sound");
                        string md_projArch = Util.TryGetStrSetting(section, "projectile_archetype");

                        if (md_snd != null && !AudioChecks.SoundExists(md_snd))
                            Logger.LogInvalidValue(file, section.GetSetting("dry_fire_sound"), "Sound doesn't exist!");
                        if (md_projArch != null && !EquipExists(md_projArch, "mine"))
                            Logger.LogInvalidValue(file, section.GetSetting("projectile_archetype"), "Mine doesn't exist!");
                        break;
                    case "munition":
                        string mun_const_eff = Util.TryGetStrSetting(section, "const_effect");
                        string mun_hit_eff = Util.TryGetStrSetting(section, "munition_hit_effect");
                        string mun_hp_type = Util.TryGetStrSetting(section, "hp_type");
                        string mun_motor = Util.TryGetStrSetting(section, "motor");
                        string mun_snd = Util.TryGetStrSetting(section, "one_shot_sound");
                        string mun_seeker = Util.TryGetStrSetting(section, "seeker");
                        string mun_weaponType = Util.TryGetStrSetting(section, "weapon_type");
                        string mun_trailParent = Util.TryGetStrSetting(section, "hp_trail_parent");

                        if(mun_const_eff != null && !FXChecks.EffectExists(mun_const_eff))
                            Logger.LogInvalidValue(file, section.GetSetting("const_effect"), "Effect not found!");
                        if(mun_hit_eff != null && !FXChecks.EffectExists(mun_hit_eff))
                            Logger.LogInvalidValue(file, section.GetSetting("munition_hit_effect"), "Effect not found!");
                        if(mun_hp_type != null && !HpTypeExists(mun_hp_type))
                            Logger.LogInvalidValue(file, section.GetSetting("hp_type"), "Invalid hardpoint type!");
                        if(mun_motor != null && !EquipExists(mun_motor, "motor"))
                            Logger.LogInvalidValue(file, section.GetSetting("motor"), "Motor doesn't exist!");
                        if(mun_snd != null && !AudioChecks.SoundExists(mun_snd))
                            Logger.LogInvalidValue(file, section.GetSetting("one_shot_sound"), "Sound doesn't exist!");
                        if (mun_seeker != null && (mun_seeker.ToLower() != "dumb" && mun_seeker.ToLower() != "lock"))
                            Logger.LogInvalidValue(file, section.GetSetting("seeker"), "Must be DUMB or LOCK!");
                        // The damage modifiers of the [WeaponType] in WeaponModDB.ini
                        if (mun_weaponType != null && !WeaponTypeExists(mun_weaponType))
                            Logger.LogInvalidValue(file, section.GetSetting("weapon_type"), "Weapon type doesn't exist!");
                        // The trail (e.g. a missile's exhaust) is attached to this hardpoint of the munition's model
                        if (mun_trailParent != null && HasEquipUtf(cat, nick) && !equipUtf[cat][nick].HardpointExists(mun_trailParent))
                            Logger.LogHardpoint(file, section.GetSetting("hp_trail_parent"));
                        break;
                    case "lod":
                        string obj = Util.TryGetStrSetting(section, "obj");
                        if (obj != null && (obj.ToLower() != "root" && obj.ToLower() != "barrel" && obj.ToLower() != "guns"))
                            Logger.LogInvalidValue(file, section.GetSetting("obj"), "Must be root, barrel, or guns!");
                        break;
                    case "armor":
                    case "internalfx":
                    case "repairdroid":
                    case "motor": // TODO
                        break;
                    default:
                        Logger.WLog(string.Format("Unexpected section {0} in file {1}", section.sectionName, file));
                        break;
                }
            }
        }

        private static void CheckGoodFile(string file)
        {
            FLDataFile ini = new FLDataFile(Path.Combine(Checker.flDataPath, file), true);

            foreach (var section in ini.sections)
            {
                switch (section.sectionName.ToLower())
                {
                    case "good":
                                                                                                                     // Removed , str_ids_info, str_ids_name
                        string nick, category, equipment, item_icon, material_library, shop_archetype, msg_id_prefix, ship, hull;
                        string[][] addons;
                        FLDataFile.Setting[] addonSettings;
                        //uint ids_info, ids_name; unused

                        if (section.SettingExists("nickname"))
                        {
                            nick = section.GetSetting("nickname").Str(0);
                        }
                        else
                        {
                            Logger.LogSettingNotFound(file, Util.GetSectionIdentifier(section), "Good", "nickname");
                            break;
                        }

                        category = Util.TryGetStrSetting(section, "category");
                        equipment = Util.TryGetStrSetting(section, "equipment");
                        ship = Util.TryGetStrSetting(section, "ship");
                        hull = Util.TryGetStrSetting(section, "hull");
                        addonSettings = section.GetSettings("addon").ToArray();
                        addons = addonSettings.Select(a => a.values.Select(s => s.ToString()).ToArray()).ToArray();
                        item_icon = Util.TryGetStrSetting(section, "item_icon");
                        material_library = Util.TryGetStrSetting(section, "material_library");
                        shop_archetype = Util.TryGetStrSetting(section, "shop_archetype");
                        msg_id_prefix = Util.TryGetStrSetting(section, "msg_id_prefix");

                        // name and info_desc are aliases of ids_name and ids_info (common.dll)
                        Util.CheckIDName(section, "ids_name", nick, file);
                        Util.CheckIDName(section, "name", nick, file);
                        Util.CheckIDInfo(section, "ids_info", nick, file);
                        Util.CheckIDInfo(section, "info_desc", nick, file);

                        if (category != null && !GoodCategories.Contains(category.ToLower()))
                            Logger.LogInvalidValue(file, section.GetSetting("category"), "Not a valid category!");

                        if (equipment != null && !EquipExists(equipment))
                            Logger.LogInvalidValue(file, section.GetSetting("equipment"), "Equipment doesn't exist!");

                        if (category == null)
                        {
                            Logger.LogSettingNotFound(file, nick, section.sectionName, "category");
                            break; // continue with the next section
                        }

                        switch (category.ToLower())
                        {
                            case "ship":
                                if (hull == null)
                                {
                                    Logger.LogSettingNotFound(file, nick, section.sectionName, "hull");
                                    break;
                                }
                                hull = hull.ToLower();
                                if (!shipHulls.ContainsKey(hull))
                                {
                                    Logger.LogInvalidValue(file, section.GetSetting("hull"), "Ship doesn't exist!", hull);
                                    break;
                                }

                                if (Checker.DisableUTF)
                                    break;
                                
                                if (!ShipChecks.shipUTF.ContainsKey(shipHulls[hull]))
                                {
                                    Logger.LogInvalidValue(file, section.GetSetting("hull"), "Ship hull not found", shipHulls[hull]);
                                    break;
                                }

                                UtfFile shipUtf = ShipChecks.shipUTF[shipHulls[hull]];
                                for (int i = 0; i < addons.Length; i++)
                                {
                                    string hp, equip;

                                    // addon = equipment, hardpoint or internal[, count[, mid or high]] (common.dll)
                                    if (addons[i].Length >= 2 && addons[i].Length <= 4)
                                    {
                                        equip = addons[i][0];
                                        hp = addons[i][1];

                                        if (!EquipExists(equip))
                                            Logger.LogInvalidValue(file, addonSettings[i], "Equip doesn't exist!", equip);
                                        if (Checker.DisableUTF)
                                            break;
                                        if (hp.ToLower() != "internal")
                                        {
                                            if (shipUtf != null && !shipUtf.HardpointExists(hp)
                                                || shipUtf == null && !Checker.AssumeVanillaFilesExist)
                                                Logger.LogHardpoint(file, addonSettings[i], hp);
                                        }
                                            
                                    }
                                    else
                                        Logger.LogArgCount(file, addonSettings[i], 2, 3, 4);
                                }
                                break;
                            case "equipment":
                            case "commodity":
                                break;
                            case "shiphull":
                                if (ship == null)
                                    Logger.LogSettingNotFound(file, nick, section.sectionName, "ship");
                                else if (!ShipChecks.ShipExists(ship))
                                    Logger.LogInvalidValue(file, section.GetSetting("ship"), "Ship doesn't exist!");
                                break;
                        }

                        if (item_icon != null && !Checker.FileExists(item_icon))
                            Logger.LogFileNotFound(file, section.GetSetting("item_icon"));
                        if (material_library != null && !Checker.FileExists(material_library))
                            Logger.LogFileNotFound(file, section.GetSetting("material_library"));
                        if (shop_archetype != null && !Checker.FileExists(shop_archetype))
                            Logger.LogFileNotFound(file, section.GetSetting("shop_archetype"));

                        if (msg_id_prefix != null && !AudioChecks.VoiceMsgExists(msg_id_prefix))
                            Logger.LogInvalidValue(file, section.GetSetting("msg_id_prefix"), "Msg doesn't exist!");

                        break;
                    case "goodtype": // Obsolete, see CheckGoodReadOrder
                        break;
                    default:
                        Logger.WLog(string.Format("Unexpected section {0} in file {1}", section.sectionName, file));
                        break;
                }
            }
        }

        private static void CheckMarketFile(string file)
        {
            FLDataFile ini = new FLDataFile(Path.Combine(Checker.flDataPath, file), true);

            foreach (var section in ini.sections)
            {
                switch (section.sectionName.ToLower())
                {
                    case "basegood":
                        string Base = Util.TryGetStrSetting(section, "base");
                        if (Base == null)
                        {
                            Logger.LogSettingNotFound(file, Util.GetSectionIdentifier(section), "BaseGood", "base");
                            break;
                        }

                        if(!UniverseChecks.BaseExists(Base))
                            Logger.LogInvalidValue(file, section.GetSetting("base"), "Base doesn't exist!");
                        int baseIndex = section.settings.IndexOf(section.GetSetting("base"));

                        var MarketGoods = section.GetSettings("MarketGood").ToArray();
                        foreach (var marketGood in MarketGoods)
                        {
                            if (marketGood.NumValues() == 0)
                            {
                                Logger.LogArgCount(file, marketGood, 1);
                                continue;
                            }

                            // MarketGood = good, level, rep, min, max, transaction type, price multiplier.
                            // Everything after the good is optional; an empty value keeps its default (common.dll).
                            string[] args = marketGood.values.Select(s => s.ToString().Trim()).ToArray();
                            string equipNick = args[0];
                            Func<int, string> arg = i => i < args.Length && args[i] != "" ? args[i] : null;
                            float level = 0, rep = -1, multip = 1;
                            int min = 0, max = 0, transactionType = 0;

                            if (arg(1) != null && !float.TryParse(arg(1), NumberStyles.Float, CultureInfo.InvariantCulture, out level))
                                Logger.LogInvalidValue(file, marketGood, "Invalid level! Has to be a number!", arg(1));
                            if (arg(2) != null)
                            {
                                if (!float.TryParse(arg(2), NumberStyles.Float, CultureInfo.InvariantCulture, out rep))
                                    Logger.LogInvalidValue(file, marketGood, "Invalid rep! Has to be a number!", arg(2));
                                else if (rep < -1 || rep > 1)
                                    Logger.LogInvalidValue(file, marketGood, "Invalid rep! Has to be between -1 and 1!", arg(2));
                            }
                            if (arg(3) != null && !int.TryParse(arg(3), out min))
                                Logger.LogInvalidValue(file, marketGood, "Invalid min! Has to be a number!", arg(3));
                            if (arg(4) != null && !int.TryParse(arg(4), out max))
                                Logger.LogInvalidValue(file, marketGood, "Invalid max! Has to be a number!", arg(4));
                            // 0: the base buys and sells it, 1: the base only buys it, 2: the base only sells it
                            if (arg(5) != null && (!int.TryParse(arg(5), out transactionType) || transactionType < 0 || transactionType > 2))
                                Logger.LogInvalidValue(file, marketGood, "Invalid transaction type! Has to be 0, 1 or 2!", arg(5));
                            if (arg(6) != null && !float.TryParse(arg(6), NumberStyles.Float, CultureInfo.InvariantCulture, out multip))
                                Logger.LogInvalidValue(file, marketGood, "Invalid multiplier! Has to be a number!", arg(6));

                            if (!GoodExists(equipNick))
                                Logger.LogInvalidValue(file, marketGood, "Equipment doesn't exist!", equipNick);

                            // Common.dll only adds a line once a base line came before it, and drops a line whose
                            // values are all the defaults (level 0, rep -1, min 0, max 0, type 0, multiplier 1)
                            if (section.settings.IndexOf(marketGood) < baseIndex)
                                Logger.LogInvalidValue(file, marketGood, "The game ignores this line, because it comes before the base line!", LogLevel.warning);
                            else if (level == 0 && rep == -1 && min == 0 && max == 0 && transactionType == 0 && multip == 1)
                                Logger.LogInvalidValue(file, marketGood, "The game ignores this line, because all its values are the defaults!", LogLevel.warning);
                        }

                        // Common.dll warns about any other key
                        foreach (var setting in section.settings.Where(st => st.settingName.ToLower() != "base" && st.settingName.ToLower() != "marketgood"))
                            Logger.LogInvalidValue(file, setting, "Unknown key! The game ignores it.", setting.settingName, LogLevel.warning);
                        break;
                    default:
                        Logger.WLog(string.Format("Unexpected section {0} in file {1}", section.sectionName, file));
                        break;
                }
            }
        }

        public static void ParseEquipmentFolder()
        {
            Logger.ILog("Parsing Equipment-Folder");
            FLDataFile.Section data = Checker.flIni.sections.First(s => s.sectionName.ToLower() == "data");

            equipmentFiles = data.GetSettings("equipment").Select(s => s.Str(0)).ToArray();
            goodFiles = data.GetSettings("goods").Select(s => s.Str(0)).ToArray();
            marketFiles = data.GetSettings("markets").Select(s => s.Str(0)).ToArray();
            weaponModFiles = data.GetSettings("WeaponModDB").Select(s => s.Str(0)).ToArray();
            equipUtf = new Dictionary<string, Dictionary<string, UtfFile>>();
            shieldTypes = new HashSet<string>(vanillaShieldTypes);

            var equipNicks = new Dictionary<string, List<string>>();
            List<string> explosionNicks = new List<string>();
            LinkedList<string> str = new LinkedList<string>();
            List<string> goodNicknames = new List<string>();
            List<string> weaponTypes = new List<string>();
            shipHulls = new Dictionary<string, string>();
            marketEntries = new Dictionary<string, string[]>();

            foreach (var equipFile in equipmentFiles)
            {
                ParseEquipFile(equipFile, ref explosionNicks, ref equipNicks);
            }
            foreach (var goodFile in goodFiles)
            {
                ParseGoodFile(goodFile, ref goodNicknames);
            }
            foreach (var marketFile in marketFiles)
            {
                ParseMarketFile(marketFile);
            }
            foreach (var weaponModFile in weaponModFiles)
            {
                // Missing files are reported by Checker.Parse
                FLDataFile ini = Checker.LoadIni(weaponModFile);
                if (ini == null)
                    continue;
                weaponTypes.AddRange(ini.GetSettings("WeaponType", "nickname").Select(s => s.Str(0).Trim().ToLower()));
            }

            AllEquipNicks = equipNicks.SelectMany(n => n.Value).ToArray();
            EquipmentChecks.equipNicks = equipNicks.Select(n => new KeyValuePair<string, string[]>(n.Key, n.Value.ToArray())).ToDictionary(n => n.Key, n => n.Value);
            EquipmentChecks.goodNicknames = goodNicknames.ToArray();
            EquipmentChecks.weaponTypes = weaponTypes.ToArray();
            FXChecks.AddExplosions(explosionNicks);
            Logger.ILog("Finished Equipment-Folder");
        }

        private static void ParseEquipFile(string file, ref List<string> explosionNicks, ref Dictionary<string, List<string>> equipNicks)
        {
            // Missing files are reported by Checker.Parse
            FLDataFile ini = Checker.LoadIni(file);
            if (ini == null)
                return;
            var sections = ini.sections;

            foreach (var section in sections)
            {
                switch (section.sectionName.ToLower())
                {
                    case "armor":
                    case "attachedfx":
                    case "internalfx":
                    case "power":
                    case "thruster":
                    case "shield":
                    case "shieldgenerator":
                    case "shieldbattery":
                    case "repairkit":
                    case "repairdroid":
                    case "countermeasure":
                    case "countermeasuredropper":
                    case "gun":
                    case "minedropper":
                    case "mine":
                    case "munition":
                    case "motor":
                    case "scanner":
                    case "tractor":
                    case "engine":
                    case "cloakingdevice":
                    case "tradelane":
                    case "cargopod":
                    case "lootcrate":
                    case "commodity":
                    case "light":
                        string cat = section.sectionName.ToLower();
                        if(!equipNicks.ContainsKey(cat))
                            equipNicks[cat] = new List<string>();
                        if(!equipUtf.ContainsKey(cat))
                            equipUtf[cat] = new Dictionary<string, UtfFile>();

                        var nickUtfTuple = Util.ParseNickUTF(Checker.flDataPath, file, section, "nickname", Util.DaArchetypeKey(section), false);
                        if (nickUtfTuple.Nick == null)
                            break;
                        nickUtfTuple.Nick = nickUtfTuple.Nick.ToLower();

                        equipNicks[cat].Add(nickUtfTuple.Nick);

                        if (!Checker.DisableUTF && nickUtfTuple.UtfFile != null)
                        {
                            equipUtf[cat][nickUtfTuple.Nick] = nickUtfTuple.UtfFile;
                        }

                        if (cat == "shieldgenerator")
                        {
                            string shieldType = Util.TryGetStrSetting(section, "shield_type");
                            if (shieldType != null)
                                shieldTypes.Add(shieldType.ToLower());
                        }
                        break;
                    case "explosion":
                        string explNick = Util.TryGetStrSetting(section, "nickname");
                        if (explNick == null)
                            Logger.LogSettingNotFound(file, Util.GetSectionIdentifier(section), section.sectionName, "nickname");
                        else
                            explosionNicks.Add(explNick.ToLower());
                        break;
                    case "lod":
                        break;
                }
            }
        }

        private static void ParseGoodFile(string file, ref List<string> goodNicknames)
        {
            // Missing files are reported by Checker.Parse
            FLDataFile ini = Checker.LoadIni(file);
            if (ini == null)
                return;
            foreach (var section in ini.sections)
            {
                switch (section.sectionName.ToLower())
                {
                    case "good":
                        string nick = Util.TryGetStrSetting(section, "nickname");
                        if (nick == null)
                            break; // Reported by CheckGoodFile
                        nick = nick.ToLower();
                        goodNicknames.Add(nick);

                        string category = Util.TryGetStrSetting(section, "category");
                        if (category == "shiphull")
                        {
                            string ship = Util.TryGetStrSetting(section, "ship");
                            if (ship == null)
                                break; // Reported by CheckGoodFile
                            Util.AddUnique(shipHulls, nick, ship.ToLower(), file, section, "good");
                        }
                        break;
                }
            }
        }

        private static void ParseMarketFile(string file)
        {
            // Missing files are reported by Checker.Parse
            FLDataFile ini = Checker.LoadIni(file);
            if (ini == null)
                return;
            var sections = ini.sections;

            foreach (var section in sections)
            {
                switch (section.sectionName.ToLower())
                {
                    case "basegood":
                        string Base = Util.TryGetStrSetting(section, "base");
                        if (Base == null)
                            break; // Reported by CheckMarketFile
                        string[] MarketGoods = section.GetSettings("MarketGood").Select(s => s.Str(0).ToLower()).ToArray();
                        if (marketEntries.ContainsKey(Base))
                            marketEntries[Base] = marketEntries[Base].Union(MarketGoods).ToArray();
                        else
                            marketEntries[Base] = MarketGoods;
                        break;
                }
            }
        }

        public static string GetCategory(string nick)
        {
            nick = nick.ToLower();
            foreach (var nicks in equipNicks)
            {
                if (nicks.Value.Contains(nick))
                    return nicks.Key;
            }
            return null;
        }

        /// <summary>
        /// True if equipment with this nickname exists in the given (lowercase) category, e.g. "munition".
        /// </summary>
        public static bool EquipExists(string nick, string category)
        {
            string[] nicks;
            return equipNicks.TryGetValue(category, out nicks) && nicks.Contains(nick.ToLower());
        }

        private static bool HasEquipUtf(string category, string nick)
        {
            Dictionary<string, UtfFile> utfs;
            return nick != null && !Checker.DisableUTF && equipUtf.TryGetValue(category, out utfs) && utfs.ContainsKey(nick);
        }

        public static bool EquipExists(string nick)
        {
            nick = nick.ToLower();
            return AllEquipNicks.Contains(nick);
        }

        public static bool GoodExists(string nick)
        {
            nick = nick.ToLower();
            return goodNicknames.Contains(nick);
        }

        public static bool HpTypeExists(string hpType)
        {
            hpType = hpType.ToLower();
            return hpTypes.Contains(hpType);
        }

        public static bool ShieldTypeExists(string type)
        {
            type = type.ToLower();
            return shieldTypes.Contains(type);
        }

        public static bool WeaponTypeExists(string type)
        {
            type = type.ToLower();
            return weaponTypes.Contains(type);
        }
    }
}
