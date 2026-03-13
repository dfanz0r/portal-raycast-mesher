using MeshTool.UI.Models;
using Silk.NET.Maths;
using System;
using System.Collections.Generic;

namespace MeshTool.UI.Rendering
{
    /// <summary>
    /// Builds vertex data for scan volume visualization.
    /// </summary>
    public static class ScanVolumeGeometryBuilder
    {
        /// <summary>
        /// Builds line vertices for the scan volume wireframe.
        /// </summary>
        /// <param name="s">The scan volume settings.</param>
        /// <param name="hoverHandle">The currently hovered handle ID.</param>
        /// <param name="activeHandle">The currently active handle ID.</param>
        /// <param name="showHelpers">Whether to show helper geometry.</param>
        /// <param name="showSelectionBox">Whether to show the selection box.</param>
        /// <param name="selectionStartWorld">Selection box start point.</param>
        /// <param name="selectionEndWorld">Selection box end point.</param>
        /// <param name="selectionYBottom">Selection box bottom Y.</param>
        /// <param name="selectionYTop">Selection box top Y.</param>
        /// <returns>Array of float values (6 floats per vertex: x, y, z, r, g, b).</returns>
        public static float[] BuildScanVolumeLineVertices(
            ScanVolumeSettings s,
            int hoverHandle,
            int activeHandle,
            bool showHelpers,
            bool showSelectionBox,
            Vector3D<float> selectionStartWorld,
            Vector3D<float> selectionEndWorld,
            float selectionYBottom,
            float selectionYTop)
        {
            s = s.Sanitize();
            float hx = s.SizeX * 0.5f;
            float hz = s.SizeZ * 0.5f;
            float yaw = s.YawDegrees * (MathF.PI / 180f);
            float cos = MathF.Cos(yaw);
            float sin = MathF.Sin(yaw);

            Vector3D<float> RotateLocal(float lx, float lz, float y)
            {
                float wx = s.CenterX + (lx * cos - lz * sin);
                float wz = s.CenterZ + (lx * sin + lz * cos);
                return new Vector3D<float>(wx, y, wz);
            }

            Vector3D<float>[] top =
            {
                RotateLocal(-hx, -hz, s.YTop),
                RotateLocal(hx, -hz, s.YTop),
                RotateLocal(hx, hz, s.YTop),
                RotateLocal(-hx, hz, s.YTop)
            };

            Vector3D<float>[] bottom =
            {
                RotateLocal(-hx, -hz, s.YBottom),
                RotateLocal(hx, -hz, s.YBottom),
                RotateLocal(hx, hz, s.YBottom),
                RotateLocal(-hx, hz, s.YBottom)
            };

            var verts = new List<float>(32 * 2 * 6);

            void AddLine(Vector3D<float> a, Vector3D<float> b, float r, float g, float bl)
            {
                verts.Add(a.X); verts.Add(a.Y); verts.Add(a.Z); verts.Add(r); verts.Add(g); verts.Add(bl);
                verts.Add(b.X); verts.Add(b.Y); verts.Add(b.Z); verts.Add(r); verts.Add(g); verts.Add(bl);
            }

            for (int i = 0; i < 4; i++)
            {
                int n = (i + 1) % 4;
                AddLine(top[i], top[n], 0.2f, 0.85f, 1.0f);
                AddLine(bottom[i], bottom[n], 0.2f, 0.85f, 1.0f);
                AddLine(top[i], bottom[i], 0.2f, 0.85f, 1.0f);
            }

            if (showHelpers)
            {
                AddHelperGeometry(s, hoverHandle, activeHandle, verts, AddLine, hx, hz, yaw);
            }

            if (showSelectionBox)
            {
                AddSelectionBox(selectionStartWorld, selectionEndWorld, selectionYBottom, selectionYTop, verts, AddLine);
            }

            return verts.ToArray();
        }

