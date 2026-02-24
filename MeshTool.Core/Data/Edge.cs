namespace MeshTool.Core.Data
{
    /// <summary>
    /// Represents an edge between two vertices in a mesh structure.
    /// Used during Delaunay triangulation for cavity boundary representation.
    /// </summary>
    public struct Edge
    {
        /// <summary>
        /// The first vertex of the edge.
        /// </summary>
        public Vertex U;

        /// <summary>
        /// The second vertex of the edge.
        /// </summary>
        public Vertex V;

        /// <summary>
        /// The triangle adjacent to this edge (if any).
        /// </summary>
        public Triangle? Neighbor;

        /// <summary>
        /// The original triangle that generated this edge during cavity construction.
        /// </summary>
        public Triangle OldTri;
    }
}
