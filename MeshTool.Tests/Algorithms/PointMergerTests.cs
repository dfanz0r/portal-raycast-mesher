using MeshTool.Core.Algorithms;
using MeshTool.Core.Data;
using Xunit;

namespace MeshTool.Tests.Algorithms
{
    public class PointMergerTests
    {
        [Fact]
        public void MergePoints_EmptyMaster_EmptyCandidates_ReturnsZero()
        {
            var master = new List<Vertex>();
            var candidates = new List<Vertex>();

            int result = PointMerger.MergePoints(master, candidates, 0.001);

            Assert.Equal(0, result);
            Assert.Empty(master);
        }

        [Fact]
        public void MergePoints_EmptyMaster_AllCandidatesAdded()
        {
            var master = new List<Vertex>();
            var candidates = new List<Vertex>
            {
                new Vertex { Position = new Vector3(0, 0, 0) },
                new Vertex { Position = new Vector3(1, 0, 0) },
                new Vertex { Position = new Vector3(2, 0, 0) }
            };

            int result = PointMerger.MergePoints(master, candidates, 0.001);

            Assert.Equal(3, result);
            Assert.Equal(3, master.Count);
        }

        [Fact]
        public void MergePoints_AllDuplicates_NoPointsAdded()
        {
            var master = new List<Vertex>
            {
                new Vertex { Position = new Vector3(0, 0, 0) },
                new Vertex { Position = new Vector3(1, 0, 0) }
            };
            var candidates = new List<Vertex>
            {
                new Vertex { Position = new Vector3(0.00001, 0, 0) }, // Very close to first
                new Vertex { Position = new Vector3(1.00001, 0, 0) }  // Very close to second
            };

            int result = PointMerger.MergePoints(master, candidates, 0.01);

            Assert.Equal(0, result);
            Assert.Equal(2, master.Count);
        }

        [Fact]
        public void MergePoints_PartialDuplicates_OnlyNewPointsAdded()
        {
            var master = new List<Vertex>
            {
                new Vertex { Position = new Vector3(0, 0, 0) }
            };
            var candidates = new List<Vertex>
            {
                new Vertex { Position = new Vector3(0.00001, 0, 0) }, // Duplicate
                new Vertex { Position = new Vector3(5, 0, 0) }        // New
            };

            int result = PointMerger.MergePoints(master, candidates, 0.01);

            Assert.Equal(1, result);
            Assert.Equal(2, master.Count);
        }

        [Fact]
        public void MergePoints_DuplicateUpdatesSpawnTime()
        {
            var existing = new Vertex { Position = new Vector3(0, 0, 0), SpawnTime = 1.0f };
            var master = new List<Vertex> { existing };
            var candidates = new List<Vertex>
            {
                new Vertex { Position = new Vector3(0.00001, 0, 0), SpawnTime = 5.0f }
            };

            PointMerger.MergePoints(master, candidates, 0.01);

            Assert.Equal(5.0f, existing.SpawnTime);
        }

        [Fact]
        public void MergePoints_OlderCandidate_DoesNotUpdateSpawnTime()
        {
            var existing = new Vertex { Position = new Vector3(0, 0, 0), SpawnTime = 5.0f };
            var master = new List<Vertex> { existing };
            var candidates = new List<Vertex>
            {
                new Vertex { Position = new Vector3(0.00001, 0, 0), SpawnTime = 1.0f }
            };

            PointMerger.MergePoints(master, candidates, 0.01);

            Assert.Equal(5.0f, existing.SpawnTime);
        }

        [Fact]
        public void MergePoints_ZeroMinDistance_Throws()
        {
            var master = new List<Vertex>();
            var candidates = new List<Vertex>();

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                PointMerger.MergePoints(master, candidates, 0));
        }

        [Fact]
        public void MergePoints_NegativeMinDistance_Throws()
        {
            var master = new List<Vertex>();
            var candidates = new List<Vertex>();

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                PointMerger.MergePoints(master, candidates, -0.1));
        }

        [Fact]
        public void MergePoints_NullMaster_Throws()
        {
            var candidates = new List<Vertex>();

            Assert.Throws<ArgumentNullException>(() =>
                PointMerger.MergePoints(null!, candidates, 0.001));
        }

        [Fact]
        public void MergePoints_NullCandidates_Throws()
        {
            var master = new List<Vertex>();

            Assert.Throws<ArgumentNullException>(() =>
                PointMerger.MergePoints(master, null!, 0.001));
        }

        [Fact]
        public void MergePoints_LargeDataset_PerformsCorrectly()
        {
            var master = new List<Vertex>();
            var candidates = new List<Vertex>();

            // Create a grid of points
            for (int x = 0; x < 10; x++)
            {
                for (int z = 0; z < 10; z++)
                {
                    candidates.Add(new Vertex { Position = new Vector3(x, 0, z) });
                }
            }

            int result = PointMerger.MergePoints(master, candidates, 0.1);

            Assert.Equal(100, result);
            Assert.Equal(100, master.Count);
        }

        [Fact]
        public void MergePointsWithSpacing_UsesCorrectMinDistance()
        {
            var master = new List<Vertex>();
            var candidates = new List<Vertex>
            {
                new Vertex { Position = new Vector3(0, 0, 0) },
                new Vertex { Position = new Vector3(0.4, 0, 0) }, // Should be duplicate with 0.5 * 1.0 = 0.5 min distance
                new Vertex { Position = new Vector3(1.0, 0, 0) }  // Should be added
            };

            int result = PointMerger.MergePointsWithSpacing(master, candidates, averageSpacing: 1.0, spacingMultiplier: 0.5);

            Assert.Equal(2, result);
        }
    }
}
