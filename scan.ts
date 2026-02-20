// --- Settings ---
let MapCenterX = 0;
let MapCenterZ = 0;

let ytop = 4000;
let ybot = -400;

const QUAD_MIN_SIZE = 192; // Match initial step size
const INITIAL_PROBE_CELL_SIZE = 384;
const INITIAL_PROBE_RADIUS = 10000;
const QUAD_PROBE_COVERAGE = 0.8;
const QUAD_PROBE_OFFSETS: Array<[number, number]> = [
    [0, 0],
    [0, -1],
    [1, 0],
    [0, 1],
    [-1, 0],
    [-1, -1],
    [1, -1],
    [1, 1],
    [-1, 1]
];
const TARGET_STEP = 24;
const INITIAL_STEP = 192;
const TERRAIN_Y_EMA_ALPHA = 0.05;
const JITTER_MIN = 2;
const JITTER_MAX = 50;
const COVERAGE_RATIO = 0.5;

// --- State ---
enum Phase {
    QuadTreeSearch, // Recursive quadtree discovery
    AdaptiveScan,   // Streamed scan + refinement (DFS stack)
    Done
}

let phase = Phase.QuadTreeSearch;

// Generic Scan Cell
type ScanCell = { x: number; z: number; step: number };

// Quadtree State
type QuadNode = {
    x: number;      // Center X
    z: number;      // Center Z
    halfSize: number; // Half-width (radius)
    probeIndex: number;
};

let quadQueue: QuadNode[] = [];
let pendingQuadNode: QuadNode | null = null; // Node currently being raycast

// Streamed adaptive scan state
let scanStack: ScanCell[] = [];
let pendingScanCell: ScanCell | null = null;

// Queue
class RayParams {
    constructor(
        public sx: number, public sy: number, public sz: number,
        public ex: number, public ey: number, public ez: number,
        public phase: Phase
    ) {}
}
const queue: RayParams[] = [];

let done = false;
let beginScan = false;
let avgTerrainY = (ytop + ybot) * 0.5;
let terrainYSampleCount = 0;

function CheckAndLogCurrentMap(): void {
    if (mod.IsCurrentMap(mod.Maps.Abbasid)) { console.log("Current Map: Abbasid (Siege of Cairo)"); return; }
    if (mod.IsCurrentMap(mod.Maps.Aftermath)) { console.log("Current Map: Aftermath (Empire State)"); return; }
    if (mod.IsCurrentMap(mod.Maps.Badlands)) { console.log("Current Map: Badlands (Blackwell Fields)"); return; }
    if (mod.IsCurrentMap(mod.Maps.Battery)) { console.log("Current Map: Battery (Iberian Offsensive)"); return; }
    if (mod.IsCurrentMap(mod.Maps.Capstone)) { console.log("Current Map: Capstone (Liberation Peak)"); return; }
    // if (mod.IsCurrentMap(mod.Maps.Contaminated)) { console.log("Current Map: Contaminated (Contaminated)"); return; }
    if (mod.IsCurrentMap(mod.Maps.Dumbo)) { console.log("Current Map: Dumbo (Manhattan Bridge)"); return; }
    if (mod.IsCurrentMap(mod.Maps.Eastwood)) { console.log("Current Map: Eastwood (Eastwood)"); return; }
    if (mod.IsCurrentMap(mod.Maps.Firestorm)) { console.log("Current Map: Firestorm (Operation Firestorm)"); return; }
    if (mod.IsCurrentMap(mod.Maps.Limestone)) { console.log("Current Map: Limestone (Saints Quarter)"); return; }
    if (mod.IsCurrentMap(mod.Maps.Outskirts)) { console.log("Current Map: Outskirts (New Sobek City)"); return; }
    if (mod.IsCurrentMap(mod.Maps.Tungsten)) { console.log("Current Map: Tungsten (Mirak Valley)"); return; }
    if (mod.IsCurrentMap(mod.Maps.Granite_ClubHouse)) { console.log("Current Map: Granite_ClubHouse (Golf Course)"); return; }
    if (mod.IsCurrentMap(mod.Maps.Granite_TechCampus)) { console.log("Current Map: Granite_TechCampus (Defense Nexus)"); return; }
    if (mod.IsCurrentMap(mod.Maps.Granite_MainStreet)) { console.log("Current Map: Granite_MainStreet (Downtown)"); return; }
    if (mod.IsCurrentMap(mod.Maps.Granite_Marina)) { console.log("Current Map: Granite_Marina (Marina)"); return; }
    if (mod.IsCurrentMap(mod.Maps.Sand)) { console.log("Current Map: Sand (Portal Sandbox)"); return; }
    if (mod.IsCurrentMap(mod.Maps.Granite_MilitaryRnD)) { console.log("Current Map: Granite_MilitaryRnD (Area 22B)"); return; }
    if (mod.IsCurrentMap(mod.Maps.Granite_MilitaryStorage)) { console.log("Current Map: Granite_MilitaryStorage (Redline Storage)"); return; }
    console.log("Current Map: Unknown");
}

