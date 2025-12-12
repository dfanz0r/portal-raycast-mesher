using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using TerrainTool.Data;

namespace TerrainTool.IO
{
    public static class XyzExporter
    {
        public static void ExportXyz(List<Vertex> points, string filePath)
        {
            // Use InvariantCulture to ensure '.' is used as decimal separator
            var culture = CultureInfo.InvariantCulture;

            using (var writer = new StreamWriter(filePath, false, Encoding.UTF8))
            {
                foreach (var point in points)
                {
                    // Format: X Y Z
                    // Using F6 for precision
                    writer.WriteLine(string.Format(culture, "{0:F6} {1:F6} {2:F6}",
                        point.Position.X,
                        point.Position.Y,
                        point.Position.Z));
                }
            }
        }
    }
}
