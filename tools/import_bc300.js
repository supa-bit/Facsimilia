// One-off importer: projects real 300 BC political boundaries from
// aourednik/historical-basemaps (world_bc300.geojson) into the game's
// grid space, and writes the result as data the Godot project loads at
// runtime. Not part of the running game - a build-time tool, run with
// `node tools/import_bc300.js` whenever the source data or projection
// parameters change.
//
// Source: https://github.com/aourednik/historical-basemaps
// (geojson/world_bc300.geojson, downloaded to the scratchpad and copied
// into this repo's tools/ folder for reproducibility)

const fs = require("fs");
const path = require("path");

const GRID_WIDTH = 480;
const GRID_HEIGHT = 270;
const LON_MIN = -10, LON_MAX = 55;
const LAT_MIN = 10, LAT_MAX = 48;

function project(lon, lat) {
	const x = (lon - LON_MIN) / (LON_MAX - LON_MIN) * GRID_WIDTH;
	const y = (LAT_MAX - lat) / (LAT_MAX - LAT_MIN) * GRID_HEIGHT;
	return [x, y];
}

// civ_key -> real feature NAME(s) to union (matched exactly, trimmed)
const REGION_SOURCES = {
	rome: ["Roman Republic"],
	carthage: ["Carthaginian Empire"],
	egypt: ["Ptolemaic Kingdom"],
	kush: ["Meroe"],
	seleucid: ["Seleucid Kingdom"],
	greek_world: ["Kingdom of Kassander", "Greek city-states"],
	lysimachus: ["Kingdom of Lysimachus"],
	antigonus: ["Kingdom of Antigonus"],
	nabatea: ["Nabatean Kingdom"],
};

const REGION_DISPLAY_NAMES = {
	rome: "Rome",
	carthage: "Carthage",
	egypt: "Ptolemaic Egypt",
	kush: "Kingdom of Kush",
	seleucid: "Seleucid Empire",
	greek_world: "Greek World",
	lysimachus: "Kingdom of Lysimachus",
	antigonus: "Kingdom of Antigonus",
	nabatea: "Nabatean Kingdom",
};

function extractPolygons(feature) {
	// Only the outer ring of each polygon part - holes are ignored, a
	// reasonable simplification for these mostly-solid historical polities.
	const polys = [];
	const geom = feature.geometry;
	if (geom.type === "Polygon") {
		polys.push(geom.coordinates[0]);
	} else if (geom.type === "MultiPolygon") {
		for (const poly of geom.coordinates) {
			polys.push(poly[0]);
		}
	}
	return polys;
}

function main() {
	const srcPath = path.join(__dirname, "world_bc300.geojson");
	const data = JSON.parse(fs.readFileSync(srcPath, "utf8"));

	const regions = [];
	for (const [key, sourceNames] of Object.entries(REGION_SOURCES)) {
		const polygons = [];
		for (const name of sourceNames) {
			const feats = data.features.filter(f => (f.properties.NAME || "").trim() === name);
			if (feats.length === 0) {
				console.warn("WARNING: no feature found for", name);
			}
			for (const f of feats) {
				for (const ring of extractPolygons(f)) {
					const projected = ring.map(([lon, lat]) => project(lon, lat));
					polygons.push(projected);
				}
			}
		}
		regions.push({
			key,
			name: REGION_DISPLAY_NAMES[key],
			polygon_count: polygons.length,
			polygons,
		});
	}

	const out = {
		grid_width: GRID_WIDTH,
		grid_height: GRID_HEIGHT,
		projection: { lon_min: LON_MIN, lon_max: LON_MAX, lat_min: LAT_MIN, lat_max: LAT_MAX },
		regions,
	};

	const outPath = path.join(__dirname, "..", "data", "ancient_bc300.json");
	fs.mkdirSync(path.dirname(outPath), { recursive: true });
	fs.writeFileSync(outPath, JSON.stringify(out));

	console.log("Wrote", outPath);
	for (const r of regions) {
		const totalPoints = r.polygons.reduce((sum, p) => sum + p.length, 0);
		console.log(" ", r.key, ":", r.polygon_count, "polygon part(s),", totalPoints, "total points");
	}
}

main();
