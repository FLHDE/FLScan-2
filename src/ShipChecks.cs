using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

using FLScanIE.Util_Functions;
using FLScanIE.Logging;

namespace FLScanIE
{
    class ShipChecks
    {
        private static string[] shipFiles;
        private static string[] shipNicknames;
        public static Dictionary<string, UtfFile> shipUTF; 

        private static string[] simpleNicknames;
        private static Dictionary<string, UtfFile> simpleUTF;

        private static string[] shipTypes = Util.LoadConfig("ShipTypes");
        private static string[] collisionGroupTypes = Util.LoadConfig("CollisionGroupTypes"); // TODO are these values from the UTF?

        public static void CheckShipFolder()
        {
            Logger.ILog("Checking Ship-Folder");
            Util.RunChecks(CheckShipFile, shipFiles);
            Logger.ILog("Finished Ship-Folder");
        }

        private static void CheckShipFile(string file)
        {
            FLDataFile ini = new FLDataFile(Path.Combine(Checker.flDataPath, file), true);

            // A [CollisionGroup] belongs to the [Ship] above it
            string currentShip = null;

            foreach (var section in ini.sections)
            {
                switch (section.sectionName.ToLower())
                {
                    case "ship":
                        currentShip = Util.TryGetStrSetting(section, "nickname");
                        CheckShip(file, section);
                        break;
                    case "collisiongroup":
                        CheckCollGroup(file, section, currentShip);
                        break;
                    case "simple":
                        currentShip = null;
                        CheckSimpe(file, section);
                        break;
                }
            }
        }