function addChildrenFromHit(parentX: number, parentZ: number, parentStep: number, output: ScanCell[]): void {
    const half = parentStep / 4;
    const childStep = parentStep / 2;
    output.push({ x: parentX - half, z: parentZ - half, step: childStep });
    output.push({ x: parentX + half, z: parentZ - half, step: childStep });
    output.push({ x: parentX - half, z: parentZ + half, step: childStep });
    output.push({ x: parentX + half, z: parentZ + half, step: childStep });
}

function getQuadProbePoint(node: QuadNode): { x: number; z: number } {
    const [ox, oz] = QUAD_PROBE_OFFSETS[node.probeIndex];
    const probeRadius = node.halfSize * QUAD_PROBE_COVERAGE;
    return {
        x: node.x + (ox * probeRadius),
        z: node.z + (oz * probeRadius)
    };
}

function getQuadProbeLimit(node: QuadNode): number {
    if (node.halfSize <= (INITIAL_PROBE_CELL_SIZE * 0.5)) {
        return 1;
    }

    return QUAD_PROBE_OFFSETS.length;
}

function fireQuadProbe(node: QuadNode): void {
    const probe = getQuadProbePoint(node);
    fireRay(probe.x, probe.z, Phase.QuadTreeSearch, node.halfSize * 2);
}

function pushAdaptiveChildren(parentX: number, parentZ: number, parentStep: number): void {
    addChildrenFromHit(parentX, parentZ, parentStep, scanStack);
}

function clamp(value: number, minValue: number, maxValue: number): number {
    return Math.max(minValue, Math.min(maxValue, value));
}

export function OnGameModeStarted(): void {
    beginScan = true;
    CheckAndLogCurrentMap();
    console.log("Starting scan (QuadTree)...");
    mod.PauseGameModeTime(true);

    const halfCell = INITIAL_PROBE_CELL_SIZE * 0.5;
    const minCenter = -INITIAL_PROBE_RADIUS + halfCell;
    const maxCenter = INITIAL_PROBE_RADIUS - halfCell;

    for (let x = minCenter; x <= maxCenter; x += INITIAL_PROBE_CELL_SIZE) {
        for (let z = minCenter; z <= maxCenter; z += INITIAL_PROBE_CELL_SIZE) {
            quadQueue.push({ x, z, halfSize: halfCell, probeIndex: 0 });
        }
    }

    console.log(`Quad seeds: ${quadQueue.length} (${INITIAL_PROBE_CELL_SIZE}x${INITIAL_PROBE_CELL_SIZE})`);
}

