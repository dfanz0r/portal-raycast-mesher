namespace MeshTool.Core.Config
{
    /// <summary>
    /// Global configuration settings for mesh generation and processing.
    /// </summary>
    public static class Settings
    {
        /// <summary>
        /// Minimum distance for merging nearby points (0.0001 = 0.1mm precision).
        /// Points closer than this distance are considered duplicates.
        /// </summary>
        public const double MIN_MERGE_DISTANCE = 0.0001;

        /// <summary>
        /// Maximum allowed aspect ratio for triangles (longest edge / shortest altitude).
        /// Triangles exceeding this ratio are filtered out.
        /// </summary>
        public const double MAX_TRIANGLE_ASPECT_RATIO = 40.0;

        /// <summary>
        /// Multiplier applied to local point spacing when determining if a boundary edge is too long.
        /// </summary>
        public const double BOUNDARY_EDGE_SPACING_MULTIPLIER = 14.0;

        /// <summary>
        /// Minimum normalized support required for boundary triangles to be kept.
        /// </summary>
        public const double BOUNDARY_MIN_NORMALIZED_SUPPORT = 0.02;

        /// <summary>
        /// Multiplier for height tolerance in boundary culling.
        /// </summary>
        public const double BOUNDARY_HEIGHT_TOL_MULTIPLIER = 2.0;

        /// <summary>
        /// Minimum height tolerance for boundary culling.
        /// </summary>
        public const double BOUNDARY_MIN_HEIGHT_TOL = 0.25;
    }

    /// <summary>
    /// Constants for Delaunay mesh generation.
    /// </summary>
    public static class MeshGeneration
    {
        /// <summary>
        /// Scale factor for the super triangle that encompasses all points.
        /// </summary>
        public const double SuperTriangleScale = 20.0;

        /// <summary>
        /// Maximum iterations for point location walk algorithm.
        /// </summary>
        public const int MaxWalkIterations = 5000;

        /// <summary>
        /// Epsilon value for geometric comparisons.
        /// </summary>
        public const double GeometricEpsilon = 1e-9;

        /// <summary>
        /// Epsilon for circumcircle containment tests.
        /// </summary>
        public const double CircumcircleEpsilon = 1e-10;
    }

    /// <summary>
    /// Constants for spatial hashing and point indexing.
    /// </summary>
    public static class SpatialIndexing
    {
        /// <summary>
        /// Multiplier for cell size relative to minimum distance.
        /// </summary>
        public const double CellSizeMultiplier = 4.0;

        /// <summary>
        /// Hash prime for X coordinate.
        /// </summary>
        public const long HashPrimeX = 73856093;

        /// <summary>
        /// Hash prime for Y coordinate.
        /// </summary>
        public const long HashPrimeY = 19349663;

        /// <summary>
        /// Hash prime for Z coordinate.
        /// </summary>
        public const long HashPrimeZ = 83492791;
    }

    /// <summary>
    /// Constants for scan density UI controls.
    /// </summary>
    public static class ScanDensity
    {
        /// <summary>
        /// Minimum probe cell size in meters.
        /// </summary>
        public const float MinProbeCell = 64f;

        /// <summary>
        /// Maximum probe cell size in meters.
        /// </summary>
        public const float MaxProbeCell = 768f;

        /// <summary>
        /// Minimum fine phase step in meters.
        /// </summary>
        public const float MinFineStep = 8f;

        /// <summary>
        /// Maximum fine phase step in meters.
        /// </summary>
        public const float MaxFineStep = 96f;

        /// <summary>
        /// Default fine phase step in meters.
        /// </summary>
        public const float DefaultFineStep = 24f;
    }
}
