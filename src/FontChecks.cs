using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using FLScanIE.Logging;
using FLScanIE.Util_Functions;

namespace FLScanIE
{
    public static class FontChecks
    {
        private static string[] fontFiles;
        private static string[] fontNicknames;
        private static string[] styleFiles;
        private static string[] styleNicknames;

        public static void CheckFontFolder()
        {
            Logger.ILog("Checking Font-Folder");
            Util.RunChecks(CheckStyleFile, styleFiles);
            // TODO: any checks for font-files? I don't think so.
            Logger.ILog("Finished Font-Folder");
        }

        private static void CheckStyleFile(string file)
        {
            FLDataFile ini = new FLDataFile(Path.Combine(Checker.flDataPath, file), true);
            // Font names are case-insensitive
            var fonts = new HashSet<string>(ini.GetSettings("TrueType", "font").Where(s => s.NumValues() > 0)
                .Select(s => s.Str(0)), StringComparer.OrdinalIgnoreCase);

            foreach (var setting in ini.GetSettings("Style", "font"))
            {
                if (setting.NumValues() > 0 && !fonts.Contains(setting.Str(0)))
                    Logger.LogInvalidValue(file, setting, "Font not defined in [TrueType]");
            }
        }


        public static void ParseFontFoler()
        {
            Logger.ILog("Parsing Font-Folder");
            List<string> fontNicknames = new List<string>();
            List<string> styleNicknames = new List<string>();

            FontChecks.fontFiles = Checker.flIni.GetSettings("data", "fonts").Select(s => s.Str(0)).ToArray();
            FontChecks.styleFiles = Checker.flIni.GetSettings("data", "rich_fonts").Select(s => s.Str(0)).ToArray();

            foreach (var fontFile in fontFiles)
            {
                ParseFontFile(fontFile, ref fontNicknames);
            }

            foreach (var styleFile in styleFiles)
            {
                ParseStyleFile(styleFile, ref styleNicknames);
            }

            FontChecks.fontNicknames = fontNicknames.ToArray();
            FontChecks.styleNicknames = styleNicknames.ToArray();
            Logger.ILog("Finished Font-Folder");
        }

        private static void ParseFontFile(string file, ref List<string> nicknames)
        {
            // Missing files are reported by Checker.Parse
            FLDataFile ini = Checker.LoadIni(file, false);
            if (ini == null)
                return;
            nicknames.AddRange(ini.GetSettings("TrueType", "nickname").Select(s => s.Str(0).ToLower()));
        }

        private static void ParseStyleFile(string file, ref List<string> nicknames)
        {
            // Missing files are reported by Checker.Parse
            FLDataFile ini = Checker.LoadIni(file, false);
            if (ini == null)
                return;
            nicknames.AddRange(ini.GetSettings("Style", "name").Select(s => s.Str(0).ToLower()));
        }

        public static bool FontExists(string nick)
        {
            nick = nick.ToLower();
            return fontNicknames.Contains(nick);
        }

        public static bool StyleExists(string nick)
        {
            nick = nick.ToLower();
            return styleNicknames.Contains(nick);
        }
    }
}