        private static void AddHelperGeometry(
            ScanVolumeSettings s,
            int hoverHandle,
            int activeHandle,
            List<float> verts,
            Action<Vector3D<float>, Vector3D<float>, float, float, float> AddLine,
            float hx,
            float hz,
            float yaw)
        {
            float tiltRad = s.RayTiltDegrees * (MathF.PI / 180f);
            float h = MathF.Tan(MathF.Abs(tiltRad));
            float sign = s.RayTiltDegrees >= 0 ? 1f : -1f;
            var dir = Vector3D.Normalize(new Vector3D<float>(MathF.Cos(yaw) * h * sign, -1f, MathF.Sin(yaw) * h * sign));

            float arrowLen = MathF.Max(50f, (s.YTop - s.YBottom) * 0.8f);
            var start = new Vector3D<float>(s.CenterX, s.YTop, s.CenterZ);
            var end = start + dir * arrowLen;
            AddLine(start, end, 1.0f, 0.6f, 0.1f);

            var side = Vector3D.Normalize(Vector3D.Cross(dir, new Vector3D<float>(0, 1, 0)));
            if (side.LengthSquared < 1e-6f)
            {
                side = new Vector3D<float>(1, 0, 0);
            }
            var up = Vector3D.Normalize(Vector3D.Cross(side, dir));
            float head = MathF.Max(12f, arrowLen * 0.08f);

            // Add thicker directional helper lines
            for (int m = -1; m <= 1; m++)
            {
                var offsetDir = side * (0.5f * m);
                var offsetUp = up * (0.5f * m);
                AddLine(start + offsetDir + offsetUp, end + offsetDir + offsetUp, 1.0f, 0.6f, 0.1f);
            }

            AddLine(end, end - dir * head + side * (head * 0.5f), 1.0f, 0.6f, 0.1f);
            AddLine(end, end - dir * head - side * (head * 0.5f), 1.0f, 0.6f, 0.1f);
            AddLine(end, end - dir * head + up * (head * 0.35f), 1.0f, 0.6f, 0.1f);

            float midY = (s.YTop + s.YBottom) * 0.5f;
            var centerMid = new Vector3D<float>(s.CenterX, midY, s.CenterZ);
            var xAxis = Vector3D.Normalize(new Vector3D<float>(MathF.Cos(yaw), 0f, MathF.Sin(yaw)));
            var zAxis = Vector3D.Normalize(new Vector3D<float>(-MathF.Sin(yaw), 0f, MathF.Cos(yaw)));
            float rotateHandleOffset = hx + MathF.Max(40f, MathF.Min(hx, hz) * 0.35f);
            float moveOffset = MathF.Max(20f, MathF.Min(hx, hz) * 0.35f);
            float rotateRadiusBase = Math.Clamp(MathF.Min(hx, hz) * 0.25f, 30f, 600f);
            float rotateRadius = MathF.Max(rotateRadiusBase, moveOffset + MathF.Max(20f, MathF.Min(hx, hz) * 0.12f));
            var hXPos = centerMid + xAxis * hx;
            var hXNeg = centerMid - xAxis * hx;
            var hZPos = centerMid + zAxis * hz;
            var hZNeg = centerMid - zAxis * hz;
            var hTop = new Vector3D<float>(s.CenterX, s.YTop, s.CenterZ);
            var hBottom = new Vector3D<float>(s.CenterX, s.YBottom, s.CenterZ);
            var hRotate = centerMid + xAxis * rotateHandleOffset;
            var hMoveX = centerMid + xAxis * moveOffset;
            var hMoveZ = centerMid + zAxis * moveOffset;

            const float redR = 0.965f;
            const float redG = 0.24f;
            const float redB = 0.24f;

            (float R, float G, float B) LineColorFor(int handleId, float r, float g, float b)
            {
                if (activeHandle == handleId) return (1.0f, 0.95f, 0.25f);
                if (hoverHandle == handleId) return (1.0f, 1.0f, 1.0f);
                return (r, g, b);
            }

            float ringRadius = rotateRadius;
            const int ringSegments = 40;
            float ringScale = (activeHandle == 8) ? 1.45f : (hoverHandle == 8 ? 1.25f : 1.0f);
            float ringR = (activeHandle == 8 || hoverHandle == 8) ? 1.0f : 1.0f;
            float ringG = (activeHandle == 8) ? 0.95f : (hoverHandle == 8 ? 1.0f : 0.6f);
            float ringB = (activeHandle == 8) ? 0.25f : (hoverHandle == 8 ? 1.0f : 0.1f);
            var rotateLineStart = centerMid + xAxis * (ringRadius * ringScale);
            AddLine(rotateLineStart, hRotate, ringR, ringG, ringB);
            var moveXLine = LineColorFor(9, redR, redG, redB);
            var moveZLine = LineColorFor(10, 0.15f, 0.45f, 1.0f);
            AddLine(centerMid, hMoveX, moveXLine.R, moveXLine.G, moveXLine.B);
            AddLine(centerMid, hMoveZ, moveZLine.R, moveZLine.G, moveZLine.B);
            for (int i = 0; i < ringSegments; i++)
            {
                float a0 = (i / (float)ringSegments) * MathF.PI * 2f;
                float a1 = ((i + 1) / (float)ringSegments) * MathF.PI * 2f;
                var p0 = centerMid + xAxis * (MathF.Cos(a0) * ringRadius * ringScale) + zAxis * (MathF.Sin(a0) * ringRadius * ringScale);
                var p1 = centerMid + xAxis * (MathF.Cos(a1) * ringRadius * ringScale) + zAxis * (MathF.Sin(a1) * ringRadius * ringScale);
                AddLine(p0, p1, ringR, ringG, ringB);
            }
        }

