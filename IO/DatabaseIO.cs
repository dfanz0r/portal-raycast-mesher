using System.Collections.Generic;
using System.IO;
using TerrainTool.Data;

namespace TerrainTool.IO
{
    public static class DatabaseIO
    {
        public static void SaveDatabase(List<Vertex> points, List<Ray> rays, string path)
        {
            using (BinaryWriter bw = new BinaryWriter(File.Open(path, FileMode.Create)))
            {
                // Version 1
                bw.Write(1);

                // Points
                bw.Write(points.Count);
                foreach (var p in points)
                {
                    bw.Write(p.Position.X);
                    bw.Write(p.Position.Y);
                    bw.Write(p.Position.Z);
                    bw.Write(p.Normal.X);
                    bw.Write(p.Normal.Y);
                    bw.Write(p.Normal.Z);
                }

                // Rays
                bw.Write(rays.Count);
                foreach (var r in rays)
                {
                    bw.Write(r.Start.X);
                    bw.Write(r.Start.Y);
                    bw.Write(r.Start.Z);
                    bw.Write(r.End.X);
                    bw.Write(r.End.Y);
                    bw.Write(r.End.Z);
                }
            }
        }

        public static void LoadDatabase(string path, out List<Vertex> points, out List<Ray> rays)
        {
            points = new List<Vertex>();
            rays = new List<Ray>();

            if (!File.Exists(path)) return;

            using (BinaryReader br = new BinaryReader(File.Open(path, FileMode.Open)))
            {
                int version = br.ReadInt32();
                if (version != 1) throw new IOException("Unknown database version");

                // Points
                int pointCount = br.ReadInt32();
                points = new List<Vertex>(pointCount);
                for (int i = 0; i < pointCount; i++)
                {
                    points.Add(new Vertex
                    {
                        Position = new Vector3(br.ReadDouble(), br.ReadDouble(), br.ReadDouble()),
                        Normal = new Vector3(br.ReadDouble(), br.ReadDouble(), br.ReadDouble())
                    });
                }

                // Rays
                int rayCount = br.ReadInt32();
                rays = new List<Ray>(rayCount);
                for (int i = 0; i < rayCount; i++)
                {
                    rays.Add(new Ray
                    {
                        Start = new Vector3(br.ReadDouble(), br.ReadDouble(), br.ReadDouble()),
                        End = new Vector3(br.ReadDouble(), br.ReadDouble(), br.ReadDouble())
                    });
                }
            }
        }

    }
}
