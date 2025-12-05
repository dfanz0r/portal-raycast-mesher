using System;
using System.Collections.Generic;
using System.Numerics;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using TerrainTool.Data;

namespace TerrainTool.IO
{
    using VPOSNRM = VertexBuilder<VertexPositionNormal, VertexEmpty, VertexEmpty>;

    public static class GlbExporter
    {
        public static void ExportGlb(List<Triangle> triangles, string path)
        {
            Console.WriteLine($"[EXPORT] Generating GLB for {triangles.Count} triangles...");

            // 1. Create Material
            var material = new MaterialBuilder("DefaultMaterial")
                .WithChannelParam(KnownChannel.BaseColor, KnownProperty.RGBA, new Vector4(0.45f, 0.35f, 0.25f, 1.0f))
                .WithDoubleSide(false);

            // 2. Create Mesh
            var mesh = VPOSNRM.CreateCompatibleMesh("TerrainMesh");
            var prim = mesh.UsePrimitive(material);

            // 3. Add Triangles
            foreach (var t in triangles)
            {
                var v1 = ToGltfVertex(t.A);
                var v2 = ToGltfVertex(t.B);
                var v3 = ToGltfVertex(t.C);

                prim.AddTriangle(v1, v2, v3);
            }

            // 4. Create Scene
            var scene = new SceneBuilder();
            scene.AddRigidMesh(mesh, Matrix4x4.Identity);

            // 5. Save
            scene.ToGltf2().SaveGLB(path);
            Console.WriteLine($"[EXPORT] Saved GLB to {path}");
        }

        private static VertexPositionNormal ToGltfVertex(Vertex v)
        {
            return new VertexPositionNormal(
                (float)v.Position.X, (float)v.Position.Y, (float)v.Position.Z,
                (float)v.Normal.X, (float)v.Normal.Y, (float)v.Normal.Z
            );
        }
    }
}