        private static void CheckShip(string file, FLDataFile.Section ship)
        {
            string nick = Util.TryGetStrSetting(ship, "nickname");
            if (nick == null)
                return; // Already reported while parsing

            string type = Util.TryGetStrSetting(ship, "type");
            if (type == null)
                Logger.LogSettingNotFound(file, nick, "Ship", "type");
            else if (!shipTypes.Contains(type.ToLower()))
                Logger.LogInvalidValue(file, ship.GetSetting("type"), "Ship type doesn't exist!");

            Util.CheckIDName(ship, "ids_name", nick, file);
            Util.CheckIDInfo(ship, "ids_info", nick, file);

            for (int i = 1; i <= 3; i++)
                Util.CheckIDInfo(ship, "ids_info" + i, nick, file);

            var fuses = ship.GetSettings("fuse");
            foreach (var fuse in fuses)
            {
                Util.CheckNumberOfArgs(fuse, file, 3);
                if (!FXChecks.FuseExists(fuse.Str(0)))
                    Logger.LogInvalidValue(file, fuse, "Fuse doesn't exist!");
            }

            var materials = ship.GetSettings("material_library");
            foreach (var material in materials)
            {
                if (!Checker.FileExists(material.Str(0)))
                    Logger.LogFileNotFound(file, material);
            }

            if (ship.SettingExists("explosion_arch"))
            {
                if (!FXChecks.ExplosionExists(ship.GetSetting("explosion_arch").Str(0)))
                    Logger.LogInvalidValue(file, ship.GetSetting("explosion_arch"), "Explosion doesn't exist!");
            }
            //else
            //    Logger.LogSettingNotFound(file, nick, "Ship", "explosion_arch");

            var surfaceEffects = ship.GetSettings("surface_hit_effects");
            foreach (var surfaceEffect in surfaceEffects)
            {
                Util.CheckNumberOfArgs(surfaceEffect, file, 4);
                for (int i = 1; i < surfaceEffect.NumValues(); i++)
                    if (!FXChecks.EffectExists(surfaceEffect.Str(i)))
                        Logger.LogInvalidValue(file, surfaceEffect, "Effect doesn't exist!", surfaceEffect.Str(i));
            }

            if (ship.SettingExists("bay_doors_open_snd") && !AudioChecks.SoundExists(ship.GetSetting("bay_doors_open_snd").Str(0)))
                Logger.LogInvalidValue(file, ship.GetSetting("bay_doors_open_snd"), "Sound doesn't exist!");
            if (ship.SettingExists("bay_doors_close_snd") && !AudioChecks.SoundExists(ship.GetSetting("bay_doors_close_snd").Str(0)))
                Logger.LogInvalidValue(file, ship.GetSetting("bay_doors_close_snd"), "Sound doesn't exist!");
            if (ship.SettingExists("msg_id_prefix") && !AudioChecks.VoiceMsgExists(ship.GetSetting("msg_id_prefix").Str(0)))
                Logger.LogInvalidValue(file, ship.GetSetting("msg_id_prefix"), "Msg doesn't exist!");

            Util.CheckNumberOfArgs(ship, file, "camera_offset", 2);
            Util.CheckNumberOfArgs(ship, file, "steering_torque", 3);
            Util.CheckNumberOfArgs(ship, file, "angular_drag", 3);
            Util.CheckNumberOfArgs(ship, file, "rotation_inertia", 3);

            if (ship.SettingExists("HP_tractor_source") && ShipChecks.shipUTF.ContainsKey(nick) && !ShipChecks.HardpointExists(nick, ship.GetSetting("HP_tractor_source").Str(0)))
                Logger.LogHardpoint(file, ship.GetSetting("HP_tractor_source"));
            if (ship.SettingExists("HP_bay_surface") && ShipChecks.shipUTF.ContainsKey(nick) && !ShipChecks.HardpointExists(nick, ship.GetSetting("HP_bay_surface").Str(0)))
                Logger.LogHardpoint(file, ship.GetSetting("HP_bay_surface"));
            if (ship.SettingExists("HP_bay_external") && ShipChecks.shipUTF.ContainsKey(nick) && !ShipChecks.HardpointExists(nick, ship.GetSetting("HP_bay_external").Str(0)))
                Logger.LogHardpoint(file, ship.GetSetting("HP_bay_external"));

            if (!Checker.DisableUTF && ShipChecks.shipUTF.ContainsKey(nick) && !(shipUTF[nick] == null && Checker.AssumeVanillaFilesExist))
            {
                if (ship.SettingExists("num_exhaust_nozzles"))
                {
                    int engines_ini = (int) ship.GetSetting("num_exhaust_nozzles").Int32(0);
                    int engines_cmp = shipUTF[nick].hardpoints.Count(h => h.ToLower().StartsWith("hpengine"));

                    if (engines_ini > engines_cmp)
                        Logger.LogInvalidValue(file, ship.GetSetting("num_exhaust_nozzles"), 
                                               "Isn't smaller or equal than number of HpEngines in cmp (" + engines_cmp + ")!");
                }
                //else
                //    Logger.LogSettingNotFound(file, nick, "Ship", "num_exhaust_nozzles");
            }

            var hp_typeEntries = ship.GetSettings("hp_type");
            foreach (var hpTypeEntry in hp_typeEntries)
            {
                if (!EquipmentChecks.HpTypeExists(hpTypeEntry.Str(0).ToLower())) // TODO: check category
                    Logger.LogInvalidValue(file, hpTypeEntry, "Invalid hardpoint-type!", hpTypeEntry.Str(0));
                for (int i = 1; i < hpTypeEntry.NumValues(); i++)
                {
                    if (hpTypeEntry.Str(i).Trim().Length != 0 && !Checker.DisableUTF && shipUTF.ContainsKey(nick) 
                        && !(shipUTF[nick] == null && Checker.AssumeVanillaFilesExist) && !shipUTF[nick].HardpointExists(hpTypeEntry.Str(i)))
                        Logger.LogHardpoint(file, hpTypeEntry, hpTypeEntry.Str(i));
                }
            }

            var shieldLinks = ship.GetSettings("shield_link");
            foreach (var shieldLink in shieldLinks)
            {
                if (!EquipmentChecks.EquipExists(shieldLink.Str(0)))
                    Logger.LogInvalidValue(file, shieldLink, "Equip doesn't exist!");
                else if (EquipmentChecks.GetCategory(shieldLink.Str(0)) != "shield")
                    Logger.LogInvalidValue(file, shieldLink, "Equip isn't a shield!");

                for (int i = 1; i < shieldLink.NumValues(); i++)
                {
                    if (shieldLink.Str(i).Trim().Length != 0 && !Checker.DisableUTF && shipUTF.ContainsKey(nick) 
                        && !(shipUTF[nick] == null && Checker.AssumeVanillaFilesExist) && !shipUTF[nick].HardpointExists(shieldLink.Str(i)))
                        Logger.LogHardpoint(file, shieldLink, shieldLink.Str(i));
                }
            }

            if (ship.SettingExists("cockpit"))
            {
                string cockpit = ship.GetSetting("cockpit").Str(0);
                if (Checker.FileExistsOnDisk(cockpit))
                    CheckCockpitFile(cockpit);
                else if (!Checker.FileExists(cockpit)) // Vanilla files that aren't present can't be checked
                    Logger.LogFileNotFound(file, ship.GetSetting("cockpit"));
            }
            //else
            //    Logger.LogSettingNotFound(file, nick, "Ship", "cockpit");

            if (ship.SettingExists("pilot_mesh"))
            {
                // simpleNicknames are lowercase
                if (!simpleNicknames.Contains(ship.GetSetting("pilot_mesh").Str(0).ToLower()))
                    Logger.LogInvalidValue(file, ship.GetSetting("pilot_mesh"), "Simple doesn't exist!");
            }
            //else
            //    Logger.LogSettingNotFound(file, nick, "Ship", "pilot_mesh");
        }

