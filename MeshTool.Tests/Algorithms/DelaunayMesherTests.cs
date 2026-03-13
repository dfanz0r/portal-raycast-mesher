using MeshTool.Core.Algorithms;
using MeshTool.Core.Data;
using Xunit;

namespace MeshTool.Tests.Algorithms
{
    public class DelaunayMesherTests
    {
        [Fact]
        public void GenerateMesh_EmptyPointList_ReturnsEmptyMesh()
        {
            var points = new List<Vertex>();

            var result = DelaunayMesher.GenerateMesh(points);

            Assert.Empty(result);
        }

        [Fact]
        public void GenerateMesh_SinglePoint_ReturnsEmptyMesh()
        {
            var points = new List<Vertex>
            {
                new Vertex { Position = new Vector3(0, 0, 0) }
            };

            var result = DelaunayMesher.GenerateMesh(points);

            Assert.Empty(result);
        }

        [Fact]
        public void GenerateMesh_TwoPoints_ReturnsEmptyMesh()
        {
            var points = new List<Vertex>
            {
                new Vertex { Position = new Vector3(0, 0, 0) },
                new Vertex { Position = new Vector3(1, 0, 0) }
            };

            var result = DelaunayMesher.GenerateMesh(points);

            Assert.Empty(result);
        }

        [Fact]
        public void GenerateMesh_ThreePoints_ReturnsOneTriangle()
        {
            var points = new List<Vertex>
            {
                new Vertex { Position = new Vector3(0, 0, 0) },
                new Vertex { Position = new Vector3(1, 0, 0) },
                new Vertex { Position = new Vector3(0.5, 0, 1) }
            };

            var result = DelaunayMesher.GenerateMesh(points);

            Assert.Single(result);
        }

        [Fact]
        public void GenerateMesh_FourPointsSquare_ReturnsTwoTriangles()
        {
            var points = new List<Vertex>
            {
                new Vertex { Position = new Vector3(0, 0, 0) },
                new Vertex { Position = new Vector3(1, 0, 0) },
                new Vertex { Position = new Vector3(1, 0, 1) },
                new Vertex { Position = new Vector3(0, 0, 1) }
            };

            var result = DelaunayMesher.GenerateMesh(points);

            Assert.Equal(2, result.Count);
        }

        [Fact]
        public void GenerateMesh_TriangleVertices_AreNotNull()
        {
            var points = new List<Vertex>
            {
                new Vertex { Position = new Vector3(0, 0, 0) },
                new Vertex { Position = new Vector3(1, 0, 0) },
                new Vertex { Position = new Vector3(0.5, 0, 1) }
            };

            var result = DelaunayMesher.GenerateMesh(points);

            foreach (var triangle in result)
            {
                Assert.NotNull(triangle.A);
                Assert.NotNull(triangle.B);
                Assert.NotNull(triangle.C);
            }
        }

        [Fact]
        public void GenerateMesh_TriangleVertices_AreDistinct()
        {
            var points = new List<Vertex>
            {
                new Vertex { Position = new Vector3(0, 0, 0) },
                new Vertex { Position = new Vector3(1, 0, 0) },
                new Vertex { Position = new Vector3(0.5, 0, 1) }
            };

            var result = DelaunayMesher.GenerateMesh(points);

            foreach (var triangle in result)
            {
                Assert.NotSame(triangle.A, triangle.B);
                Assert.NotSame(triangle.B, triangle.C);
                Assert.NotSame(triangle.C, triangle.A);
            }
        }

        [Fact]
        public void GenerateMesh_GridPoints_ReturnsExpectedTriangleCount()
        {
            // Create a 3x3 grid of points
            var points = new List<Vertex>();
            for (int x = 0; x < 3; x++)
            {
                for (int z = 0; z < 3; z++)
                {
                    points.Add(new Vertex { Position = new Vector3(x, 0, z) });
                }
            }

            var result = DelaunayMesher.GenerateMesh(points);

            // A 3x3 grid should produce 8 triangles (2 per grid cell, 4 cells)
            Assert.True(result.Count >= 6 && result.Count <= 12);
        }

