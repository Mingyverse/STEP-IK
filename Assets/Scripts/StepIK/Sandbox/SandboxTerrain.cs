using UnityEngine;

namespace StepIK.Sandbox
{
    /// <summary>Deterministic root route shared by mesh construction and every comparison mode.</summary>
    public static class SandboxTerrain
    {
        public const float GridX = 0.25f;

        public static float LegacyHeight(int type, float x, float z)
        {
            if (type != 3) return HeightAt(type, x, z);
            return Mathf.Clamp01((z - 2f) / 2f) * Mathf.Clamp01((18f - z) / 2f) *
                (0.08f * Mathf.Sin(z * 2f) + 0.035f * Mathf.Sin(x * 2.5f + z * 1.3f));
        }

        public static float MeshHeight(int type, float x, float z)
        {
            if (type < 3) return HeightAt(type, x, z);
            float stepZ = type == 3 ? 0.1f : 0.25f;
            float originZ = type == 3 ? -1f : -12f;
            if (z < originZ || (type == 3 && z > 21f)) return 0f;
            float ix = (x + 30f) / GridX, iz = (z - originZ) / stepZ;
            float i = Mathf.Floor(ix), j = Mathf.Floor(iz);
            float sx = -30f + i * GridX, sz = originZ + j * stepZ;
            float u = ix - i, v = iz - j;
            float east = HeightAt(type, sx + GridX, sz);
            float north = HeightAt(type, sx, sz + stepZ);
            if (u + v <= 1f)
                return HeightAt(type, sx, sz) * (1f - u - v) + east * u + north * v;
            return HeightAt(type, sx + GridX, sz + stepZ) * (u + v - 1f) + east * (1f - v) + north * (1f - u);
        }

        public static float HeightAt(int type, float x, float z)
        {
            if (type == 1)
                return Mathf.Clamp01((z - 3f) / 4f) * 0.8f - Mathf.Clamp01((z - 11f) / 4f) * 0.8f;
            if (type == 2)
            {
                if (z < 3f || z >= 13f) return 0f;
                if (z < 6f) return (Mathf.Floor((z - 3f) / 0.5f) + 1f) * 0.12f;
                if (z < 10f) return 0.72f;
                return Mathf.Max(0f, (6f - Mathf.Floor((z - 10f) / 0.5f)) * 0.12f);
            }
            if (type == 3)
            {
                float fade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-1f, 2f, z)) *
                    Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(21f, 18f, z));
                return fade * BrokenGround(x, z, 431);
            }
            if (type == 4)
            {
                float hill = Mathf.Clamp01((z - 2f) / 4f) * Mathf.Clamp01((13f - z) / 5f);
                float rolling = Mathf.Clamp01((z - 9f) / 2f) * (0.13f * Mathf.Sin(z * 1.6f) + 0.06f * Mathf.Sin(x * 1.1f + z));
                float sides = Mathf.Clamp01((Mathf.Abs(x) - 4f) / 12f);
                return 0.85f * hill + rolling + sides * (0.9f + 0.7f * Mathf.Sin(x * 0.15f + z * 0.12f));
            }
            if (type == 5)
                return 0.9f * Noise(x * 0.3f, z * 0.26f, 732) + BrokenGround(x, z, 723) * 0.7f;
            if (type == 6)
                return 0.7f * Noise(x * 0.11f, z * 0.40f, 152) + 0.18f * Mathf.Sin(z * 1.8f + x * 0.5f) +
                    BrokenGround(x, z, 153) * 0.45f;
            if (type == 7)
            {
                // Asymmetric crossings through a shallow, winding meltwater channel.
                float channel = Mathf.Abs(x - FrostChannelX(z));
                float basin = 1f - Mathf.SmoothStep(0f, 1f, channel / 1.8f);
                float shelves = 0.22f * Mathf.Sin(z * 0.74f + x * 0.19f) + 0.32f * Noise(x * 0.35f, z * 0.28f, 870);
                return shelves + 0.08f * Noise(x * 1.2f, z * 0.91f, 896) - 0.40f * basin;
            }
            if (type == 8)
            {
                // A meandering trail with root-like berms crossing its sloping banks.
                float fromTrail = Mathf.Abs(x - MosswoodTrailX(z));
                float bank = Mathf.SmoothStep(0f, 1f, fromTrail / 5.5f);
                float roots = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(z * 2.1f + x * 0.62f)), 10f);
                return 0.19f * Mathf.Sin(z * 0.68f) + 0.22f * roots + bank * 0.38f +
                    (0.13f + 0.34f * bank) * Noise(x * 0.27f, z * 0.37f, 2107);
            }
            return 0f;
        }

        public static float FrostChannelX(float z) => 2.4f * Mathf.Sin(z * 0.27f) + 0.24f * z - 1.6f;
        public static float MosswoodTrailX(float z) => 2.3f * Mathf.Sin(z * 0.21f);

        private static float BrokenGround(float x, float z, int seed)
        {
            float height = 0.35f * Noise(x * 0.48f, z * 0.43f, seed) +
                0.13f * Noise(x * 1.45f, z * 1.25f, seed + 16);
            // One potential bowl per 2.8 m cell. Margins keep bowls away from cell boundaries.
            const float cell = 2.8f;
            int i = Mathf.FloorToInt(x / cell), j = Mathf.FloorToInt(z / cell);
            if (Hash(i, j, seed + 55) > 0.63f) return height;
            float cx = (i + 0.34f + 0.32f * Hash(i, j, seed + 30)) * cell;
            float cz = (j + 0.34f + 0.32f * Hash(i, j, seed + 31)) * cell;
            float radius = 0.43f + 0.36f * Hash(i, j, seed + 33);
            float dx = (x - cx) / radius, dz = (z - cz) / radius;
            float bowl = Mathf.Clamp01(1f - dx * dx - dz * dz);
            return height - bowl * bowl * (0.23f + 0.15f * Hash(i, j, seed + 34));
        }

        private static float Noise(float x, float z, int seed)
        {
            int i = Mathf.FloorToInt(x), j = Mathf.FloorToInt(z);
            float u = x - i, v = z - j;
            u = u * u * (3f - 2f * u);
            v = v * v * (3f - 2f * v);
            return 2f * Mathf.Lerp(Mathf.Lerp(Hash(i, j, seed), Hash(i + 1, j, seed), u),
                Mathf.Lerp(Hash(i, j + 1, seed), Hash(i + 1, j + 1, seed), u), v) - 1f;
        }

        private static float Hash(int x, int z, int seed)
        {
            unchecked
            {
                uint n = (uint)(x * 374761393 + z * 668265263 + seed * 1442695041);
                n = (n ^ (n >> 13)) * 1274126177u;
                return ((n ^ (n >> 16)) & 0xFFFFFF) / 16777215f;
            }
        }
    }
}