        /// <param name="ship">Nickname of the [Ship] the collision group belongs to, or null if there's none.</param>
        private static void CheckCollGroup(string file, FLDataFile.Section collGrp, string ship)
        {
            if (ship == null)
                Logger.ELog(file + ":" + Util.GetSectionIdentifier(collGrp) + ": [CollisionGroup] doesn't belong to a [Ship]!");

            // explosion_arch, type and the dmg_* settings are all optional
            var explosionArch = Util.TryGetSetting(collGrp, "explosion_arch");
            if (explosionArch != null && !FXChecks.ExplosionExists(explosionArch.Str(0)))
                Logger.LogInvalidValue(file, explosionArch, "Explosion doesn't exist!");

            var separationExplosion = Util.TryGetSetting(collGrp, "separation_explosion");
            if (separationExplosion != null && !FXChecks.ExplosionExists(separationExplosion.Str(0)))
                Logger.LogInvalidValue(file, separationExplosion, "Explosion doesn't exist!");

            foreach (var fuse in collGrp.GetSettings("fuse"))
            {
                Util.CheckNumberOfArgs(fuse, file, 3);
                if (!FXChecks.FuseExists(fuse.Str(0)))
                    Logger.LogInvalidValue(file, fuse, "Fuse doesn't exist!");
            }

            var type = Util.TryGetSetting(collGrp, "type");
            if (type != null && !collisionGroupTypes.Contains(type.Str(0).ToLower()))
                Logger.LogInvalidValue(file, type, "Invalid type!");

            var debrisType = Util.TryGetSetting(collGrp, "debris_type");
            if (debrisType != null && !FXChecks.DebrisExists(debrisType.Str(0)))
                Logger.LogInvalidValue(file, debrisType, "Debris type doesn't exist!");

            CheckDamageObject(file, collGrp, ship, "dmg_obj", "dmg_hp");
            CheckDamageObject(file, collGrp, ship, "group_dmg_obj", "group_dmg_hp");
        }

        /// <summary>
        /// Checks a damage object (a [Simple]) and the hardpoint on the ship it gets attached to.
        /// </summary>
        private static void CheckDamageObject(string file, FLDataFile.Section collGrp, string ship, string objName, string hpName)
        {
            var dmgObj = Util.TryGetSetting(collGrp, objName);
            var dmgHp = Util.TryGetSetting(collGrp, hpName);

            if (dmgObj == null)
            {
                if (dmgHp != null)
                    Logger.LogInvalidValue(file, dmgHp, hpName + " exists but " + objName + " doesn't!", "");
                return;
            }

            if (!simpleNicknames.Contains(dmgObj.Str(0).ToLower()))
                Logger.LogInvalidValue(file, dmgObj, "Simple doesn't exist!");

            if (dmgHp == null)
                Logger.LogSettingNotFound(file, Util.GetSectionIdentifier(collGrp), "CollisionGroup", hpName);
            else if (ship != null && shipUTF.ContainsKey(ship) && !HardpointExists(ship, dmgHp.Str(0)))
                Logger.LogHardpoint(file, dmgHp);
        }

        private static void CheckSimpe(string file, FLDataFile.Section simple)
        {
            if (simple.SettingExists("material_library"))
            {
                if (!Checker.FileExists(simple.GetSetting("material_library").Str(0)))
                    Logger.LogFileNotFound(file, simple.GetSetting("material_library"));
            }
            else
                Logger.LogSettingNotFound(file, Util.GetSectionIdentifier(simple), "Simple", "material_library");
        }

