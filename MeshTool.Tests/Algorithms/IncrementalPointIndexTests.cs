using MeshTool.Core.Algorithms;
using MeshTool.Core.Data;
using Xunit;

namespace MeshTool.Tests.Algorithms
{
    public class IncrementalPointIndexTests
    {
        [Fact]
        public void Constructor_WithZeroMinDistance_Throws()
        {
            var points = new List<Vertex>();

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new IncrementalPointIndex(points, 0));
        }

        [Fact]
        public void Constructor_WithNegativeMinDistance_Throws()
        {
            var points = new List<Vertex>();

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new IncrementalPointIndex(points, -0.1));
        }

        [Fact]
        public void TryAdd_EmptyIndex_AddsPoint()
        {
            var index = new IncrementalPointIndex(new List<Vertex>(), 0.1);
            var master = new List<Vertex>();
            var point = new Vertex { Position = new Vector3(0, 0, 0) };

            bool result = index.TryAdd(master, point);

            Assert.True(result);
            Assert.Single(master);
        }

        [Fact]
        public void TryAdd_Duplicate_ReturnsFalse()
        {
            var existing = new Vertex { Position = new Vector3(0, 0, 0) };
            var index = new IncrementalPointIndex(new List<Vertex> { existing }, 0.1);
            var master = new List<Vertex> { existing };
            var duplicate = new Vertex { Position = new Vector3(0.01, 0, 0) };

            bool result = index.TryAdd(master, duplicate);

            Assert.False(result);
            Assert.Single(master);
        }

        [Fact]
        public void TryAdd_NewPoint_ReturnsTrue()
        {
            var existing = new Vertex { Position = new Vector3(0, 0, 0) };
            var index = new IncrementalPointIndex(new List<Vertex> { existing }, 0.1);
            var master = new List<Vertex> { existing };
            var newPoint = new Vertex { Position = new Vector3(5, 0, 0) };

            bool result = index.TryAdd(master, newPoint);

            Assert.True(result);
            Assert.Equal(2, master.Count);
        }

        [Fact]
        public void TryAdd_DuplicateWithSpawnTimeRefresh_UpdatesSpawnTime()
        {
            var existing = new Vertex { Position = new Vector3(0, 0, 0), SpawnTime = 1.0f };
            var index = new IncrementalPointIndex(new List<Vertex> { existing }, 0.1, refreshExistingSpawnTime: true);
            var master = new List<Vertex> { existing };
            var duplicate = new Vertex { Position = new Vector3(0.01, 0, 0), SpawnTime = 5.0f };

            index.TryAdd(master, duplicate);

            Assert.Equal(5.0f, existing.SpawnTime);
        }

        [Fact]
        public void TryAdd_DuplicateWithoutSpawnTimeRefresh_DoesNotUpdateSpawnTime()
        {
            var existing = new Vertex { Position = new Vector3(0, 0, 0), SpawnTime = 1.0f };
            var index = new IncrementalPointIndex(new List<Vertex> { existing }, 0.1, refreshExistingSpawnTime: false);
            var master = new List<Vertex> { existing };
            var duplicate = new Vertex { Position = new Vector3(0.01, 0, 0), SpawnTime = 5.0f };

            index.TryAdd(master, duplicate);

            Assert.Equal(1.0f, existing.SpawnTime);
        }

        [Fact]
        public void AddRange_MultiplePoints_ReturnsCorrectCount()
        {
            var index = new IncrementalPointIndex(new List<Vertex>(), 0.1);
            var master = new List<Vertex>();
            var candidates = new List<Vertex>
            {
                new Vertex { Position = new Vector3(0, 0, 0) },
                new Vertex { Position = new Vector3(1, 0, 0) },
                new Vertex { Position = new Vector3(2, 0, 0) }
            };

            int result = index.AddRange(master, candidates);

            Assert.Equal(3, result);
            Assert.Equal(3, master.Count);
        }

        [Fact]
        public void AddRange_WithDuplicates_ReturnsCorrectCount()
        {
            var existing = new Vertex { Position = new Vector3(0, 0, 0) };
            var index = new IncrementalPointIndex(new List<Vertex> { existing }, 0.1);
            var master = new List<Vertex> { existing };
            var candidates = new List<Vertex>
            {
                new Vertex { Position = new Vector3(0.01, 0, 0) }, // Duplicate
                new Vertex { Position = new Vector3(5, 0, 0) }     // New
            };

            int result = index.AddRange(master, candidates);

            Assert.Equal(1, result);
            Assert.Equal(2, master.Count);
        }

        [Fact]
        public void TryAdd_MultiplePointsInSameCell_DetectsDuplicates()
        {
            var index = new IncrementalPointIndex(new List<Vertex>(), 0.5);
            var master = new List<Vertex>();

            // Add points that would be in the same cell
            index.TryAdd(master, new Vertex { Position = new Vector3(0.1, 0, 0.1) });
            index.TryAdd(master, new Vertex { Position = new Vector3(0.2, 0, 0.2) }); // Should be duplicate
            index.TryAdd(master, new Vertex { Position = new Vector3(0.3, 0, 0.3) }); // Should be duplicate

            Assert.Single(master);
        }

        [Fact]
        public void TryAdd_PointsInNeighboringCells_DetectsDuplicates()
        {
            var index = new IncrementalPointIndex(new List<Vertex>(), 0.5);
            var master = new List<Vertex>();

            // Add a point near a cell boundary
            index.TryAdd(master, new Vertex { Position = new Vector3(0.9, 0, 0) });

            // Try to add a point in a neighboring cell but within min distance
            bool result = index.TryAdd(master, new Vertex { Position = new Vector3(1.1, 0, 0) });

            // This should be detected as duplicate due to 3x3x3 neighborhood check
            Assert.False(result);
            Assert.Single(master);
        }

        [Fact]
        public void Constructor_PrePopulatedIndex_DetectsDuplicates()
        {
            var existing = new List<Vertex>
            {
                new Vertex { Position = new Vector3(0, 0, 0) },
                new Vertex { Position = new Vector3(1, 0, 0) }
            };
            var index = new IncrementalPointIndex(existing, 0.1);
            var master = new List<Vertex>(existing);

            // Try to add a duplicate
            bool result = index.TryAdd(master, new Vertex { Position = new Vector3(0.01, 0, 0) });

            Assert.False(result);
            Assert.Equal(2, master.Count);
        }
    }
}