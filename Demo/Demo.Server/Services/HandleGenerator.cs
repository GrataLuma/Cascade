using System;
using System.Collections.Generic;
using Demo.Server.Data;

namespace Demo.Server.Services
{
    // Generuje human-readable handles ve tvaru `color-animal`. Při kolizi
    // s already-taken setem retry až 50× s novým randomem. Pokud i tak
    // collision (extrémně nepravděpodobné při < 900 obsazených), přidá
    // numerický suffix `-2`, `-3`, … do nalezení volného.
    public sealed class HandleGenerator
    {
        private const int MaxRetries = 50;

        // Per-instance Random je OK pro low-throughput demo. Pro vysokou
        // soutěž thread-safe by stálo lock nebo per-thread.
        private readonly Random _rng = new();
        private readonly object _lock = new();

        public string Generate(IReadOnlySet<string> taken)
        {
            for (int i = 0; i < MaxRetries; i++)
            {
                var candidate = NextRandom();
                if (!taken.Contains(candidate)) return candidate;
            }

            // Fallback: enumerate suffix.
            var basePart = NextRandom();
            for (int n = 2; n < 1000; n++)
            {
                var withSuffix = $"{basePart}-{n}";
                if (!taken.Contains(withSuffix)) return withSuffix;
            }

            // Extrémní fallback (prakticky nedosažitelné při < 1000 conn):
            // GUID prefix.
            return $"{basePart}-{Guid.NewGuid().ToString("N")[..6]}";
        }

        private string NextRandom()
        {
            int colorIdx, animalIdx;
            lock (_lock)
            {
                colorIdx = _rng.Next(HandleWords.Colors.Length);
                animalIdx = _rng.Next(HandleWords.Animals.Length);
            }
            return $"{HandleWords.Colors[colorIdx]}-{HandleWords.Animals[animalIdx]}";
        }
    }
}
