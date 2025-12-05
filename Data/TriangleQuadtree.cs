using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TerrainTool.Data
{
    public class TriangleQuadtree
    {
        private const int MAX_DEPTH = 8;
        private const int MAX_TRIANGLES = 50;

        public Bounds Bounds;
        public List<Triangle>? Triangles;
        public TriangleQuadtree[]? Children;

        public TriangleQuadtree(Bounds bounds)
        {
            Bounds = bounds;
        }

        public static TriangleQuadtree Build(List<Triangle> triangles, Bounds bounds)
        {
            // Start the recursive build
            // We use a helper to allow parallel execution
            return BuildNode(triangles, bounds, 0);
        }

        private static TriangleQuadtree BuildNode(List<Triangle> triangles, Bounds bounds, int depth)
        {
            var node = new TriangleQuadtree(bounds);

            // Leaf condition
            if (triangles.Count <= MAX_TRIANGLES || depth >= MAX_DEPTH)
            {
                node.Triangles = triangles;
                return node;
            }

            double midX = bounds.MidX;
            double midZ = bounds.MidZ;

            // Define child bounds (Quadtree is 2D on XZ, but Bounds is 3D. We preserve Y range)
            Bounds[] childBounds = new Bounds[4];
            childBounds[0] = new Bounds { MinX = bounds.MinX, MaxX = midX, MinZ = bounds.MinZ, MaxZ = midZ, MinY = bounds.MinY, MaxY = bounds.MaxY }; // BL
            childBounds[1] = new Bounds { MinX = midX, MaxX = bounds.MaxX, MinZ = bounds.MinZ, MaxZ = midZ, MinY = bounds.MinY, MaxY = bounds.MaxY }; // BR
            childBounds[2] = new Bounds { MinX = bounds.MinX, MaxX = midX, MinZ = midZ, MaxZ = bounds.MaxZ, MinY = bounds.MinY, MaxY = bounds.MaxY }; // TL
            childBounds[3] = new Bounds { MinX = midX, MaxX = bounds.MaxX, MinZ = midZ, MaxZ = bounds.MaxZ, MinY = bounds.MinY, MaxY = bounds.MaxY }; // TR

            List<Triangle>[] childTris = new List<Triangle>[4];
            for (int i = 0; i < 4; i++) childTris[i] = new List<Triangle>();

            // Partition triangles
            // This part is O(N) for the current node's triangles
            foreach (var t in triangles)
            {
                for (int i = 0; i < 4; i++)
                {
                    // Check intersection with child bounds (2D check is sufficient usually, but we use 3D bounds)
                    if (t.Bounds.Intersects(childBounds[i]))
                    {
                        childTris[i].Add(t);
                    }
                }
            }

            node.Children = new TriangleQuadtree[4];

            // Multithreaded recursion for top levels
            if (depth < 3)
            {
                Parallel.For(0, 4, i =>
                {
                    node.Children[i] = BuildNode(childTris[i], childBounds[i], depth + 1);
                });
            }
            else
            {
                for (int i = 0; i < 4; i++)
                {
                    node.Children[i] = BuildNode(childTris[i], childBounds[i], depth + 1);
                }
            }

            return node;
        }

        public static void Query(TriangleQuadtree root, Bounds queryBounds, HashSet<Triangle> results)
        {
            var stack = new Stack<TriangleQuadtree>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                var node = stack.Pop();

                // Fast AABB check
                if (!node.Bounds.Intersects(queryBounds)) continue;

                if (node.Children == null)
                {
                    if (node.Triangles != null)
                    {
                        foreach (var t in node.Triangles)
                        {
                            if (t.Bounds.Intersects(queryBounds))
                            {
                                results.Add(t);
                            }
                        }
                    }
                }
                else
                {
                    for (int i = 0; i < node.Children.Length; i++)
                    {
                        stack.Push(node.Children[i]);
                    }
                }
            }
        }
    }
}
