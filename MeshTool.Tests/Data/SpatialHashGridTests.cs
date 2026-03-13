using MeshTool.Core.Data;
using Xunit;

namespace MeshTool.Tests.Data
{
    public class SpatialHashGridTests
    {
        private class TestItem
        {
            public Vector3 Position { get; set; }
            public string Name { get; set; } = "";
        }

        [Fact]
        public void Add_IncreasesCount()
        {
            var grid = new SpatialHashGrid<TestItem>(1.0, x => x.Position);

            grid.Add(new TestItem { Position = new Vector3(0, 0, 0) });

            Assert.Equal(1, grid.Count);
        }

        [Fact]
        public void Add_MultipleItems_IncreasesCount()
        {
            var grid = new SpatialHashGrid<TestItem>(1.0, x => x.Position);

            grid.Add(new TestItem { Position = new Vector3(0, 0, 0) });
            grid.Add(new TestItem { Position = new Vector3(1, 0, 0) });
            grid.Add(new TestItem { Position = new Vector3(2, 0, 0) });

            Assert.Equal(3, grid.Count);
        }

        [Fact]
        public void Remove_ExistingItem_DecreasesCount()
        {
            var grid = new SpatialHashGrid<TestItem>(1.0, x => x.Position);
            var item = new TestItem { Position = new Vector3(0, 0, 0) };

            grid.Add(item);
            bool result = grid.Remove(item);

            Assert.True(result);
            Assert.Equal(0, grid.Count);
        }

        [Fact]
        public void Remove_NonExistingItem_ReturnsFalse()
        {
            var grid = new SpatialHashGrid<TestItem>(1.0, x => x.Position);
            var item = new TestItem { Position = new Vector3(0, 0, 0) };

            bool result = grid.Remove(item);

            Assert.False(result);
        }

        [Fact]
        public void QueryNeighborhood_EmptyGrid_ReturnsEmpty()
        {
            var grid = new SpatialHashGrid<TestItem>(1.0, x => x.Position);

            var results = grid.QueryNeighborhood(new Vector3(0, 0, 0)).ToList();

            Assert.Empty(results);
        }

        [Fact]
        public void QueryNeighborhood_WithItems_ReturnsNearbyItems()
        {
            var grid = new SpatialHashGrid<TestItem>(1.0, x => x.Position);
            grid.Add(new TestItem { Position = new Vector3(0, 0, 0), Name = "A" });
            grid.Add(new TestItem { Position = new Vector3(0.5, 0, 0), Name = "B" });
            grid.Add(new TestItem { Position = new Vector3(10, 0, 0), Name = "C" }); // Far away

            var results = grid.QueryNeighborhood(new Vector3(0, 0, 0)).ToList();

            Assert.Equal(2, results.Count);
            Assert.Contains(results, x => x.Name == "A");
            Assert.Contains(results, x => x.Name == "B");
        }

        [Fact]
        public void IsTooClose_WithNearbyItem_ReturnsTrue()
        {
            var grid = new SpatialHashGrid<TestItem>(1.0, x => x.Position, minDistance: 0.5);
            grid.Add(new TestItem { Position = new Vector3(0, 0, 0) });

            bool result = grid.IsTooClose(new Vector3(0.3, 0, 0), out var closest);

            Assert.True(result);
            Assert.NotNull(closest);
        }

        [Fact]
        public void IsTooClose_WithNoNearbyItems_ReturnsFalse()
        {
            var grid = new SpatialHashGrid<TestItem>(1.0, x => x.Position, minDistance: 0.5);
            grid.Add(new TestItem { Position = new Vector3(0, 0, 0) });

            bool result = grid.IsTooClose(new Vector3(10, 0, 0), out var closest);

            Assert.False(result);
            Assert.Null(closest);
        }

        [Fact]
        public void IsTooClose_WithZeroMinDistance_ReturnsFalse()
        {
            var grid = new SpatialHashGrid<TestItem>(1.0, x => x.Position, minDistance: 0);
            grid.Add(new TestItem { Position = new Vector3(0, 0, 0) });

            bool result = grid.IsTooClose(new Vector3(0.001, 0, 0), out var closest);

            Assert.False(result);
        }