function fireRay(x: number, z: number, phase: Phase, step: number): void {
    let jitterBase = 50;
    let tiltBase = 100;

    // Strict vertical for QuadTree to accurately find bounds
    if (phase === Phase.QuadTreeSearch) { 
        tiltBase = 0; 
        jitterBase = step * 0.25; // Jitter position a bit, but keep ray axis-aligned (vertical)
    }

    if (phase === Phase.AdaptiveScan) {
        jitterBase = Math.max(JITTER_MIN, Math.min(JITTER_MAX, step * 0.35));
        tiltBase = Math.max(4, Math.min(100, step * 0.5));
    }

    const estimatedTerrainY = terrainYSampleCount > 0 ? avgTerrainY : (ytop + ybot) * 0.5;
    const ySpan = ybot - ytop;
    const t = ySpan !== 0 ? Math.max(0, Math.min(1, (estimatedTerrainY - ytop) / ySpan)) : 0.5;
    const expectedScale = (2 * t) - 1;
    const hitFactor = Math.abs(expectedScale);
    const segmentHalf = Math.max(step * 0.5, 1);
    const desiredHalfSpread = Math.max(JITTER_MIN * 0.5, step * COVERAGE_RATIO);

    let tilt = tiltBase;
    if (phase === Phase.AdaptiveScan && hitFactor > 0) {
        const maxTiltFromSegment = segmentHalf / hitFactor;
        tilt = Math.min(tiltBase, maxTiltFromSegment);
    }

    const jitterFromCoverage = 2 * Math.max(0, desiredHalfSpread - (hitFactor * tilt));
    const jitterFromSegment = 2 * Math.max(0, segmentHalf - (hitFactor * tilt));
    const jitter = Math.max(0, Math.min(jitterBase, jitterFromCoverage, jitterFromSegment));

    const jx = x + (Math.random() - 0.5) * jitter;
    const jz = z + (Math.random() - 0.5) * jitter;
    let tx = (Math.random() - 0.5) * 2 * tilt;
    let tz = (Math.random() - 0.5) * 2 * tilt;

    const minX = x - segmentHalf;
    const maxX = x + segmentHalf;
    const minZ = z - segmentHalf;
    const maxZ = z + segmentHalf;

    if (Math.abs(expectedScale) < 1e-6) {
        tx = 0;
        tz = 0;
    } else {
        let minTx = (minX - jx) / expectedScale;
        let maxTx = (maxX - jx) / expectedScale;
        if (minTx > maxTx) {
            const swap = minTx;
            minTx = maxTx;
            maxTx = swap;
        }
        tx = clamp(tx, minTx, maxTx);

        let minTz = (minZ - jz) / expectedScale;
        let maxTz = (maxZ - jz) / expectedScale;
        if (minTz > maxTz) {
            const swap = minTz;
            minTz = maxTz;
            maxTz = swap;
        }
        tz = clamp(tz, minTz, maxTz);
    }

    const p = new RayParams(jx - tx, ytop, jz - tz, jx + tx, ybot, jz + tz, phase);
    queue.push(p);
    mod.RayCast(mod.CreateVector(p.sx, p.sy, p.sz), mod.CreateVector(p.ex, p.ey, p.ez));
}

function startAdaptiveScan(): void {
    phase = Phase.AdaptiveScan;
    console.log(`Scan Phase: Adaptive (${scanStack.length} seed cells found via QuadTree)`);
}

export function OngoingGlobal(): void {
    if (!beginScan || done || queue.length > 0) return;

    switch (phase) {
        case Phase.QuadTreeSearch:
            if (pendingQuadNode) {
                fireQuadProbe(pendingQuadNode);
            } else if (quadQueue.length > 0) {
                pendingQuadNode = quadQueue.pop()!;
                fireQuadProbe(pendingQuadNode);
            } else {
                // QuadTree complete. We have populated the scan stack with valid leaves.
                startAdaptiveScan();
            }
            break;

        case Phase.AdaptiveScan:
            if (pendingScanCell) return; // Wait for ray result

            if (scanStack.length > 0) {
                pendingScanCell = scanStack.pop()!;
                fireRay(pendingScanCell.x, pendingScanCell.z, Phase.AdaptiveScan, pendingScanCell.step);
            } else {
                done = true;
                mod.AddUIText("Complete", mod.CreateVector(0,0,0), mod.CreateVector(200,100,100), mod.UIAnchor.Center, mod.Message("Complete!"));
                console.log("MAPPING COMPLETE");
                mod.EndGameMode(mod.GetTeam(0));
            }
            break;
    }
}