        private static void AddSelectionBox(
            Vector3D<float> selectionStartWorld,
            Vector3D<float> selectionEndWorld,
            float selectionYBottom,
            float selectionYTop,
            List<float> verts,
            Action<Vector3D<float>, Vector3D<float>, float, float, float> AddLine)
        {
            float minX = MathF.Min(selectionStartWorld.X, selectionEndWorld.X);
            float maxX = MathF.Max(selectionStartWorld.X, selectionEndWorld.X);
            float minZ = MathF.Min(selectionStartWorld.Z, selectionEndWorld.Z);
            float maxZ = MathF.Max(selectionStartWorld.Z, selectionEndWorld.Z);
            float y0 = MathF.Min(selectionYBottom, selectionYTop);
            float y1 = MathF.Max(selectionYBottom, selectionYTop);

            if ((maxX - minX) < 0.5f) { minX -= 1f; maxX += 1f; }
            if ((maxZ - minZ) < 0.5f) { minZ -= 1f; maxZ += 1f; }

            var b0 = new Vector3D<float>(minX, y0, minZ);
            var b1 = new Vector3D<float>(maxX, y0, minZ);
            var b2 = new Vector3D<float>(maxX, y0, maxZ);
            var b3 = new Vector3D<float>(minX, y0, maxZ);
            var t0 = new Vector3D<float>(minX, y1, minZ);
            var t1 = new Vector3D<float>(maxX, y1, minZ);
            var t2 = new Vector3D<float>(maxX, y1, maxZ);
            var t3 = new Vector3D<float>(minX, y1, maxZ);

            const float sr = 1.0f;
            const float sg = 0.42f;
            const float sb = 0.95f;
            if (MathF.Abs(y1 - y0) < 0.001f)
            {
                AddLine(b0, b1, sr, sg, sb); AddLine(b1, b2, sr, sg, sb); AddLine(b2, b3, sr, sg, sb); AddLine(b3, b0, sr, sg, sb);
            }
            else
            {
                AddLine(b0, b1, sr, sg, sb); AddLine(b1, b2, sr, sg, sb); AddLine(b2, b3, sr, sg, sb); AddLine(b3, b0, sr, sg, sb);
                AddLine(t0, t1, sr, sg, sb); AddLine(t1, t2, sr, sg, sb); AddLine(t2, t3, sr, sg, sb); AddLine(t3, t0, sr, sg, sb);
                AddLine(b0, t0, sr, sg, sb); AddLine(b1, t1, sr, sg, sb); AddLine(b2, t2, sr, sg, sb); AddLine(b3, t3, sr, sg, sb);
            }
        }

