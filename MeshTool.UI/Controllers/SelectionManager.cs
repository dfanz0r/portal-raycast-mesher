using System;
using System.Collections.Generic;
using MeshTool.Core.Data;

namespace MeshTool.UI.Controllers
{
    /// <summary>
    /// Manages point selection state and operations.
    /// Handles the working set of points during selection mode.
    /// </summary>
    public class SelectionManager
    {
        private readonly List<Vertex> _originalPoints = new();
        private readonly List<Vertex> _workingPoints = new();
        private bool _hasPendingChanges;

        /// <summary>
        /// Gets the number of points in the working set.
        /// </summary>
        public int PointCount => _workingPoints.Count;

        /// <summary>
        /// Gets whether there are unsaved changes.
        /// </summary>
        public bool HasPendingChanges => _hasPendingChanges;

        /// <summary>
        /// Gets the working points collection.
        /// </summary>
        public IReadOnlyList<Vertex> WorkingPoints => _workingPoints;

        /// <summary>
        /// Raised when the working set changes.
        /// </summary>
        public event Action? WorkingSetChanged;

        /// <summary>
        /// Begins a selection session with the given points.
        /// </summary>
        public void BeginSession(IReadOnlyList<Vertex> loadedPoints)
        {
            _originalPoints.Clear();
            _originalPoints.AddRange(loadedPoints);
            _workingPoints.Clear();
            _workingPoints.AddRange(loadedPoints);
            _hasPendingChanges = false;
        }

        /// <summary>
        /// Removes points at the specified indices from the working set.
        /// </summary>
        /// <param name="indices">The indices to remove.</param>
        /// <returns>The number of points removed.</returns>
        public int RemovePoints(ReadOnlySpan<int> indices)
        {
            if (indices.Length == 0)
                return 0;

            var removedSet = new HashSet<int>(indices.Length);
            foreach (int idx in indices)
            {
                removedSet.Add(idx);
            }

            var kept = new List<Vertex>(_workingPoints.Count);
            int removed = 0;

            for (int i = 0; i < _workingPoints.Count; i++)
            {
                if (removedSet.Contains(i))
                {
                    removed++;
                }
                else
                {
                    kept.Add(_workingPoints[i]);
                }
            }

            _workingPoints.Clear();
            _workingPoints.AddRange(kept);
            _hasPendingChanges = true;
            WorkingSetChanged?.Invoke();

            return removed;
        }

        /// <summary>
        /// Commits the working set as the new original.
        /// </summary>
        public void CommitChanges()
        {
            _originalPoints.Clear();
            _originalPoints.AddRange(_workingPoints);
            _hasPendingChanges = false;
        }

        /// <summary>
        /// Discards pending changes and reverts to the original set.
        /// </summary>
        public void DiscardChanges()
        {
            _workingPoints.Clear();
            _workingPoints.AddRange(_originalPoints);
            _hasPendingChanges = false;
            WorkingSetChanged?.Invoke();
        }

        /// <summary>
        /// Gets the working points as an array.
        /// </summary>
        public Vertex[] GetWorkingPointsArray()
        {
            return _workingPoints.ToArray();
        }

        /// <summary>
        /// Updates the loaded points collection to match the working set.
        /// </summary>
        public void SyncToLoadedPoints(List<Vertex> loadedPoints)
        {
            loadedPoints.Clear();
            loadedPoints.AddRange(_workingPoints);
        }

        /// <summary>
        /// Clears all selection state.
        /// </summary>
        public void Clear()
        {
            _originalPoints.Clear();
            _workingPoints.Clear();
            _hasPendingChanges = false;
        }
    }
}