export function OnRayCastHit(eventPlayer: mod.Player, eventPoint: mod.Vector, eventNormal: mod.Vector): void {
    const p = queue.shift();
    if (!p) return;

    const px = mod.XComponentOf(eventPoint);
    const py = mod.YComponentOf(eventPoint);
    const pz = mod.ZComponentOf(eventPoint);
    const nx = mod.XComponentOf(eventNormal);
    const ny = mod.YComponentOf(eventNormal);
    const nz = mod.ZComponentOf(eventNormal);

    // EMA for height estimation
    if (terrainYSampleCount === 0) {
        avgTerrainY = py;
    } else {
        avgTerrainY += (py - avgTerrainY) * TERRAIN_Y_EMA_ALPHA;
    }
    terrainYSampleCount++;

    console.log(`HIT|P:${px},${py},${pz}|N:${nx},${ny},${nz}`);

    if (p.phase === Phase.QuadTreeSearch) {
        if (pendingQuadNode) {
            // Hit found in this quadrant!
            
            // If it's small enough, keep it as a valid spawn area
            if (pendingQuadNode.halfSize <= QUAD_MIN_SIZE) {
                // Leaf Node Reached - Seed adaptive stack
                scanStack.push({ x: pendingQuadNode.x, z: pendingQuadNode.z, step: pendingQuadNode.halfSize * 2 });
            } else {
                // Too big, check 4 corners to be thorough? 
                // No, "QuadTree Discovery" usually implies if PARENT hits, children MIGHT hit.
                // Wait, if PARENT (center) hits, we definitely keep it.
                // But if PARENT misses, we discard. This is risky for small islands.
                
                // For this map scale, let's just subdivide on HIT.
                const size = pendingQuadNode.halfSize;
                const quarter = size / 2;
                quadQueue.push({ x: pendingQuadNode.x - quarter, z: pendingQuadNode.z - quarter, halfSize: quarter, probeIndex: 0 });
                quadQueue.push({ x: pendingQuadNode.x + quarter, z: pendingQuadNode.z - quarter, halfSize: quarter, probeIndex: 0 });
                quadQueue.push({ x: pendingQuadNode.x - quarter, z: pendingQuadNode.z + quarter, halfSize: quarter, probeIndex: 0 });
                quadQueue.push({ x: pendingQuadNode.x + quarter, z: pendingQuadNode.z + quarter, halfSize: quarter, probeIndex: 0 });
            }
            pendingQuadNode = null;
        }
    } else if (p.phase === Phase.AdaptiveScan) {
        if (pendingScanCell && pendingScanCell.step > TARGET_STEP) {
            pushAdaptiveChildren(pendingScanCell.x, pendingScanCell.z, pendingScanCell.step);
        }
        pendingScanCell = null;
    }
}

export function OnRayCastMissed(eventPlayer: mod.Player): void {
    const p = queue.shift();
    if (!p) return;

    console.log(`MISS|S:${p.sx},${p.sy},${p.sz}|E:${p.ex},${p.ey},${p.ez}`);

    if (p.phase === Phase.QuadTreeSearch) {
        if (pendingQuadNode) {
            pendingQuadNode.probeIndex++;
            if (pendingQuadNode.probeIndex >= getQuadProbeLimit(pendingQuadNode)) {
                // Missed all probes -> Empty ocean, discard node
                pendingQuadNode = null;
            }
        }
    } else if (p.phase === Phase.AdaptiveScan) {
        pendingScanCell = null;
    }
}
