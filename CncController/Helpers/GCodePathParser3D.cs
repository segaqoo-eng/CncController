using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace CncController.Helpers
{
    // [2026-03-12] G-Code 3D 刀具路徑解析器：解析 G0/G1/G2/G3 生成 3D 線段
    public static class GCodePathParser3D
    {
        public class PathSegment
        {
            public Point3D Start { get; set; }
            public Point3D End { get; set; }
            public bool IsRapid { get; set; } // G0 = rapid (黃), G1/G2/G3 = cutting (綠)
        }

        /// <summary>
        /// 解析 G-Code 文字，回傳 3D 線段集合
        /// </summary>
        public static List<PathSegment> Parse(string gcode)
        {
            var segments = new List<PathSegment>();
            if (string.IsNullOrEmpty(gcode)) return segments;

            double x = 0, y = 0, z = 0;
            bool isRapid = true; // G0 = 快速移動
            bool isAbsolute = true; // G90 = 絕對座標

            var lines = gcode.Split('\n');
            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim().ToUpper();
                if (string.IsNullOrEmpty(line) || line.StartsWith("(") || line.StartsWith("%") || line.StartsWith(";"))
                    continue;

                // 去除行內註解
                int commentIdx = line.IndexOf('(');
                if (commentIdx >= 0) line = line.Substring(0, commentIdx);
                commentIdx = line.IndexOf(';');
                if (commentIdx >= 0) line = line.Substring(0, commentIdx);

                // 判斷 G 碼模式
                if (line.Contains("G90")) isAbsolute = true;
                if (line.Contains("G91")) isAbsolute = false;

                // 判斷運動模式
                bool hasMotion = false;
                if (Regex.IsMatch(line, @"G0[^0-9]|G0$|G00[^0-9]|G00$"))
                { isRapid = true; hasMotion = true; }
                if (Regex.IsMatch(line, @"G1[^0-9]|G1$|G01[^0-9]|G01$"))
                { isRapid = false; hasMotion = true; }
                if (Regex.IsMatch(line, @"G2[^0-9]|G2$|G02[^0-9]|G02$"))
                { isRapid = false; hasMotion = true; }
                if (Regex.IsMatch(line, @"G3[^0-9]|G3$|G03[^0-9]|G03$"))
                { isRapid = false; hasMotion = true; }

                // 解析座標
                double? nx = ParseWord(line, 'X');
                double? ny = ParseWord(line, 'Y');
                double? nz = ParseWord(line, 'Z');

                if (nx == null && ny == null && nz == null) continue;

                var start = new Point3D(x, y, z);

                if (isAbsolute)
                {
                    if (nx.HasValue) x = nx.Value;
                    if (ny.HasValue) y = ny.Value;
                    if (nz.HasValue) z = nz.Value;
                }
                else
                {
                    if (nx.HasValue) x += nx.Value;
                    if (ny.HasValue) y += ny.Value;
                    if (nz.HasValue) z += nz.Value;
                }

                var end = new Point3D(x, y, z);
                segments.Add(new PathSegment { Start = start, End = end, IsRapid = isRapid });
            }

            return segments;
        }

        private static double? ParseWord(string line, char word)
        {
            var match = Regex.Match(line, $@"{word}([+-]?\d*\.?\d+)");
            if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
                return val;
            return null;
        }
    }
}
