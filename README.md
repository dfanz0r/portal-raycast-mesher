# Battlefield Portal Raycast Mesher

This tool is used to take raycast hit/misses from this script: https://gist.github.com/dfanz0r/598cfff02beafc92e241e76fb3206fe1

And turn them into a usable mesh (either obj or glb file) that can be imported into godot or whatever you might need.

The tool is intended to be used in a progressive detail increasing way. Because we are limited in memory usage at runtime we can only do ~9000 raycasts before a crash occurs. So this tool allows you to accumulate raycast points over multiple runs to gain one high detailed mesh of a FULL battlefield map.

One thing i've implemented in this version of this approach is storing all ray misses which allows us to re-cast the missed rays in the post processing step to remove any triangles that should not exist in the reconstructed output making the result more closely match the real maps.

Example output showing a run integrating a fresh set of raycast points:

```
=== BATTLEFIELD RAYCAST MESHER ===
[DB] Loaded: 160194 points, 55386 rays from terrain.db
[LOG] Found New Points: 8364
[LOG] Found New Misses: 0
[MERGE] Integrated 8364 unique points.
[LOG] Log file cleared successfully.
[MESH] Building Adaptive Mesh...
[MESH] Starting Global Delaunay Triangulation...
[MESH] Processing 168554 unique points.
................
[MESH] Generated 337086 triangles.
[MESH] Building Triangle Quadtree for acceleration...
[CARVE] Raycasting 55386 miss rays against the mesh...
[CARVE] Pruned 246 triangles intersecting empty space.
[MESH] Final Triangle Count: 336840
[DONE] Total Processing Time: 4.24s
[EXPORT] Generating GLB for 336840 triangles...
[EXPORT] Saved GLB to terrain_final.glb
```

Additionally there is a merge command:
`Usage: merge <pathA> <pathB> <pathOut>`

This is used for cases where 2 people might want to colaborate on filling out a map in high detail. It will merge 2 point db sets into a single higher detail one.

## How to use:

Start by using the portal script, and i prefer to start at a very wide scale with a very large step count. I typically would say start with 256 step centered on 0, 0 This will give you a very low detail picture of what the map looks like. It's probably a good idea to run this step a few times to accumulate some more points as it will randomly sample within that area so each time you will get new points increasing detail.

Once you find a region on the map you might want to build a portal experience on you can get those coorinates from godot and then do a high detail scan pass over that area so you have a much better resolution scan of the area you are working within. You can keep scanning with a smaller step (0.1 is a good size for highest detail) until you reach an acceptable quality/area reconstruction.
