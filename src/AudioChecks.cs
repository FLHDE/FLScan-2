using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.IO;

using FLScanIE.Util_Functions;
using FLScanIE.Logging;

namespace FLScanIE
{
    public static class AudioChecks
    {
        private static string[] soundFiles;
        private static string[] soundNicknames;

        private static string[] voiceFiles;
        private static string[] voiceMsgs;
        private static string[] voiceNicks;

        public static void CheckAudioFolder()
        {
            Logger.ILog("Checking Audio-Folder");
            Util.RunChecks(CheckSoundFile, soundFiles);
            Util.RunChecks(CheckVoiceFile, voiceFiles);
            // TODO voice files, the nicknames hash has to exists as node in the utf-file
            Logger.ILog("Finished Audio-Folder");
        }

        private static void CheckSoundFile(string file)
        {
            FLDataFile ini = new FLDataFile(Path.Combine(Checker.flDataPath, file), true);

            foreach (var soundFile in ini.GetSettings("Sound", "file"))
            {
                // The path is relative to DATA. A leading separator would make it relative to the drive root.
                if (soundFile.NumValues() > 0 && !Checker.FileExists(soundFile.Str(0).TrimStart('\\', '/')))
                    Logger.LogFileNotFound(file, soundFile);
            }

            // The values the client warns about when it reads the sounds (SoundArch.cpp)
            foreach (var section in ini.sections.Where(sec => sec.sectionName.ToLower() == "sound"))
            {
                foreach (var setting in section.settings.Where(st => st.NumValues() > 0))
                {
                    string val = setting.Str(0).Trim();
                    float number;
                    switch (setting.settingName.ToLower())
                    {
                        case "nickname":
                        case "file":
                        case "streamer":
                        case "is_2d":
                        case "pitch_bendable":
                        case "crv_pitch":
                            break;
                        case "type":
                            if (!soundTypes.Contains(val.ToLower()))
                                Logger.LogInvalidValue(file, setting, "Has to be normal, interface, voice, music or ambience! The game warns about it.", LogLevel.warning);
                            break;
                        case "persistent":
                            if (val.ToLower() != "space" && val.ToLower() != "gameflow")
                                Logger.LogInvalidValue(file, setting, "Has to be space or gameflow! The game warns about it.", LogLevel.warning);
                            break;
                        case "attenuation":
                            if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && ((int)number > 0 || (int)number < -96))
                                Logger.LogInvalidValue(file, setting, "Has to be between -96 and 0! The game warns and clamps it.", LogLevel.warning);
                            break;
                        case "range": // min[, max], where max defaults to 30 * min. A negative min means no range.
                            float min, max;
                            if (!float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out min) || min < 0)
                                break;
                            max = min * 30;
                            if (setting.NumValues() > 1 && setting.Str(1).Trim() != "" && !float.TryParse(setting.Str(1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out max))
                                break;
                            if (!(min < max))
                                Logger.LogInvalidValue(file, setting, "The minimum has to be below the maximum! The game warns about an invalid range.", LogLevel.warning);
                            break;
                        case "lod1":
                        case "lod2":
                            if (!SoundExists(val))
                                Logger.LogInvalidValue(file, setting, "Sound doesn't exist!");
                            break;
                        default:
                            Logger.LogInvalidValue(file, setting, "Unknown [Sound] key! The game warns about it.", setting.settingName, LogLevel.warning);
                            break;
                    }
                }
            }
        }

        private static readonly string[] soundTypes = { "normal", "interface", "voice", "music", "ambience" };

        /// <summary>
        /// Checks the [Sound] and [VoiceSound] sections of a voice file the way the client reads them: the file is
        /// stored under the msg read so far, so it has to come after msg.
        /// </summary>
        private static void CheckVoiceFile(string file)
        {
            FLDataFile ini = Checker.LoadIni(file, false);
            if (ini == null)
                return;
            bool inVoice = false;
            foreach (var section in ini.sections)
            {
                string name = section.sectionName.ToLower();
                if (name == "voice")
                {
                    inVoice = true;
                    continue;
                }
                if (name != "sound" && name != "voicesound")
                    continue;
                if (!inVoice)
                {
                    Logger.WLog(string.Format("{0}:{1}: The [{2}] isn't in a [Voice]! The game warns and ignores it.", file, Util.GetSectionIdentifier(section), section.sectionName));
                    continue;
                }
                bool hasMsg = section.settings.Any(st => st.NumValues() > 0 && st.settingName.ToLower() == "msg");
                bool gotMsg = false;
                foreach (var setting in section.settings.Where(st => st.NumValues() > 0))
                {
                    switch (setting.settingName.ToLower())
                    {
                        case "msg":
                            gotMsg = true;
                            break;
                        case "file":
                            if (hasMsg && !gotMsg)
                                Logger.LogInvalidValue(file, setting, "Has to come after msg! The message doesn't get the sound otherwise.");
                            break;
                        case "priority":
                        case "attenuation":
                        case "duration":
                            break;
                        default:
                            Logger.LogInvalidValue(file, setting, "Unknown voice [" + section.sectionName + "] key! The game warns about it.", setting.settingName, LogLevel.warning);
                            break;
                    }
                }
                if (!hasMsg)
                    Logger.ELog(string.Format("{0}:{1}: The [{2}] has no msg! The game reports an error and ignores it.", file, Util.GetSectionIdentifier(section), section.sectionName));
            }
        }

        public static void ParseAudioFolder()
        {
            Logger.ILog("Parsing Audio-Folder");

            var voiceMsgs = new List<string>();
            var voiceNicks = new List<string>();

            string[][] sounds = Util.GetNicks("Data", "sounds", "Sound", "nickname");
            soundFiles = sounds[0];
            soundNicknames = sounds[1].Select(n => n.ToLower()).ToArray();

            voiceFiles = Checker.flIni.GetSettings("Data", "voices").Select(s => s.Str(0)).ToArray();

            foreach (var voiceFile in voiceFiles)
            {
                // Missing files are reported by Checker.Parse
                FLDataFile ini = Checker.LoadIni(voiceFile, false);
                if (ini == null)
                    continue;
                // The game also reads [VoiceSound] as a [Sound] of the [Voice] before it (freelancer.exe)
                var allSettings = ini.sections.SelectMany(s => s.settings).Where(s => s.sectionName.ToLower() == "sound" || s.sectionName.ToLower() == "voicesound" || s.sectionName.ToLower() == "voice").ToArray();
                voiceMsgs.AddRange(allSettings.Where(s => s.settingName == "msg").Select(s => s.Str(0).ToLower()).ToArray());
                voiceNicks.AddRange(allSettings.Where(s => s.settingName == "nickname").Select(s => s.Str(0).ToLower()).ToArray());
            }

            AudioChecks.voiceMsgs = voiceMsgs.ToArray();
            AudioChecks.voiceNicks = voiceNicks.ToArray();

            Logger.ILog(string.Format("Finished Audio-Folder, found {0} sounds and {1} voice messages", soundNicknames.Length, AudioChecks.voiceMsgs.Length));
        }

        public static bool SoundExists(string soundNick)
        {
            soundNick = soundNick.ToLower();
            return soundNicknames.Contains(soundNick);
        }

        public static bool VoiceMsgExists(string voiceMsg)
        {
            voiceMsg = voiceMsg.ToLower();
            return voiceMsgs.Contains(voiceMsg);
        }

        public static bool VoiceExists(string nick)
        {
            nick = nick.ToLower();
            return voiceNicks.Contains(nick);
        }
    }
}