        /// <summary>
        /// Builds solid vertices for scan handle gizmos.
        /// </summary>
        /// <param name="s">The scan volume settings.</param>
        /// <param name="hoverHandle">The currently hovered handle ID.</param>
        /// <param name="activeHandle">The currently active handle ID.</param>
        /// <param name="camPos">The camera position for LOD calculations.</param>
        /// <returns>Array of float values (10 floats per vertex: x, y, z, nx, ny, nz, r, g, b, a).</returns>
        public static float[] BuildScanHandleSolidVertices(ScanVolumeSettings s, int hoverHandle, int activeHandle, Vector3D<float> camPos)
        {
            s = s.Sanitize();
            float hx = s.SizeX * 0.5f;
            float hz = s.SizeZ * 0.5f;
            float yaw = s.YawDegrees * (MathF.PI / 180f);
            var xAxis = Vector3D.Normalize(new Vector3D<float>(MathF.Cos(yaw), 0f, MathF.Sin(yaw)));
            var zAxis = Vector3D.Normalize(new Vector3D<float>(-MathF.Sin(yaw), 0f, MathF.Cos(yaw)));
            float midY = (s.YTop + s.YBottom) * 0.5f;
            var centerMid = new Vector3D<float>(s.CenterX, midY, s.CenterZ);
            float moveOffset = MathF.Max(20f, MathF.Min(hx, hz) * 0.35f);
            float rotateRadius = Math.Clamp(MathF.Min(hx, hz) * 0.25f, 30f, 600f);
            float rotateHandleOffset = hx + MathF.Max(40f, MathF.Min(hx, hz) * 0.35f);

            var hXPos = centerMid + xAxis * hx;
            var hXNeg = centerMid - xAxis * hx;
            var hZPos = centerMid + zAxis * hz;
            var hZNeg = centerMid - zAxis * hz;
            var hTop = new Vector3D<float>(s.CenterX, s.YTop, s.CenterZ);
            var hBottom = new Vector3D<float>(s.CenterX, s.YBottom, s.CenterZ);
            var hRotate = centerMid + xAxis * rotateHandleOffset;
            var hMoveX = centerMid + xAxis * moveOffset;
            var hMoveZ = centerMid + zAxis * moveOffset;

            // Compute a base size that scales with camera distance to maintain visual consistency
            float distToCamera = (centerMid - camPos).Length;
            float baseSize = Math.Clamp(distToCamera * 0.02f, 0.5f, 400f);

            const float redR = 0.965f;
            const float redG = 0.24f;
            const float redB = 0.24f;
            var verts = new List<float>(2048);

            (float R, float G, float B, float Scale) StyleFor(int handleId, float r, float g, float b)
            {
                if (activeHandle == handleId) return (1.0f, 0.95f, 0.25f, 1.45f);
                if (hoverHandle == handleId) return (1.0f, 1.0f, 1.0f, 1.25f);
                return (r, g, b, 1.0f);
            }

            void AddTri(Vector3D<float> a, Vector3D<float> b, Vector3D<float> c, float r, float g, float bl, float alpha = 1.0f)
            {
                var n = Vector3D.Normalize(Vector3D.Cross(b - a, c - a));
                void Emit(Vector3D<float> p)
                {
                    verts.Add(p.X); verts.Add(p.Y); verts.Add(p.Z);
                    verts.Add(n.X); verts.Add(n.Y); verts.Add(n.Z);
                    verts.Add(r); verts.Add(g); verts.Add(bl);
                    verts.Add(alpha);
                }
                Emit(a); Emit(b); Emit(c);
            }

            void AddTriWithNormal(Vector3D<float> a, Vector3D<float> b, Vector3D<float> c, Vector3D<float> n, float r, float g, float bl, float alpha = 1.0f)
            {
                n = Vector3D.Normalize(n);
                void Emit(Vector3D<float> p)
                {
                    verts.Add(p.X); verts.Add(p.Y); verts.Add(p.Z);
                    verts.Add(n.X); verts.Add(n.Y); verts.Add(n.Z);
                    verts.Add(r); verts.Add(g); verts.Add(bl);
                    verts.Add(alpha);
                }
                Emit(a); Emit(b); Emit(c);
            }

            void AddCone(Vector3D<float> baseCenter, Vector3D<float> dir, float length, float radius, float r, float g, float bl)
            {
                var d = Vector3D.Normalize(dir);
                var tip = baseCenter + d * length;
                Vector3D<float> refAxis = MathF.Abs(d.Y) > 0.8f ? new Vector3D<float>(1, 0, 0) : new Vector3D<float>(0, 1, 0);
                var u = Vector3D.Normalize(Vector3D.Cross(d, refAxis));
                var v = Vector3D.Normalize(Vector3D.Cross(u, d));
                const int segments = 14;

                void EmitVertex(Vector3D<float> p, Vector3D<float> n, float cr, float cg, float cb)
                {
                    verts.Add(p.X); verts.Add(p.Y); verts.Add(p.Z);
                    verts.Add(n.X); verts.Add(n.Y); verts.Add(n.Z);
                    verts.Add(cr); verts.Add(cg); verts.Add(cb);
                    verts.Add(1.0f);
                }

                for (int i = 0; i < segments; i++)
                {
                    float a0 = (i / (float)segments) * MathF.PI * 2f;
                    float a1 = ((i + 1) / (float)segments) * MathF.PI * 2f;
                    var radial0 = u * MathF.Cos(a0) + v * MathF.Sin(a0);
                    var radial1 = u * MathF.Cos(a1) + v * MathF.Sin(a1);
                    var p0 = baseCenter + radial0 * radius;
                    var p1 = baseCenter + radial1 * radius;

                    var n0 = Vector3D.Normalize(radial0 + d * (radius / MathF.Max(1e-4f, length)));
                    var n1 = Vector3D.Normalize(radial1 + d * (radius / MathF.Max(1e-4f, length)));
                    var nTip = Vector3D.Normalize((n0 + n1) * 0.5f);

                    EmitVertex(tip, nTip, r, g, bl);
                    EmitVertex(p0, n0, r, g, bl);
                    EmitVertex(p1, n1, r, g, bl);

                    AddTri(baseCenter, p1, p0, r * 0.85f, g * 0.85f, bl * 0.85f);
                }
            }

            void AddSphere(Vector3D<float> c, float radius, float r, float g, float bl)
            {
                const int lon = 24;
                const int lat = 16;

                void AddSmoothTri(Vector3D<float> a, Vector3D<float> b, Vector3D<float> d)
                {
                    void Emit(Vector3D<float> p)
                    {
                        var n = Vector3D.Normalize(p - c);
                        verts.Add(p.X); verts.Add(p.Y); verts.Add(p.Z);
                        verts.Add(n.X); verts.Add(n.Y); verts.Add(n.Z);
                        verts.Add(r); verts.Add(g); verts.Add(bl);
                        verts.Add(1.0f);
                    }

                    Emit(a);
                    Emit(b);
                    Emit(d);
                }

                for (int iy = 0; iy < lat; iy++)
                {
                    float v0 = iy / (float)lat;
                    float v1 = (iy + 1) / (float)lat;
                    float th0 = v0 * MathF.PI;
                    float th1 = v1 * MathF.PI;
                    for (int ix = 0; ix < lon; ix++)
                    {
                        float u0 = ix / (float)lon;
                        float u1 = (ix + 1) / (float)lon;
                        float ph0 = u0 * MathF.PI * 2f;
                        float ph1 = u1 * MathF.PI * 2f;

                        Vector3D<float> P(float th, float ph)
                        {
                            float sx = MathF.Sin(th) * MathF.Cos(ph);
                            float sy = MathF.Cos(th);
                            float sz = MathF.Sin(th) * MathF.Sin(ph);
                            return c + new Vector3D<float>(sx, sy, sz) * radius;
                        }

                        var p00 = P(th0, ph0);
                        var p01 = P(th0, ph1);
                        var p10 = P(th1, ph0);
                        var p11 = P(th1, ph1);
                        AddSmoothTri(p00, p10, p11);
                        AddSmoothTri(p00, p11, p01);
                    }
                }
            }

            void AddPlaneHandle(int id, Vector3D<float> center, Vector3D<float> axisU, Vector3D<float> axisV, float halfU, float halfV, float r, float g, float bl)
            {
                bool isActive = activeHandle == id;
                bool isHover = hoverHandle == id;
                float alpha = (isActive || isHover) ? 1.0f : 0.20f;

                float cr = r;
                float cg = g;
                float cb = bl;
                if (isActive || isHover)
                {
                    float boost = isActive ? 1.75f : 1.55f;
                    cr = Math.Clamp(cr * boost, 0f, 1f);
                    cg = Math.Clamp(cg * boost, 0f, 1f);
                    cb = Math.Clamp(cb * boost, 0f, 1f);

                    // Keep the dominant channel vivid (especially red/blue handles).
                    float maxC = MathF.Max(cr, MathF.Max(cg, cb));
                    if (maxC > 1e-5f)
                    {
                        float inv = 1.0f / maxC;
                        cr = Math.Clamp(cr * inv, 0f, 1f);
                        cg = Math.Clamp(cg * inv, 0f, 1f);
                        cb = Math.Clamp(cb * inv, 0f, 1f);
                    }
                }
                float hu = halfU;
                float hv = halfV;
                var u = Vector3D.Normalize(axisU);
                var v = Vector3D.Normalize(axisV);
                var p00 = center - u * hu - v * hv;
                var p01 = center - u * hu + v * hv;
                var p10 = center + u * hu - v * hv;
                var p11 = center + u * hu + v * hv;

                var lightDir = Vector3D.Normalize(new Vector3D<float>(0.5f, 1.0f, 0.5f));
                AddTriWithNormal(p00, p10, p11, lightDir, cr, cg, cb, alpha);
                AddTriWithNormal(p00, p11, p01, lightDir, cr, cg, cb, alpha);
                AddTriWithNormal(p00, p01, p11, lightDir, cr, cg, cb, alpha);
                AddTriWithNormal(p00, p11, p10, lightDir, cr, cg, cb, alpha);
            }

            void AddMoveHandle(int id, Vector3D<float> pos, Vector3D<float> axis, float r, float g, float bl, float sizeMul = 1.0f)
            {
                var st = StyleFor(id, r, g, bl);
                float scale = st.Scale * sizeMul;
                AddCone(pos - axis * (baseSize * 0.8f * scale), axis, baseSize * 1.9f * scale, baseSize * 0.58f * scale, st.R, st.G, st.B);
            }

            void AddRotateHandle(int id, Vector3D<float> pos, float r, float g, float bl)
            {
                var st = StyleFor(id, r, g, bl);
                AddSphere(pos, baseSize * 0.95f * st.Scale, st.R, st.G, st.B);
            }

            AddRotateHandle(1, centerMid, 1.0f, 1.0f, 0.2f);
            float ySpan = MathF.Max(8f, s.YTop - s.YBottom);
            float sharedFaceMin = MathF.Min(MathF.Min(2f * hx, 2f * hz), ySpan);
            float sharedSquareHalf = MathF.Max(24f, sharedFaceMin / 3f);
            float faceOffset = 0.75f;

            bool IsFaceVisible(Vector3D<float> faceCenter, Vector3D<float> outwardNormal)
            {
                var toCam = camPos - faceCenter;
                return Vector3D.Dot(outwardNormal, toCam) > 0.0f;
            }

            var xPosCenter = hXPos - xAxis * faceOffset;
            var xNegCenter = hXNeg + xAxis * faceOffset;
            var zPosCenter = hZPos - zAxis * faceOffset;
            var zNegCenter = hZNeg + zAxis * faceOffset;

            if (IsFaceVisible(xPosCenter, xAxis))
                AddPlaneHandle(2, xPosCenter, zAxis, new Vector3D<float>(0, 1, 0), sharedSquareHalf, sharedSquareHalf, redR, redG, redB);
            if (IsFaceVisible(xNegCenter, -xAxis))
                AddPlaneHandle(3, xNegCenter, zAxis, new Vector3D<float>(0, 1, 0), sharedSquareHalf, sharedSquareHalf, redR, redG, redB);
            if (IsFaceVisible(zPosCenter, zAxis))
                AddPlaneHandle(4, zPosCenter, xAxis, new Vector3D<float>(0, 1, 0), sharedSquareHalf, sharedSquareHalf, 0.3f, 0.5f, 1.0f);
            if (IsFaceVisible(zNegCenter, -zAxis))
                AddPlaneHandle(5, zNegCenter, xAxis, new Vector3D<float>(0, 1, 0), sharedSquareHalf, sharedSquareHalf, 0.3f, 0.5f, 1.0f);

            var yUp = new Vector3D<float>(0, 1, 0);
            var topCenter = hTop - yUp * faceOffset;
            var bottomCenter = hBottom + yUp * faceOffset;
            if (IsFaceVisible(topCenter, yUp))
                AddPlaneHandle(6, topCenter, xAxis, zAxis, sharedSquareHalf, sharedSquareHalf, 0.2f, 0.95f, 0.4f);
            if (IsFaceVisible(bottomCenter, -yUp))
                AddPlaneHandle(7, bottomCenter, xAxis, zAxis, sharedSquareHalf, sharedSquareHalf, 0.2f, 0.95f, 0.4f);
            AddRotateHandle(8, hRotate, 1.0f, 0.6f, 0.1f);
            AddMoveHandle(9, hMoveX, xAxis, redR, redG, redB, 0.95f);
            AddMoveHandle(10, hMoveZ, zAxis, 0.225f, 0.475f, 1.0f, 0.95f);

            return verts.ToArray();
        }

