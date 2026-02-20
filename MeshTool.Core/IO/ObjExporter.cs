using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TerrainTool.Data;

namespace TerrainTool.IO
{
    public static class ObjExporter
    {
        public static void ExportObj(List<Vertex> vertices, List<Triangle> triangles, string path)
        {
            // Assign IDs
            for (int i = 0; i < vertices.Count; i++)
            {
                vertices[i].ID = i + 1;
            }

            // Verify the IDs got set correctly
            int zeroIdCount = 0;
            int zeroPosCount = 0;
            int zeroNormalCount = 0;

            foreach (var v in vertices)
            {
                if (Math.Abs(v.Position.X) < 1e-9 && Math.Abs(v.Position.Y) < 1e-9 && Math.Abs(v.Position.Z) < 1e-9) zeroPosCount++;
                if (Math.Abs(v.Normal.X) < 1e-9 && Math.Abs(v.Normal.Y) < 1e-9 && Math.Abs(v.Normal.Z) < 1e-9) zeroNormalCount++;
            }

            foreach (var t in triangles)
            {
                if (t.A.ID == 0) zeroIdCount++;
                if (t.B.ID == 0) zeroIdCount++;
                if (t.C.ID == 0) zeroIdCount++;
            }

            if (zeroIdCount > 0)
            {
                Console.WriteLine($"[EXPORT WARNING] {zeroIdCount} triangle vertices have ID=0!");
            }
            if (zeroPosCount > 0)
            {
                Console.WriteLine($"[EXPORT WARNING] {zeroPosCount} vertices have ZERO POSITION (0,0,0)!");
            }
            if (zeroNormalCount > 0)
            {
                Console.WriteLine($"[EXPORT WARNING] {zeroNormalCount} vertices have ZERO NORMAL (0,0,0)!");
            }

            using (StreamWriter sw = new StreamWriter(path))
            {
                sw.WriteLine($"# Vertices: {vertices.Count}");
                foreach (var v in vertices)
                {
                    sw.WriteLine(string.Format(CultureInfo.InvariantCulture, "v {0:F4} {1:F4} {2:F4}", v.Position.X, v.Position.Y, v.Position.Z));
                    sw.WriteLine(string.Format(CultureInfo.InvariantCulture, "vn {0:F4} {1:F4} {2:F4}", v.Normal.X, v.Normal.Y, v.Normal.Z));
                }
                foreach (var t in triangles)
                {
                    sw.WriteLine($"f {t.A.ID}//{t.A.ID} {t.B.ID}//{t.B.ID} {t.C.ID}//{t.C.ID}");
                }
            }
            Console.WriteLine($"[EXPORT] Saved {Path.GetFullPath(path)}");
        }
    }
}
