using System.Collections.Generic;
using System.IO;
using TerrainTool.Data;

namespace TerrainTool.IO
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

            byte[] fileData = File.ReadAllBytes(path);
            int offset = 0;

            int version = System.BitConverter.ToInt32(fileData, offset);
            offset += 4;
            if (version != 1 && version != 2) throw new IOException("Unknown database version");

            // Points
            int pointCount = System.BitConverter.ToInt32(fileData, offset);
            offset += 4;
            points = new List<Vertex>(pointCount);
            for (int i = 0; i < pointCount; i++)
            {
                double px = System.BitConverter.ToDouble(fileData, offset); offset += 8;
                double py = System.BitConverter.ToDouble(fileData, offset); offset += 8;
                double pz = System.BitConverter.ToDouble(fileData, offset); offset += 8;
                double nx = System.BitConverter.ToDouble(fileData, offset); offset += 8;
                double ny = System.BitConverter.ToDouble(fileData, offset); offset += 8;
                double nz = System.BitConverter.ToDouble(fileData, offset); offset += 8;
                float spawnTime = 0f;
                if (version >= 2)
                {
                    spawnTime = System.BitConverter.ToSingle(fileData, offset); offset += 4;
                }

                points.Add(new Vertex
                {
                    Position = new Vector3(px, py, pz),
                    Normal = new Vector3(nx, ny, nz),
                    SpawnTime = spawnTime
                });
            }

            // Rays
            int rayCount = System.BitConverter.ToInt32(fileData, offset);
            offset += 4;
            rays = new List<Ray>(rayCount);
            for (int i = 0; i < rayCount; i++)
            {
                double sx = System.BitConverter.ToDouble(fileData, offset); offset += 8;
                double sy = System.BitConverter.ToDouble(fileData, offset); offset += 8;
                double sz = System.BitConverter.ToDouble(fileData, offset); offset += 8;
                double ex = System.BitConverter.ToDouble(fileData, offset); offset += 8;
                double ey = System.BitConverter.ToDouble(fileData, offset); offset += 8;
                double ez = System.BitConverter.ToDouble(fileData, offset); offset += 8;
                float spawnTime = 0f;
                if (version >= 2)
                {
                    spawnTime = System.BitConverter.ToSingle(fileData, offset); offset += 4;
                }

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
