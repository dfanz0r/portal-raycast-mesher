using MeshTool.Core.Data;
using Xunit;

namespace MeshTool.Tests.Data
{
    public class BoundsTests
    {
        [Fact]
        public void Inverted_CreatesInvalidBounds()
        {
            var bounds = Bounds.Inverted();

            Assert.Equal(double.MaxValue, bounds.MinX);
            Assert.Equal(double.MinValue, bounds.MaxX);
            Assert.Equal(double.MaxValue, bounds.MinY);
            Assert.Equal(double.MinValue, bounds.MaxY);
            Assert.Equal(double.MaxValue, bounds.MinZ);
            Assert.Equal(double.MinValue, bounds.MaxZ);
        }

        [Fact]
        public void Encapsulate_Point_ExpandsBounds()
        {
            var bounds = Bounds.Inverted();
            var point = new Vector3(5, 10, 15);

            bounds.Encapsulate(point);

            Assert.Equal(5.0, bounds.MinX);
            Assert.Equal(5.0, bounds.MaxX);
            Assert.Equal(10.0, bounds.MinY);
            Assert.Equal(10.0, bounds.MaxY);
            Assert.Equal(15.0, bounds.MinZ);
            Assert.Equal(15.0, bounds.MaxZ);
        }

        [Fact]
        public void Encapsulate_MultiplePoints_ExpandsCorrectly()
        {
            var bounds = Bounds.Inverted();
            bounds.Encapsulate(new Vector3(0, 0, 0));
            bounds.Encapsulate(new Vector3(10, 20, 30));
            bounds.Encapsulate(new Vector3(-5, -10, -15));

            Assert.Equal(-5.0, bounds.MinX);
            Assert.Equal(10.0, bounds.MaxX);
            Assert.Equal(-10.0, bounds.MinY);
            Assert.Equal(20.0, bounds.MaxY);
            Assert.Equal(-15.0, bounds.MinZ);
            Assert.Equal(30.0, bounds.MaxZ);
        }

        [Fact]
        public void Width_CalculatesCorrectly()
        {
            var bounds = new Bounds
            {
                MinX = -5,
                MaxX = 10
            };

            Assert.Equal(15.0, bounds.Width);
        }

        [Fact]
        public void Height_CalculatesCorrectly()
        {
            var bounds = new Bounds
            {
                MinY = 0,
                MaxY = 20
            };

            Assert.Equal(20.0, bounds.Height);
        }

        [Fact]
        public void Depth_CalculatesCorrectly()
        {
            var bounds = new Bounds
            {
                MinZ = -10,
                MaxZ = 5
            };

            Assert.Equal(15.0, bounds.Depth);
        }

        [Fact]
        public void MidX_CalculatesCorrectly()
        {
            var bounds = new Bounds
            {
                MinX = 0,
                MaxX = 10
            };

            Assert.Equal(5.0, bounds.MidX);
        }

        [Fact]
        public void MidY_CalculatesCorrectly()
        {
            var bounds = new Bounds
            {
                MinY = 0,
                MaxY = 20
            };

            Assert.Equal(10.0, bounds.MidY);
        }

        [Fact]
        public void MidZ_CalculatesCorrectly()
        {
            var bounds = new Bounds
            {
                MinZ = 0,
                MaxZ = 30
            };

            Assert.Equal(15.0, bounds.MidZ);
        }

        [Fact]
        public void Contains_PointInside_ReturnsTrue()
        {
            var bounds = new Bounds
            {
                MinX = 0,
                MaxX = 10,
                MinY = 0,
                MaxY = 10,
                MinZ = 0,
                MaxZ = 10
            };

            Assert.True(bounds.Contains(new Vertex { Position = new Vector3(5, 5, 5) }));
            Assert.True(bounds.Contains(new Vertex { Position = new Vector3(0, 0, 0) })); // On boundary
            Assert.True(bounds.Contains(new Vertex { Position = new Vector3(10, 10, 10) })); // On boundary
        }

        [Fact]
        public void Contains_PointOutside_ReturnsFalse()
        {
            var bounds = new Bounds
            {
                MinX = 0,
                MaxX = 10,
                MinY = 0,
                MaxY = 10,
                MinZ = 0,
                MaxZ = 10
            };

            Assert.False(bounds.Contains(new Vertex { Position = new Vector3(-1, 5, 5) }));
            Assert.False(bounds.Contains(new Vertex { Position = new Vector3(11, 5, 5) }));
            Assert.False(bounds.Contains(new Vertex { Position = new Vector3(5, -1, 5) }));
            Assert.False(bounds.Contains(new Vertex { Position = new Vector3(5, 11, 5) }));
            Assert.False(bounds.Contains(new Vertex { Position = new Vector3(5, 5, -1) }));
            Assert.False(bounds.Contains(new Vertex { Position = new Vector3(5, 5, 11) }));
        }

