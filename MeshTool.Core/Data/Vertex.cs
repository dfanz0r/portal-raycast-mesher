namespace MeshTool.Core.Data
{
    /// <summary>
    /// Represents a vertex in 3D space with position, normal, and timing information.
    /// </summary>
    public class Vertex
    {
        /// <summary>
        /// Optional identifier for the vertex.
        /// </summary>
        public int ID;

        /// <summary>
        /// The 3D position of the vertex.
        /// </summary>
        public Vector3 Position;

        /// <summary>
        /// The surface normal at this vertex.
        /// </summary>
        public Vector3 Normal;

        /// <summary>
        /// The time when this vertex was created/spawned (for animation purposes).
        /// </summary>
        public float SpawnTime;
    }
}
