using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MeshTool.Core.Data;

namespace MeshTool.Core.IO
{
    public static class ObjExporter
    {
        public static void ExportObj(List<Vertex> vertices, List<Triangle> triangles, string path)
        {
            var exportVertices = new List<Vertex>(vertices.Count);
            var indexByKey = new Dictionary<(long, long, long, long, long, long), int>(vertices.Count);

            static long Q(double v) => (long)Math.Round(v * 10000.0, MidpointRounding.AwayFromZero);

            (long, long, long, long, long, long) Key(Vertex v) =>
                (Q(v.Position.X), Q(v.Position.Y), Q(v.Position.Z), Q(v.Normal.X), Q(v.Normal.Y), Q(v.Normal.Z));

            int GetOrAddIndex(Vertex v)
            {
                var key = Key(v);
                if (indexByKey.TryGetValue(key, out int idx))
                {
                    return idx;
                }

                exportVertices.Add(v);
                idx = exportVertices.Count; // OBJ indices are 1-based
                indexByKey[key] = idx;
                return idx;
            }

            var faces = new List<(int A, int B, int C)>(triangles.Count);
            for (int i = 0; i < triangles.Count; i++)
            {
                var t = triangles[i];
                int a = GetOrAddIndex(t.A);
                int b = GetOrAddIndex(t.B);
                int c = GetOrAddIndex(t.C);
                faces.Add((a, b, c));
            }

            using (StreamWriter sw = new StreamWriter(path))
            {
                sw.WriteLine($"# Vertices: {exportVertices.Count}");
                foreach (var v in exportVertices)
                {
                    sw.WriteLine(string.Format(CultureInfo.InvariantCulture, "v {0:F4} {1:F4} {2:F4}", v.Position.X, v.Position.Y, v.Position.Z));
                    sw.WriteLine(string.Format(CultureInfo.InvariantCulture, "vn {0:F4} {1:F4} {2:F4}", v.Normal.X, v.Normal.Y, v.Normal.Z));
                }
                foreach (var f in faces)
                {
                    sw.WriteLine($"f {f.A}//{f.A} {f.B}//{f.B} {f.C}//{f.C}");
                }
            }
            Console.WriteLine($"[EXPORT] Saved {Path.GetFullPath(path)}");
        }
    }
}
