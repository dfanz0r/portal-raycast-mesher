namespace TerrainTool.Data
{
    public struct Edge
    {
        public Vertex U, V;
        public Triangle? Neighbor;
        public Triangle OldTri; // The bad triangle that generated this edge
    }
}
