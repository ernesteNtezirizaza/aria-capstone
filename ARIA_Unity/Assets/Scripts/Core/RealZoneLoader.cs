using System;
using System.Collections.Generic;
using UnityEngine;
using ARIA.Core;

namespace ARIA.Core
{
    [Serializable]
    public class RealZoneJson
    {
        public int size;
        public int nChannels;
        public string name;
        public string agroZone;
        public string split;
        public double boundsLeft;
        public double boundsRight;
        public double boundsTop;
        public double boundsBottom;
        public float meanSoil;
        public float noPlantPct;
        public float[] terrainFlat;
        public float[] distGridFlat;
        public float[] obsGridFlat;
        public bool[]  noPlantFlat;
    }

    [Serializable]
    public class ZoneManifestEntry
    {
        public int index;
        public string fileName;
        public string name;
        public string agroZone;
        public string split;
        public float meanSoil;
    }

    [Serializable]
    public class ZoneManifest
    {
        public List<ZoneManifestEntry> zones;
    }

    public static class RealZoneLoader
    {
        public static List<ZoneManifestEntry> LoadManifest(string fileName = "zone_manifest.json")
        {
            string path = System.IO.Path.Combine(Application.streamingAssetsPath, fileName);

            if (!System.IO.File.Exists(path))
            {
                Debug.LogWarning($"[RealZoneLoader] Manifest not found: {path}. " +
                    "Zone-switching UI will be unavailable -- only the single " +
                    "default zone can be loaded. Copy zone_manifest.json (and the " +
                    "zone JSON files it references) into Assets/StreamingAssets/.");
                return new List<ZoneManifestEntry>();
            }

            string json = System.IO.File.ReadAllText(path);
            var manifest = JsonUtility.FromJson<ZoneManifest>(json);

            if (manifest == null || manifest.zones == null)
            {
                Debug.LogError("[RealZoneLoader] Failed to parse zone_manifest.json.");
                return new List<ZoneManifestEntry>();
            }

            Debug.Log($"[RealZoneLoader] Loaded zone manifest -- {manifest.zones.Count} real zones available.");
            return manifest.zones;
        }

        public static ZoneData Load(out RealZoneJson meta, string fileName = "aria_zone.json")
        {
            string path = System.IO.Path.Combine(Application.streamingAssetsPath, fileName);

            string json;
            if (!System.IO.File.Exists(path))
            {
                Debug.LogError($"[RealZoneLoader] File not found: {path}. " +
                    "Did you copy aria_zone.json into Assets/StreamingAssets/?");
                meta = null;
                return null;
            }

            json = System.IO.File.ReadAllText(path);
            meta = JsonUtility.FromJson<RealZoneJson>(json);

            if (meta == null)
            {
                Debug.LogError("[RealZoneLoader] Failed to parse aria_zone.json.");
                return null;
            }

            int size = meta.size;
            int ch   = meta.nChannels;

            if (size != ARIAConstants.ZONE_SIZE)
            {
                Debug.LogWarning($"[RealZoneLoader] Zone size {size} does not match " +
                    $"ARIAConstants.ZONE_SIZE ({ARIAConstants.ZONE_SIZE}). " +
                    "Proceeding with the zone's actual size, but downstream code that " +
                    "assumes ZONE_SIZE everywhere may break.");
            }

            var zone = new ZoneData(size);

            // terrainFlat index = (y*size + x)*ch + c -- matches export_zone.py exactly
            int idx = 0;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    for (int c = 0; c < ch; c++)
                    {
                        zone.Terrain[y, x, c] = meta.terrainFlat[idx++];
                    }
                }
            }

            idx = 0;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    zone.DistGrid[y, x] = meta.distGridFlat[idx++];

            idx = 0;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    zone.ObsGrid[y, x] = meta.obsGridFlat[idx++];

            idx = 0;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    zone.NoPlant[y, x] = meta.noPlantFlat[idx++];

            Debug.Log($"[RealZoneLoader] Loaded real zone '{meta.name}' " +
                $"({meta.agroZone}, split={meta.split}) -- {size}x{size} cells.");

            return zone;
        }
    }
}
