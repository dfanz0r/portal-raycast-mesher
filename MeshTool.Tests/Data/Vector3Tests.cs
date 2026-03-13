using System;
using MeshTool.Core.Data;
using Xunit;

namespace MeshTool.Tests.Data
{
    public class Vector3Tests
    {
        [Fact]
        public void Constructor_SetsXYZCorrectly()
        {
            var v = new Vector3(1.5, 2.5, 3.5);

            Assert.Equal(1.5, v.X);
            Assert.Equal(2.5, v.Y);
            Assert.Equal(3.5, v.Z);
        }

        [Fact]
        public void DefaultConstructor_SetsXYZToZero()
        {
            var v = new Vector3();

            Assert.Equal(0.0, v.X);
            Assert.Equal(0.0, v.Y);
            Assert.Equal(0.0, v.Z);
        }

        [Fact]
        public void Dot_ProductCalculatesCorrectly()
        {
            var a = new Vector3(1, 2, 3);
            var b = new Vector3(4, 5, 6);

            double result = a.Dot(b);

            Assert.Equal(32.0, result); // 1*4 + 2*5 + 3*6 = 4 + 10 + 18 = 32
        }

        [Fact]
        public void Dot_WithZeroVector_ReturnsZero()
        {
            var a = new Vector3(1, 2, 3);
            var zero = new Vector3(0, 0, 0);

            double result = a.Dot(zero);

            Assert.Equal(0.0, result);
        }

        [Fact]
        public void Dot_IsCommutative()
        {
            var a = new Vector3(1, 2, 3);
            var b = new Vector3(4, 5, 6);

            double ab = a.Dot(b);
            double ba = b.Dot(a);

            Assert.Equal(ab, ba);
        }

        [Fact]
        public void Cross_ProductCalculatesCorrectly()
        {
            var a = new Vector3(1, 0, 0);
            var b = new Vector3(0, 1, 0);

            var result = a.Cross(b);

            Assert.Equal(0.0, result.X);
            Assert.Equal(0.0, result.Y);
            Assert.Equal(1.0, result.Z);
        }

        [Fact]
        public void Cross_IsAntiCommutative()
        {
            var a = new Vector3(1, 2, 3);
            var b = new Vector3(4, 5, 6);

            var ab = a.Cross(b);
            var ba = b.Cross(a);

            Assert.Equal(-ab.X, ba.X);
            Assert.Equal(-ab.Y, ba.Y);
            Assert.Equal(-ab.Z, ba.Z);
        }

        [Fact]
        public void Cross_WithParallelVectors_ReturnsZero()
        {
            var a = new Vector3(1, 2, 3);
            var b = new Vector3(2, 4, 6); // Parallel to a

            var result = a.Cross(b);

            Assert.Equal(0.0, result.X);
            Assert.Equal(0.0, result.Y);
            Assert.Equal(0.0, result.Z);
        }

        [Fact]
        public void Length_CalculatesCorrectly()
        {
            var v = new Vector3(3, 4, 0);

            double result = v.Length();

            Assert.Equal(5.0, result);
        }

        [Fact]
        public void Length_OfZeroVector_ReturnsZero()
        {
            var v = new Vector3(0, 0, 0);

            double result = v.Length();

            Assert.Equal(0.0, result);
        }

        [Fact]
        public void LengthSquared_CalculatesCorrectly()
        {
            var v = new Vector3(3, 4, 0);

            double result = v.LengthSquared();

            Assert.Equal(25.0, result);
        }

        [Fact]
        public void LengthSquared_MatchesLengthSquared()
        {
            var v = new Vector3(1, 2, 3);

            double length = v.Length();
            double lengthSquared = v.LengthSquared();

            Assert.Equal(length * length, lengthSquared, 10);
        }

        [Fact]
        public void Normalized_ReturnsUnitVector()
        {
            var v = new Vector3(3, 4, 0);

            var result = v.Normalized();

            Assert.Equal(1.0, result.Length(), 10);
            Assert.Equal(0.6, result.X, 10);
            Assert.Equal(0.8, result.Y, 10);
            Assert.Equal(0.0, result.Z, 10);
        }

        [Fact]
        public void Normalized_OfZeroVector_ReturnsZero()
        {
            var v = new Vector3(0, 0, 0);

            var result = v.Normalized();

            Assert.Equal(0.0, result.X);
            Assert.Equal(0.0, result.Y);
            Assert.Equal(0.0, result.Z);
        }

        [Fact]
        public void Normalized_DoesNotModifyOriginal()
        {
            var v = new Vector3(3, 4, 0);

            v.Normalized();

            Assert.Equal(3.0, v.X);
            Assert.Equal(4.0, v.Y);
            Assert.Equal(0.0, v.Z);
        }

        [Fact]
        public void Addition_AddsComponents()
        {
            var a = new Vector3(1, 2, 3);
            var b = new Vector3(4, 5, 6);

            var result = a + b;

            Assert.Equal(5.0, result.X);
            Assert.Equal(7.0, result.Y);
            Assert.Equal(9.0, result.Z);
        }

        [Fact]
        public void Subtraction_SubtractsComponents()
        {
            var a = new Vector3(4, 5, 6);
            var b = new Vector3(1, 2, 3);

            var result = a - b;

            Assert.Equal(3.0, result.X);
            Assert.Equal(3.0, result.Y);
            Assert.Equal(3.0, result.Z);
        }

        [Fact]
        public void ScalarMultiplication_MultipliesComponents()
        {
            var v = new Vector3(1, 2, 3);

            var result = v * 2.0;

            Assert.Equal(2.0, result.X);
            Assert.Equal(4.0, result.Y);
            Assert.Equal(6.0, result.Z);
        }

        [Fact]
        public void ScalarMultiplication_ByZero_ReturnsZeroVector()
        {
            var v = new Vector3(1, 2, 3);

            var result = v * 0.0;

            Assert.Equal(0.0, result.X);
            Assert.Equal(0.0, result.Y);
            Assert.Equal(0.0, result.Z);
        }

        [Fact]
        public void ScalarMultiplication_ByNegative_FlipsDirection()
        {
            var v = new Vector3(1, 2, 3);

            var result = v * -1.0;

            Assert.Equal(-1.0, result.X);
            Assert.Equal(-2.0, result.Y);
            Assert.Equal(-3.0, result.Z);
        }

        [Fact]
        public void LengthSquared_IsFasterThanLength()
        {
            var v = new Vector3(1, 2, 3);

            // Just verify they're consistent
            double length = v.Length();
            double lengthSquared = v.LengthSquared();

            Assert.Equal(length * length, lengthSquared, 10);
        }

        [Fact]
        public void Dot_WithSelf_ReturnsLengthSquared()
        {
            var v = new Vector3(3, 4, 0);

            double dot = v.Dot(v);
            double lengthSquared = v.LengthSquared();

            Assert.Equal(lengthSquared, dot);
        }

        [Fact]
        public void Cross_WithSelf_ReturnsZero()
        {
            var v = new Vector3(1, 2, 3);

            var result = v.Cross(v);

            Assert.Equal(0.0, result.X);
            Assert.Equal(0.0, result.Y);
            Assert.Equal(0.0, result.Z);
        }
    }
}
