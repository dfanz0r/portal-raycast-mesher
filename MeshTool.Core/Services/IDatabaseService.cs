using System.Collections.Generic;
using MeshTool.Core.Data;

namespace MeshTool.Core.Services
{
    /// <summary>
    /// Database service interface for point cloud data persistence.
    /// </summary>
    public interface IDatabaseService
    {
        /// <summary>
        /// Saves points and rays to the specified path.
        /// </summary>
        void Save(string path, IReadOnlyList<Vertex> points, IReadOnlyList<Ray> rays);

        /// <summary>
        /// Loads points and rays from the specified path.
        /// </summary>
        void Load(string path, out List<Vertex> points, out List<Ray> rays);

        /// <summary>
        /// Checks if a database file exists at the specified path.
        /// </summary>
        bool Exists(string path);
    }
}