        [Fact]
        public void IsTooClose_WithCustomMinDistance_UsesCustomDistance()
        {
            var grid = new SpatialHashGrid<TestItem>(1.0, x => x.Position, minDistance: 0.1);
            grid.Add(new TestItem { Position = new Vector3(0, 0, 0) });

            // Use a larger custom min distance
            bool result = grid.IsTooClose(new Vector3(0.5, 0, 0), out var closest, customMinDistanceSq: 1.0);

            Assert.True(result);
        }

        [Fact]
        public void FindNearest_WithItems_ReturnsClosest()
        {
            var grid = new SpatialHashGrid<TestItem>(1.0, x => x.Position);
            grid.Add(new TestItem { Position = new Vector3(0, 0, 0), Name = "A" });
            grid.Add(new TestItem { Position = new Vector3(1, 0, 0), Name = "B" });
            grid.Add(new TestItem { Position = new Vector3(2, 0, 0), Name = "C" });

            var nearest = grid.FindNearest(new Vector3(1.5, 0, 0));

            Assert.NotNull(nearest);
            Assert.Equal("B", nearest.Name);
        }

        [Fact]
        public void FindNearest_WithMaxDistance_ReturnsNullIfTooFar()
        {
            var grid = new SpatialHashGrid<TestItem>(1.0, x => x.Position);
            grid.Add(new TestItem { Position = new Vector3(0, 0, 0) });

            var nearest = grid.FindNearest(new Vector3(100, 0, 0), maxDistance: 10);

            Assert.Null(nearest);
        }

        [Fact]
        public void Clear_RemovesAllItems()
        {
            var grid = new SpatialHashGrid<TestItem>(1.0, x => x.Position);
            grid.Add(new TestItem { Position = new Vector3(0, 0, 0) });
            grid.Add(new TestItem { Position = new Vector3(1, 0, 0) });

            grid.Clear();

            Assert.Equal(0, grid.Count);
        }

        [Fact]
        public void CellCount_ReturnsNumberOfCells()
        {
            var grid = new SpatialHashGrid<TestItem>(1.0, x => x.Position);
            grid.Add(new TestItem { Position = new Vector3(0, 0, 0) });
            grid.Add(new TestItem { Position = new Vector3(5, 0, 0) }); // Different cell
            grid.Add(new TestItem { Position = new Vector3(10, 0, 0) }); // Different cell

            Assert.Equal(3, grid.CellCount);
        }

        [Fact]
        public void GetCell_ReturnsCorrectCell()
        {
            var grid = new SpatialHashGrid<TestItem>(2.0, x => x.Position);

            var cell = grid.GetCell(new Vector3(3.5, 4.5, 5.5));

            Assert.Equal(1, cell.X); // floor(3.5/2) = 1
            Assert.Equal(2, cell.Y); // floor(4.5/2) = 2
            Assert.Equal(2, cell.Z); // floor(5.5/2) = 2
        }

        [Fact]
        public void GetHash_ReturnsConsistentHash()
        {
            var grid = new SpatialHashGrid<TestItem>(1.0, x => x.Position);
            var pos = new Vector3(1.5, 2.5, 3.5);

            long hash1 = grid.GetHash(pos);
            long hash2 = grid.GetHash(pos);

            Assert.Equal(hash1, hash2);
        }

        [Fact]
        public void HashCell_ProducesDifferentHashesForDifferentCells()
        {
            var hash1 = SpatialHashGrid<TestItem>.HashCell(0, 0, 0);
            var hash2 = SpatialHashGrid<TestItem>.HashCell(1, 0, 0);
            var hash3 = SpatialHashGrid<TestItem>.HashCell(0, 1, 0);
            var hash4 = SpatialHashGrid<TestItem>.HashCell(0, 0, 1);

            Assert.NotEqual(hash1, hash2);
            Assert.NotEqual(hash1, hash3);
            Assert.NotEqual(hash1, hash4);
            Assert.NotEqual(hash2, hash3);
            Assert.NotEqual(hash2, hash4);
            Assert.NotEqual(hash3, hash4);
        }
    }
}