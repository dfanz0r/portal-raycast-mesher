using System;
using System.Collections.Generic;
using MeshTool.Core.Data;

namespace MeshTool.Core.Services
{
    /// <summary>
    /// Mesh generator service interface for mesh generation from point clouds.
    /// </summary>
    public interface IMeshGenerator
    {
        /// <summary>
        /// Generates a mesh from the input points using Delaunay triangulation.
        /// </summary>
        /// <param name="inputPoints">The input points to generate the mesh from.</param>
        /// <param name="onProgress">Optional callback for progress updates during mesh generation.</param>
        /// <param name="isBusy">Optional callback to check if the operation should be cancelled.</param>
        /// <returns>List of triangles forming the mesh.</returns>
        List<Triangle> GenerateMesh(
            List<Vertex> inputPoints,
            Action<float[], int>? onProgress = null,
            Func<bool>? isBusy = null);

        /// <summary>
        /// Filters triangles with high aspect ratios.
        /// </summary>
        /// <param name="triangles">The triangles to filter.</param>
        /// <param name="maxAspectRatio">Maximum allowed aspect ratio.</param>
        /// <param name="removedCount">Output: number of triangles removed.</param>
        /// <returns>Filtered list of triangles.</returns>
        List<Triangle> FilterHighAspectRatioTriangles(
            List<Triangle> triangles,
            double maxAspectRatio,
            out int removedCount);

        /// <summary>
        /// Culls weak boundary triangles based on support from nearby points.
        /// </summary>
        /// <param name="triangles">The triangles to cull.</param>
        /// <param name="supportPoints">Points used to calculate triangle support.</param>
        /// <param name="edgeSpacingMultiplier">Multiplier for edge spacing threshold.</param>
        /// <param name="minNormalizedSupport">Minimum normalized support required.</param>
        /// <param name="heightTolMultiplier">Multiplier for height tolerance.</param>
        /// <param name="minHeightTol">Minimum height tolerance.</param>
        /// <param name="removedCount">Output: number of triangles removed.</param>
        /// <returns>Culled list of triangles.</returns>
        List<Triangle> CullWeakBoundaryTriangles(
            List<Triangle> triangles,
            List<Vertex> supportPoints,
            double edgeSpacingMultiplier,
            double minNormalizedSupport,
            double heightTolMultiplier,
            double minHeightTol,
            out int removedCount);
    }
}
