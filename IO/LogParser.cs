using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using TerrainTool.Data;

namespace TerrainTool.IO
{
    public static class LogParser
    {
        private static readonly Regex s_hitRegex = new Regex(
            @"HIT\|P:\s*([-+]?\d*\.?\d+),([-+]?\d*\.?\d+),([-+]?\d*\.?\d+)\|N:\s*([-+]?\d*\.?\d+),([-+]?\d*\.?\d+),([-+]?\d*\.?\d+)",
            RegexOptions.Compiled);

        private static readonly Regex s_missRegex = new Regex(
            @"MISS\|S:\s*([-+]?\d*\.?\d+),([-+]?\d*\.?\d+),([-+]?\d*\.?\d+)\|E:\s*([-+]?\d*\.?\d+),([-+]?\d*\.?\d+),([-+]?\d*\.?\d+)",
            RegexOptions.Compiled);

        public static void ParseLog(string path, out List<Vertex> points, out List<Ray> misses)
        {
            points = new List<Vertex>();
            misses = new List<Ray>();

            using (StreamReader sr = new StreamReader(path, Encoding.UTF8))
            {
                string? line;
                while ((line = sr.ReadLine()) != null)
                {
                    if (TryParseLine(line, out var hit, out var miss, out bool isMiss))
                    {
                        if (!isMiss && hit != null) points.Add(hit);
                        else if (isMiss) misses.Add(miss);
                    }
                }
            }
        }

        /// <summary>
        /// Parses a single log line into either a HIT vertex or a MISS ray.
        /// Returns true if the line matched either pattern.
        /// </summary>
        public static bool TryParseLine(string? line, out Vertex? hit, out Ray miss, out bool isMiss)
        {
            hit = null;
            miss = default;
            isMiss = false;

            if (string.IsNullOrWhiteSpace(line)) return false;

            var m = s_hitRegex.Match(line);
            if (m.Success)
            {
                hit = new Vertex
                {
                    Position = new Vector3(
                        double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                        double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                        double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture)
                    ),
                    Normal = new Vector3(
                        double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture),
                        double.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture),
                        double.Parse(m.Groups[6].Value, CultureInfo.InvariantCulture)
                    )
                };
                return true;
            }

            m = s_missRegex.Match(line);
            if (m.Success)
            {
                isMiss = true;
                miss = new Ray
                {
                    Start = new Vector3(
                        double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
                        double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
                        double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture)
                    ),
                    End = new Vector3(
                        double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture),
                        double.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture),
                        double.Parse(m.Groups[6].Value, CultureInfo.InvariantCulture)
                    )
                };
                return true;
            }

            return false;
        }
    }
}