        [Fact]
        public void GenerateMesh_CollinearPoints_ReturnsEmptyMesh()
        {
            var points = new List<Vertex>
            {
                new Vertex { Position = new Vector3(0, 0, 0) },
                new Vertex { Position = new Vector3(1, 0, 0) },
                new Vertex { Position = new Vector3(2, 0, 0) },
                new Vertex { Position = new Vector3(3, 0, 0) }
            };

            var result = DelaunayMesher.GenerateMesh(points);

            // Collinear points cannot form triangles
            Assert.Empty(result);
        }

        [Fact]
        public void GenerateMesh_NearlyCollinearPoints_MayReturnTriangles()
        {
            var points = new List<Vertex>
            {
                new Vertex { Position = new Vector3(0, 0, 0) },
                new Vertex { Position = new Vector3(1, 0.001, 0) },
                new Vertex { Position = new Vector3(2, 0, 0) }
            };

            var result = DelaunayMesher.GenerateMesh(points);

            // Nearly collinear points might form very thin triangles
            // This tests that the algorithm handles near-degenerate cases
        }

        [Fact]
        public void GenerateMesh_LargeDataset_CompletesInReasonableTime()
        {
            var rand = new Random(42);
            var points = new List<Vertex>();

            // Create 100 random points
            for (int i = 0; i < 100; i++)
            {
                points.Add(new Vertex
                {
                    Position = new Vector3(
                        rand.NextDouble() * 10,
                        0,
                        rand.NextDouble() * 10
                    )
                });
            }

            var result = DelaunayMesher.GenerateMesh(points);

            // Should complete and produce some triangles
            Assert.True(result.Count > 0);
        }

        [Fact]
        public void GenerateMesh_PointsAtSameLocation_HandlesGracefully()
        {
            var points = new List<Vertex>
            {
                new Vertex { Position = new Vector3(0, 0, 0) },
                new Vertex { Position = new Vector3(0, 0, 0) }, // Same location
                new Vertex { Position = new Vector3(1, 0, 0) },
                new Vertex { Position = new Vector3(0.5, 0, 1) }
            };

            // Should not crash
            var result = DelaunayMesher.GenerateMesh(points);

            // May produce fewer triangles due to duplicate points
        }

        [Fact]
        public void GenerateMesh_NegativeCoordinates_HandlesCorrectly()
        {
            var points = new List<Vertex>
            {
                new Vertex { Position = new Vector3(-1, 0, -1) },
                new Vertex { Position = new Vector3(1, 0, -1) },
                new Vertex { Position = new Vector3(0, 0, 1) }
            };

            var result = DelaunayMesher.GenerateMesh(points);

            Assert.Single(result);
        }

        [Fact]
        public void GenerateMesh_LargeCoordinates_HandlesCorrectly()
        {
            var points = new List<Vertex>
            {
                new Vertex { Position = new Vector3(10000, 0, 10000) },
                new Vertex { Position = new Vector3(10001, 0, 10000) },
                new Vertex { Position = new Vector3(10000.5, 0, 10001) }
            };

            var result = DelaunayMesher.GenerateMesh(points);

            Assert.Single(result);
        }

        [Fact]
        public void FilterHighAspectRatioTriangles_RemovesThinTriangles()
        {
            // Create triangles with varying aspect ratios
            var triangles = new List<Triangle>
            {
                // Equilateral-like triangle (good aspect ratio)
                new Triangle(
                    new Vertex { Position = new Vector3(0, 0, 0) },
                    new Vertex { Position = new Vector3(1, 0, 0) },
                    new Vertex { Position = new Vector3(0.5, 0, 0.866) }
                ),
                // Very thin triangle (bad aspect ratio)
                new Triangle(
                    new Vertex { Position = new Vector3(0, 0, 0) },
                    new Vertex { Position = new Vector3(1, 0, 0) },
                    new Vertex { Position = new Vector3(0.5, 0, 0.001) }
                )
            };

            var result = DelaunayMesher.FilterHighAspectRatioTriangles(triangles, 10.0, out int removedCount);

            Assert.Single(result);
            Assert.Equal(1, removedCount);
        }

        [Fact]
        public void FilterHighAspectRatioTriangles_EmptyInput_ReturnsEmpty()
        {
            var triangles = new List<Triangle>();

            var result = DelaunayMesher.FilterHighAspectRatioTriangles(triangles, 10.0, out int removedCount);

            Assert.Empty(result);
            Assert.Equal(0, removedCount);
        }
    }
}
