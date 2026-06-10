using System;

namespace MeshTool.UI.Models
{
    public readonly record struct ScanVolumeSettings(
        float CenterX,
        float CenterZ,
        float SizeX,
        float SizeZ,
        float YTop,
        float YBottom,
        float YawDegrees,
        float RayTiltDegrees,
        float ProbeCellSize,
        int BotCount)
    {
        public static ScanVolumeSettings Default => new ScanVolumeSettings(
            CenterX: 0f,
            CenterZ: 0f,
            SizeX: 12000f,
            SizeZ: 12000f,
            YTop: 4000f,
            YBottom: -400f,
            YawDegrees: 0f,
            RayTiltDegrees: 0f,
            ProbeCellSize: 384f,
            BotCount: 5);

        public ScanVolumeSettings Sanitize()
        {
            float sx = MathF.Max(10f, SizeX);
            float sz = MathF.Max(10f, SizeZ);
            float top = YTop;
            float bottom = YBottom;
            if (bottom > top)
            {
                (top, bottom) = (bottom, top);
            }

            float cell = MathF.Max(8f, ProbeCellSize);
            float tilt = Math.Clamp(RayTiltDegrees, -89f, 89f);
            int bots = Math.Clamp(BotCount, 1, 20);

            return this with
            {
                SizeX = sx,
                SizeZ = sz,
                YTop = top,
                YBottom = bottom,
                RayTiltDegrees = tilt,
                ProbeCellSize = cell,
                BotCount = bots
            };
        }
    }
}
