using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
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

    // Application.streamingAssetsPath is a URL on WebGL (not a real filesystem path), so
    // System.IO.File can never read it there -- every load must go through UnityWebRequest.
    public static class RealZoneLoader
    {
        public static IEnumerator LoadManifestAsync(string fileName, Action<List<ZoneManifestEntry>> onComplete)
        {
            string path = System.IO.Path.Combine(Application.streamingAssetsPath, fileName);

            using (var req = UnityWebRequest.Get(path))
            {
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"[RealZoneLoader] Manifest not found: {path} ({req.error}). " +
                        "Zone-switching UI will be unavailable -- only the single " +
                        "default zone can be loaded. Copy zone_manifest.json (and the " +
                        "zone JSON files it references) into Assets/StreamingAssets/.");
                    onComplete(new List<ZoneManifestEntry>());
                    yield break;
                }

                var manifest = JsonUtility.FromJson<ZoneManifest>(req.downloadHandler.text);

                if (manifest == null || manifest.zones == null)
                {
                    Debug.LogError("[RealZoneLoader] Failed to parse zone_manifest.json.");
                    onComplete(new List<ZoneManifestEntry>());
                    yield break;
                }

                Debug.Log($"[RealZoneLoader] Loaded zone manifest -- {manifest.zones.Count} real zones available.");
                onComplete(manifest.zones);
            }
        }

        public static IEnumerator LoadAsync(string fileName, Action<ZoneData, RealZoneJson> onComplete)
        {
            string path = System.IO.Path.Combine(Application.streamingAssetsPath, fileName);

            using (var req = UnityWebRequest.Get(path))
            {
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[RealZoneLoader] File not found: {path} ({req.error}). " +
                        "Did you copy aria_zone.json into Assets/StreamingAssets/?");
                    onComplete(null, null);
                    yield break;
                }

                var meta = JsonUtility.FromJson<RealZoneJson>(req.downloadHandler.text);

                if (meta == null)
                {
                    Debug.LogError("[RealZoneLoader] Failed to parse aria_zone.json.");
                    onComplete(null, null);
                    yield break;
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

                onComplete(zone, meta);
            }
        }
    }
}
