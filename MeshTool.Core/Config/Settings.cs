namespace MeshTool.Core.Config
{
    public static class Settings
    {
        // Merge distance for database accumulation (0.001 = 1mm precision)
        public const double MIN_MERGE_DISTANCE = 0.0001;

        // Discard very skinny triangles after meshing/carving.
        // Aspect ratio is defined as longest edge / shortest altitude.
        public const double MAX_TRIANGLE_ASPECT_RATIO = 40.0;

        // Boundary cleanup: iterative culling of weakly supported boundary triangles.
        public const double BOUNDARY_EDGE_SPACING_MULTIPLIER = 14.0;
        public const double BOUNDARY_MIN_NORMALIZED_SUPPORT = 0.02;
        public const double BOUNDARY_HEIGHT_TOL_MULTIPLIER = 2.0;
        public const double BOUNDARY_MIN_HEIGHT_TOL = 0.25;
    }
}
