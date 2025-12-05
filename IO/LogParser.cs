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
        public static void ParseLog(string path, out List<Vertex> points, out List<Ray> misses)
        {
            points = new List<Vertex>();
            misses = new List<Ray>();

            Regex rHit = new Regex(@"HIT\|P:\s*([-+]?\d*\.?\d+),([-+]?\d*\.?\d+),([-+]?\d*\.?\d+)\|N:\s*([-+]?\d*\.?\d+),([-+]?\d*\.?\d+),([-+]?\d*\.?\d+)", RegexOptions.Compiled);
            Regex rMiss = new Regex(@"MISS\|S:\s*([-+]?\d*\.?\d+),([-+]?\d*\.?\d+),([-+]?\d*\.?\d+)\|E:\s*([-+]?\d*\.?\d+),([-+]?\d*\.?\d+),([-+]?\d*\.?\d+)", RegexOptions.Compiled);

            using (StreamReader sr = new StreamReader(path, Encoding.UTF8))
            {
                string? line;
                while ((line = sr.ReadLine()) != null)
                {
                    Match m = rHit.Match(line);
                    if (m.Success)
                    {
                        points.Add(new Vertex
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
                        });
                        continue;
                    }

                    m = rMiss.Match(line);
                    if (m.Success)
                    {
                        misses.Add(new Ray
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
                        });
                    }
                }
            }
        }
    }
}
