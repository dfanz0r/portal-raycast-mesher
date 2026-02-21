using System.Collections.Generic;
using System.IO;
using MeshTool.Core.Data;

namespace MeshTool.Core.IO
{
    public static class DatabaseIO
    {
        public static void SaveDatabase(IReadOnlyList<Vertex> points, IReadOnlyList<Ray> rays, string path)
        {
            using (BinaryWriter bw = new BinaryWriter(File.Open(path, FileMode.Create)))
            {
                // Version 2
                bw.Write(2);

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
                    bw.Write(p.SpawnTime);
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
                    bw.Write(r.SpawnTime);
                }
            }
        }

        public static void LoadDatabase(string path, out List<Vertex> points, out List<Ray> rays)
        {
            points = new List<Vertex>();
            rays = new List<Ray>();

            if (!File.Exists(path)) return;

            using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var br = new BinaryReader(fs);

            int version = br.ReadInt32();
            if (version != 1 && version != 2) throw new IOException("Unknown database version");

            // Points
            int pointCount = br.ReadInt32();
            points = new List<Vertex>(pointCount);
            for (int i = 0; i < pointCount; i++)
            {
                double px = br.ReadDouble();
                double py = br.ReadDouble();
                double pz = br.ReadDouble();
                double nx = br.ReadDouble();
                double ny = br.ReadDouble();
                double nz = br.ReadDouble();
                float spawnTime = version >= 2 ? br.ReadSingle() : 0f;

                points.Add(new Vertex
                {
                    Position = new Vector3(px, py, pz),
                    Normal = new Vector3(nx, ny, nz),
                    SpawnTime = spawnTime
                });
            }

            // Rays
            int rayCount = br.ReadInt32();
            rays = new List<Ray>(rayCount);
            for (int i = 0; i < rayCount; i++)
            {
                double sx = br.ReadDouble();
                double sy = br.ReadDouble();
                double sz = br.ReadDouble();
                double ex = br.ReadDouble();
                double ey = br.ReadDouble();
                double ez = br.ReadDouble();
                float spawnTime = version >= 2 ? br.ReadSingle() : 0f;

                rays.Add(new Ray
                {
                    Start = new Vector3(sx, sy, sz),
                    End = new Vector3(ex, ey, ez),
                    SpawnTime = spawnTime
                });
            }
        }

    }
}
