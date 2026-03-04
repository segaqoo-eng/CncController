// [2026-03-04] 新增 GCodeParser：G-Code 刀具號解析器（供 ATC PROGRAM TOOLS 使用）
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CncController.Helpers
{
    public static class GCodeParser
    {
        // [2026-03-04] 從 G-Code 文字中擷取所有使用的刀具號（T1, T5, T10 等）
        // 跳過註解行（; 或 ( 開頭或 % 行），過濾 T0（卸刀），去重後排序
        public static List<int> ExtractToolNumbers(string gcode)
        {
            var tools = new HashSet<int>();
            if (string.IsNullOrWhiteSpace(gcode))
                return new List<int>();

            var regex = new Regex(@"\bT(\d+)\b", RegexOptions.IgnoreCase);

            foreach (var rawLine in gcode.Split('\n'))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line)) continue;
                // 跳過純註解行
                if (line.StartsWith(';') || line.StartsWith('(') || line.StartsWith('%'))
                    continue;
                // 截取行內註解前的有效部分
                int semiIdx = line.IndexOf(';');
                if (semiIdx >= 0) line = line.Substring(0, semiIdx);
                int parenIdx = line.IndexOf('(');
                if (parenIdx >= 0) line = line.Substring(0, parenIdx);

                foreach (Match m in regex.Matches(line))
                {
                    if (int.TryParse(m.Groups[1].Value, out int toolNum) && toolNum > 0)
                        tools.Add(toolNum);
                }
            }

            return tools.OrderBy(t => t).ToList();
        }
    }
}