        [Fact]
        public void EncapsulateBounds_ExpandsCorrectly()
        {
            var bounds1 = new Bounds
            {
                MinX = 0,
                MaxX = 10,
                MinY = 0,
                MaxY = 10,
                MinZ = 0,
                MaxZ = 10
            };

            var bounds2 = new Bounds
            {
                MinX = -5,
                MaxX = 15,
                MinY = 5,
                MaxY = 20,
                MinZ = -10,
                MaxZ = 5
            };

            bounds1.Encapsulate(bounds2);

            Assert.Equal(-5.0, bounds1.MinX);
            Assert.Equal(15.0, bounds1.MaxX);
            Assert.Equal(0.0, bounds1.MinY);
            Assert.Equal(20.0, bounds1.MaxY);
            Assert.Equal(-10.0, bounds1.MinZ);
            Assert.Equal(10.0, bounds1.MaxZ);
        }

        [Fact]
        public void Intersects_OverlappingBounds_ReturnsTrue()
        {
            var a = new Bounds
            {
                MinX = 0,
                MaxX = 10,
                MinY = 0,
                MaxY = 10,
                MinZ = 0,
                MaxZ = 10
            };

            var b = new Bounds
            {
                MinX = 5,
                MaxX = 15,
                MinY = 5,
                MaxY = 15,
                MinZ = 5,
                MaxZ = 15
            };

            Assert.True(a.Intersects(b));
            Assert.True(b.Intersects(a));
        }

        [Fact]
        public void Intersects_NonOverlappingBounds_ReturnsFalse()
        {
            var a = new Bounds
            {
                MinX = 0,
                MaxX = 10,
                MinY = 0,
                MaxY = 10,
                MinZ = 0,
                MaxZ = 10
            };

            var b = new Bounds
            {
                MinX = 20,
                MaxX = 30,
                MinY = 20,
                MaxY = 30,
                MinZ = 20,
                MaxZ = 30
            };

            Assert.False(a.Intersects(b));
            Assert.False(b.Intersects(a));
        }

        [Fact]
        public void Intersects_TouchingBounds_ReturnsTrue()
        {
            var a = new Bounds
            {
                MinX = 0,
                MaxX = 10,
                MinY = 0,
                MaxY = 10,
                MinZ = 0,
                MaxZ = 10
            };

            var b = new Bounds
            {
                MinX = 10,
                MaxX = 20,
                MinY = 0,
                MaxY = 10,
                MinZ = 0,
                MaxZ = 10
            };

            Assert.True(a.Intersects(b));
        }

        [Fact]
        public void Expand_ReturnsExpandedBounds()
        {
            var bounds = new Bounds
            {
                MinX = 0,
                MaxX = 10,
                MinY = 0,
                MaxY = 10,
                MinZ = 0,
                MaxZ = 10
            };

            var expanded = bounds.Expand(5);

            Assert.Equal(-5.0, expanded.MinX);
            Assert.Equal(15.0, expanded.MaxX);
            Assert.Equal(-5.0, expanded.MinY);
            Assert.Equal(15.0, expanded.MaxY);
            Assert.Equal(-5.0, expanded.MinZ);
            Assert.Equal(15.0, expanded.MaxZ);

            // Original should be unchanged
            Assert.Equal(0.0, bounds.MinX);
            Assert.Equal(10.0, bounds.MaxX);
        }

        [Fact]
        public void FromPoints_ReturnsCorrectBounds()
        {
            var a = new Vertex { Position = new Vector3(0, 0, 0) };
            var b = new Vertex { Position = new Vector3(10, 5, 3) };
            var c = new Vertex { Position = new Vector3(-2, 8, 7) };

            var bounds = Bounds.FromPoints(a, b, c);

            Assert.Equal(-2.0, bounds.MinX);
            Assert.Equal(10.0, bounds.MaxX);
            Assert.Equal(0.0, bounds.MinY);
            Assert.Equal(8.0, bounds.MaxY);
            Assert.Equal(0.0, bounds.MinZ);
            Assert.Equal(7.0, bounds.MaxZ);
        }
    }
}