        private static void CheckCockpitFile(string file)
        {
            FLDataFile ini = new FLDataFile(Path.Combine(Checker.flDataPath, file), true);
            FLDataFile.Section CockpitSection = ini.sections.FirstOrDefault(s => s.sectionName.ToLower() == "cockpit");
            FLDataFile.Section TurretCameraSection = ini.sections.FirstOrDefault(s => s.sectionName.ToLower() == "turretcamera");

            if (CockpitSection == null)
            {
                Logger.ELog(file + ": [Cockpit] section not found!");
                return;
            }

            if(CockpitSection.SettingExists("mesh"))
            {
                var mesh = CockpitSection.GetSetting("mesh");
                if (!Checker.FileExists(mesh.Str(0)))
                    Logger.LogFileNotFound(file, mesh);
            }
            else
                Logger.LogSettingNotFound(file, "", "Cockpit", "mesh");

            Util.CheckNumberOfArgs(CockpitSection, file, "head_turn", 2);
            if (TurretCameraSection != null)
                Util.CheckNumberOfArgs(TurretCameraSection, file, "tether", 3);
        }

        public static void ParseShipFolder()
        {
            Logger.ILog("Parsing Ship-Folder");
            ShipChecks.shipFiles = Checker.flIni.GetSettings("Data", "ships").Select(s => s.Str(0)).ToArray();

            List<string> shipNicknames = new List<string>();
            // Case-insensitive, because nicknames are referenced with any casing
            shipUTF = new Dictionary<string, UtfFile>(StringComparer.OrdinalIgnoreCase);

            List<string> simpleNicknames = new List<string>();
            simpleUTF = new Dictionary<string, UtfFile>(StringComparer.OrdinalIgnoreCase);

            foreach (var shipFile in ShipChecks.shipFiles)
            {
                // Missing files are reported by Checker.Parse
                FLDataFile ini = Checker.LoadIni(shipFile);
                if (ini == null)
                    continue;

                foreach (var section in ini.sections)
                {
                    Util.NickUtfTuple retn;
                    switch (section.sectionName)
                    {
                        case "Ship":
                            retn = Util.ParseNickUTF(Checker.flDataPath, shipFile, section);
                            if (retn.Nick == null)
                                break;
                            if (shipNicknames.Contains(retn.Nick, StringComparer.OrdinalIgnoreCase))
                            {
                                Logger.LogDublicateValue(shipFile, section.GetSetting("nickname"), "Duplicate ship nickname!");
                                break;
                            }
                            shipNicknames.Add(retn.Nick);
                            if (retn.UtfFile != null)
                                shipUTF.Add(retn.Nick, retn.UtfFile);
                            else
                            {
                                string shipPath = Util.TryGetStrSetting(section, "DA_archetype");

                                if (shipPath != null && Checker.HasVanillaFile(shipPath))
                                    shipUTF.Add(retn.Nick, null);
                            }
                            break;
                        case "Simple":
                            retn = Util.ParseNickUTF(Checker.flDataPath, shipFile, section);
                            if (retn.Nick == null)
                                break;
                            simpleNicknames.Add(retn.Nick);
                            if (retn.UtfFile != null)
                                Util.AddUnique(simpleUTF, retn.Nick, retn.UtfFile, shipFile, section, "simple");
                            break;
                    }
                }
            }

            ShipChecks.shipNicknames = shipNicknames.Select(n => n.ToLower()).ToArray();
            ShipChecks.simpleNicknames = simpleNicknames.Select(n => n.ToLower()).ToArray();

            Logger.ILog(string.Format("Finished Ship-Folder, found {0} ships ({1} models loaded) and {2} simples ({3} models loaded)",
                ShipChecks.shipNicknames.Length, shipUTF.Values.Count(u => u != null), ShipChecks.simpleNicknames.Length, simpleUTF.Count));
        }

        public static bool ShipExists(string nickname)
        {
            nickname = nickname.ToLower();
            return shipNicknames.Contains(nickname);
        }

        private static bool HardpointExists(string ship, string hp)
        {
            if (Checker.DisableUTF || (shipUTF[ship] == null && Checker.AssumeVanillaFilesExist))
                return true;
            return shipUTF[ship].HardpointExists(hp);
        }
    }
}