        /// <summary>
        /// Builds vertices for scan density preview points.
        /// </summary>
        /// <param name="s">The scan volume settings.</param>
        /// <param name="gridPlaneY">The Y position of the grid plane.</param>
        /// <param name="fineTargetStep">The fine scan step size.</param>
        /// <param name="camPos">The camera position.</param>
        /// <param name="fineDensityPreviewRadius">The radius for fine density preview (modified by reference).</param>
        /// <returns>A tuple containing the vertex array and broad point count.</returns>
        public static (float[] Vertices, int BroadCount) BuildScanDensityVertices(
            ScanVolumeSettings s,
            float gridPlaneY,
            float fineTargetStep,
            Vector3D<float> camPos,
            ref float fineDensityPreviewRadius)
        {
            const int FineDensityPreviewTargetPoints = 36000;
            const float FineDensityPreviewAdjustRate = 0.35f;

            s = s.Sanitize();
            float hx = s.SizeX * 0.5f;
            float hz = s.SizeZ * 0.5f;
            float broadCell = MathF.Max(8f, s.ProbeCellSize);
            float fineCell = MathF.Max(8f, fineTargetStep);
            float yaw = s.YawDegrees * (MathF.PI / 180f);
            var xAxis = Vector3D.Normalize(new Vector3D<float>(MathF.Cos(yaw), 0f, MathF.Sin(yaw)));
            var zAxis = Vector3D.Normalize(new Vector3D<float>(-MathF.Sin(yaw), 0f, MathF.Cos(yaw)));

            float y = gridPlaneY + 2f;

            int countX = Math.Max(1, (int)MathF.Floor((s.SizeX - broadCell) / broadCell) + 1);
            int countZ = Math.Max(1, (int)MathF.Floor((s.SizeZ - broadCell) / broadCell) + 1);
            int fineCountX = Math.Max(1, (int)MathF.Floor((s.SizeX - fineCell) / fineCell) + 1);
            int fineCountZ = Math.Max(1, (int)MathF.Floor((s.SizeZ - fineCell) / fineCell) + 1);

            float broadMinX = -hx + broadCell * 0.5f;
            float broadMinZ = -hz + broadCell * 0.5f;
            var verts = new List<float>((countX * countZ + fineCountX * fineCountZ) * 6);
            float fineRadiusSq = fineDensityPreviewRadius * fineDensityPreviewRadius;

            // Broad phase points (larger, brighter green)
            for (int ix = 0; ix < countX; ix++)
            {
                float lx = broadMinX + ix * broadCell;
                for (int iz = 0; iz < countZ; iz++)
                {
                    float lz = broadMinZ + iz * broadCell;
                    var p = new Vector3D<float>(s.CenterX, y, s.CenterZ) + xAxis * lx + zAxis * lz;
                    verts.Add(p.X); verts.Add(p.Y); verts.Add(p.Z);
                    verts.Add(0.2f); verts.Add(0.95f); verts.Add(0.45f);
                }
            }
            int broadCount = verts.Count / 6;

            // Fine phase points (smaller spacing; camera-radius culled)
            float fineMinX = -hx + fineCell * 0.5f;
            float fineMinZ = -hz + fineCell * 0.5f;
            float camDx = camPos.X - s.CenterX;
            float camDz = camPos.Z - s.CenterZ;
            float camLocalX = (camDx * xAxis.X) + (camDz * xAxis.Z);
            float camLocalZ = (camDx * zAxis.X) + (camDz * zAxis.Z);
            float radius = fineDensityPreviewRadius;

            int ixMin = Math.Max(0, (int)MathF.Ceiling(((camLocalX - radius) - fineMinX) / fineCell));
            int ixMax = Math.Min(fineCountX - 1, (int)MathF.Floor(((camLocalX + radius) - fineMinX) / fineCell));
            int izMin = Math.Max(0, (int)MathF.Ceiling(((camLocalZ - radius) - fineMinZ) / fineCell));
            int izMax = Math.Min(fineCountZ - 1, (int)MathF.Floor(((camLocalZ + radius) - fineMinZ) / fineCell));

            int renderedFineCount = 0;
            for (int ix = ixMin; ix <= ixMax; ix++)
            {
                float lx = fineMinX + ix * fineCell;
                for (int iz = izMin; iz <= izMax; iz++)
                {
                    float lz = fineMinZ + iz * fineCell;
                    var p = new Vector3D<float>(s.CenterX, y + 0.5f, s.CenterZ) + xAxis * lx + zAxis * lz;
                    float dx = p.X - camPos.X;
                    float dz = p.Z - camPos.Z;
                    if ((dx * dx) + (dz * dz) > fineRadiusSq)
                    {
                        continue;
                    }
                    verts.Add(p.X); verts.Add(p.Y); verts.Add(p.Z);
                    verts.Add(0.1f); verts.Add(0.65f); verts.Add(1.0f);
                    renderedFineCount++;
                }
            }

            float minRadius = Math.Max(700f, fineCell * 6f);
            float maxRadius = Math.Max(4200f, MathF.Sqrt((s.SizeX * s.SizeX) + (s.SizeZ * s.SizeZ)) * 1.25f);
            float sampleCount = Math.Max(1f, renderedFineCount);
            float ratio = FineDensityPreviewTargetPoints / sampleCount;
            float factor = Math.Clamp(MathF.Sqrt(ratio), 0.7f, 1.35f);
            float desiredRadius = Math.Clamp(fineDensityPreviewRadius * factor, minRadius, maxRadius);
            fineDensityPreviewRadius += (desiredRadius - fineDensityPreviewRadius) * FineDensityPreviewAdjustRate;

            return (verts.ToArray(), broadCount);
        }
    }
}
