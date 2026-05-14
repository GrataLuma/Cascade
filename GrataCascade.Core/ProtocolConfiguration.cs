using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrataCascade.Core
{
    /// <summary>
    /// F9.b: Immutable, JSON-loadable protocol configuration.
    /// Replaces the hardcoded statics in <see cref="Configuration"/>. Instances are
    /// produced by <see cref="LoadFromJson"/> / <see cref="LoadFromJsonString"/>
    /// or the built-in <see cref="Reference"/>. <see cref="Configuration.LoadFrom"/>
    /// copies values into the legacy statics until the full DI refactor lands.
    ///
    /// Validation tiers (F9.a):
    ///  - HARD (<see cref="InvalidDataException"/>): schema_version, required fields,
    ///    rng.type, and the cut >= candidate consistency check.
    ///  - HARD numeric (<see cref="ArgumentOutOfRangeException"/>): update_probability
    ///    strict [0.6, 1.0] (F10 lower bound; F8 raised upper to 1.0 for crack-point
    ///    extremes — at p_upd=1.0 the operator MUST also relax candidate_max_probability_log2
    ///    closer to 0, otherwise the Stat distribution collapse starves all candidates);
    ///    vector_length [4, 64] (F8 lowered minimum for crack-point experiments at small
    ///    universes); all other structural ranges.
    ///  - SOFT numeric (<see cref="ArgumentOutOfRangeException"/>): cut_limit and
    ///    candidate_max log2 thresholds in [-256, 0]; optimum depends on L/N
    ///    (calibrated in F11).
    /// </summary>
    public sealed class ProtocolConfiguration
    {
        public const string ExpectedSchemaVersion = "1.0";

        public string SchemaVersion { get; }
        public string Name { get; }
        public string Description { get; }

        public int VectorLength { get; }
        public int VectorCount { get; }
        public int HashLengthAtoB { get; }
        public int HashLengthBtoA { get; }
        public int HashLengthPassword { get; }
        public int SeedMinMax { get; }
        public int AesPassedVectorsCount { get; }
        public double UpdateProbability { get; }
        public double TerminationThresholdLog2 { get; }
        public double CutLimitProbabilityLog2 { get; }
        public double CandidateMaxProbabilityLog2 { get; }
        public int CutLimitSafetyAttempts { get; }
        public int MaxIterations { get; }

        public string RngType { get; }
        public long? RngSeed { get; }

        /// <summary>Path the config was loaded from (or a sentinel for the built-in reference).</summary>
        public string OriginPath { get; }

        /// <summary>Full raw JSON text used to construct this config. Written verbatim into report headers for audit.</summary>
        public string OriginalJson { get; }

        /// <summary>Legacy bridge: protocol code branches on <c>rng &gt; NoUpdateLimit</c>. Derived as <c>1 - UpdateProbability</c>.</summary>
        public double NoUpdateLimit => 1.0 - UpdateProbability;

        /// <summary>Built-in reference matching the post-F4/F10 hardcoded defaults. Used when <c>--config</c> resolution fails to even find <c>configs/reference.json</c> AND the caller passes <see cref="Reference"/> explicitly; not used as an implicit fallback (F9.a §3).</summary>
        public static readonly ProtocolConfiguration Reference = BuildReference();

        private ProtocolConfiguration(
            string schemaVersion, string name, string description,
            int vectorLength, int vectorCount,
            int hAB, int hBA, int hPW,
            int seedMinMax, int aesPassedVectorsCount,
            double updateProbability,
            double terminationThresholdLog2,
            double cutLimitLog2, double candidateMaxLog2,
            int cutLimitSafetyAttempts,
            int maxIterations,
            string rngType, long? rngSeed,
            string originPath, string originalJson)
        {
            SchemaVersion = schemaVersion;
            Name = name;
            Description = description;
            VectorLength = vectorLength;
            VectorCount = vectorCount;
            HashLengthAtoB = hAB;
            HashLengthBtoA = hBA;
            HashLengthPassword = hPW;
            SeedMinMax = seedMinMax;
            AesPassedVectorsCount = aesPassedVectorsCount;
            UpdateProbability = updateProbability;
            TerminationThresholdLog2 = terminationThresholdLog2;
            CutLimitProbabilityLog2 = cutLimitLog2;
            CandidateMaxProbabilityLog2 = candidateMaxLog2;
            CutLimitSafetyAttempts = cutLimitSafetyAttempts;
            MaxIterations = maxIterations;
            RngType = rngType;
            RngSeed = rngSeed;
            OriginPath = originPath;
            OriginalJson = originalJson;
        }

        public static ProtocolConfiguration LoadFromJson(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("config path is null or empty", nameof(path));
            if (!File.Exists(path))
                throw new FileNotFoundException($"config file not found: {path}", path);

            string raw = File.ReadAllText(path);
            return LoadFromJsonString(raw, path);
        }

        public static ProtocolConfiguration LoadFromJsonString(string json, string originLabel)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));

            ConfigDto dto;
            try
            {
                var opts = new JsonSerializerOptions
                {
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    PropertyNameCaseInsensitive = false
                };
                dto = JsonSerializer.Deserialize<ConfigDto>(json, opts);
            }
            catch (JsonException jex)
            {
                throw new InvalidDataException($"config '{originLabel}': malformed JSON — {jex.Message}", jex);
            }

            if (dto == null)
                throw new InvalidDataException($"config '{originLabel}': empty or null JSON root");

            string err(string msg) => $"config '{originLabel}': {msg}";

            // ---- schema_version (HARD) ----
            if (string.IsNullOrWhiteSpace(dto.SchemaVersion))
                throw new InvalidDataException(err("missing required field 'schema_version'"));
            if (dto.SchemaVersion != ExpectedSchemaVersion)
                throw new InvalidDataException(err(
                    $"unsupported schema_version '{dto.SchemaVersion}', expected '{ExpectedSchemaVersion}'"));

            // ---- name (HARD) ----
            if (string.IsNullOrWhiteSpace(dto.Name))
                throw new InvalidDataException(err("missing required field 'name'"));

            // ---- protocol block (HARD) ----
            if (dto.Protocol == null)
                throw new InvalidDataException(err("missing required section 'protocol'"));
            var p = dto.Protocol;

            int vectorLength = RequireInt(p.VectorLength, "protocol.vector_length", originLabel);
            // Range lowered to [4, 64] so F8 crack-point experiments can target small
            // universes (|U| = 2^(8L)) where the structural foundational asymmetry
            // (N^2 / |U|) breaks down at moderate attacker pool sizes.
            RequireRange(vectorLength, 4, 64, "protocol.vector_length", originLabel);

            int vectorCount = RequireInt(p.VectorCount, "protocol.vector_count", originLabel);
            RequireRange(vectorCount, 64, 65536, "protocol.vector_count", originLabel);

            int hAB = RequireInt(p.HashLengthAtoB, "protocol.hash_length_ab", originLabel);
            RequireRange(hAB, 1, 64, "protocol.hash_length_ab", originLabel);

            int hBA = RequireInt(p.HashLengthBtoA, "protocol.hash_length_ba", originLabel);
            RequireRange(hBA, 1, 64, "protocol.hash_length_ba", originLabel);

            int hPW = RequireInt(p.HashLengthPassword, "protocol.hash_length_password", originLabel);
            RequireRange(hPW, 1, 64, "protocol.hash_length_password", originLabel);

            int seedMinMax = RequireInt(p.SeedMinMax, "protocol.seed_min_max", originLabel);
            // Range relaxed to [1, 64] so F12 can sweep up to s=30. The protocol
            // is structurally sound for any s >= 1; very large s just makes the
            // pool astronomically sparse.
            RequireRange(seedMinMax, 1, 64, "protocol.seed_min_max", originLabel);

            int aesCount = RequireInt(p.AesPassedVectorsCount, "protocol.aes_passed_vectors_count", originLabel);
            RequireRange(aesCount, 1, vectorCount, "protocol.aes_passed_vectors_count", originLabel);

            double uProb = RequireDouble(p.UpdateProbability, "protocol.update_probability", originLabel);
            // F10 lower bound (0.6) preserved; upper bound raised from 0.95 to 1.0 in F8
            // for crack-point experiments. At p_upd > 0.95 the Stat distribution collapses
            // (heavy concentration); the operator MUST also raise candidate_max_probability_log2
            // toward 0 to keep any candidates viable. Configs with p_upd > 0.95 + tight
            // candidate threshold WILL fail to converge — this is by design, validated in F8.
            if (uProb < 0.6 || uProb > 1.0)
                throw new ArgumentOutOfRangeException(
                    "protocol.update_probability",
                    uProb,
                    err("update_probability must be in the operational band [0.6, 1.0] (F10 lower; F8 upper)"));

            double termLog2 = RequireDouble(p.TerminationThresholdLog2, "protocol.termination_threshold_log2", originLabel);
            RequireRangeDouble(termLog2, -8192.0, 0.0, "protocol.termination_threshold_log2", originLabel);

            double cutLog2 = RequireDouble(p.CutLimitProbabilityLog2, "protocol.cut_limit_probability_log2", originLabel);
            RequireRangeDouble(cutLog2, -256.0, 0.0, "protocol.cut_limit_probability_log2", originLabel);

            double candLog2 = RequireDouble(p.CandidateMaxProbabilityLog2, "protocol.candidate_max_probability_log2", originLabel);
            RequireRangeDouble(candLog2, -256.0, 0.0, "protocol.candidate_max_probability_log2", originLabel);

            int cutSafety = RequireInt(p.CutLimitSafetyAttempts, "protocol.cut_limit_safety_attempts", originLabel);
            RequireRange(cutSafety, 1, 100000, "protocol.cut_limit_safety_attempts", originLabel);

            // Reference v2 (2026-05-02) introduces max_iterations as a JSON field. Older
            // configs (schema 1.0 written before the pivot) omit it; default to 500 with
            // a stderr warning so the operator notices. Range [1, 100000] mirrors the
            // safety-attempts cap.
            int maxIterations;
            if (p.MaxIterations.HasValue)
            {
                maxIterations = p.MaxIterations.Value;
                RequireRange(maxIterations, 1, 100000, "protocol.max_iterations", originLabel);
            }
            else
            {
                maxIterations = 500;
                Console.Error.WriteLine(
                    $"config '{originLabel}': max_iterations missing — defaulting to 500 (reference v2 baseline). " +
                    "Add an explicit \"max_iterations\" field to silence this warning.");
            }

            // Consistency: cut limit is a pool-fill filter (looser), candidate max is a
            // per-round selection filter (stricter). Inverting them starves the pool
            // without tightening the password candidate set. HARD per §F9.a review.
            if (cutLog2 < candLog2)
                throw new InvalidDataException(err(
                    $"inconsistent thresholds: cut_limit_probability_log2={cutLog2} must be >= candidate_max_probability_log2={candLog2}"));

            // ---- rng block (HARD) ----
            if (dto.Rng == null)
                throw new InvalidDataException(err("missing required section 'rng'"));
            if (string.IsNullOrWhiteSpace(dto.Rng.Type))
                throw new InvalidDataException(err("missing required field 'rng.type'"));
            if (dto.Rng.Type != "crypto")
                throw new InvalidDataException(err(
                    $"unsupported rng.type '{dto.Rng.Type}', only 'crypto' is supported in schema 1.0"));
            long? rngSeed = dto.Rng.Seed;
            if (rngSeed.HasValue && rngSeed.Value < 0)
                throw new ArgumentOutOfRangeException(
                    "rng.seed", rngSeed.Value, err("rng.seed must be null or non-negative"));

            string description = dto.Description ?? string.Empty;

            return new ProtocolConfiguration(
                dto.SchemaVersion, dto.Name, description,
                vectorLength, vectorCount, hAB, hBA, hPW,
                seedMinMax, aesCount,
                uProb, termLog2, cutLog2, candLog2, cutSafety,
                maxIterations,
                dto.Rng.Type, rngSeed,
                originLabel, json);
        }

        private static int RequireInt(int? v, string fieldName, string origin)
        {
            if (!v.HasValue)
                throw new InvalidDataException($"config '{origin}': missing required field '{fieldName}'");
            return v.Value;
        }

        private static double RequireDouble(double? v, string fieldName, string origin)
        {
            if (!v.HasValue)
                throw new InvalidDataException($"config '{origin}': missing required field '{fieldName}'");
            if (double.IsNaN(v.Value) || double.IsInfinity(v.Value))
                throw new InvalidDataException($"config '{origin}': field '{fieldName}' must be a finite number");
            return v.Value;
        }

        private static void RequireRange(int v, int lo, int hi, string fieldName, string origin)
        {
            if (v < lo || v > hi)
                throw new ArgumentOutOfRangeException(fieldName, v,
                    $"config '{origin}': '{fieldName}'={v} is out of range [{lo}, {hi}]");
        }

        private static void RequireRangeDouble(double v, double lo, double hi, string fieldName, string origin)
        {
            if (v < lo || v > hi)
                throw new ArgumentOutOfRangeException(fieldName, v,
                    $"config '{origin}': '{fieldName}'={v} is out of range [{lo}, {hi}]");
        }

        private static ProtocolConfiguration BuildReference()
        {
            // Reference v2 (2026-05-02 pivot). h_P=8 + M=8 raise lambda to 120 bits and
            // shrink Final-slot collision rate to ~2^-64. CandidateMax filter disabled by
            // default (-8 = same as cut limit) and surfaced as a configurable defense in
            // the paper. MaxIterations 500 is the natural ceiling (F2 v1 max iter was 181).
            const string refJson =
                "{\n" +
                "  \"schema_version\": \"1.0\",\n" +
                "  \"name\": \"reference\",\n" +
                "  \"description\": \"Built-in reference v2 (2026-05-02 pivot). h_P=8, M=8, s=4, CandidateMax disabled, MaxIter=500. Lambda=120 bits.\",\n" +
                "  \"protocol\": {\n" +
                "    \"vector_length\": 32,\n" +
                "    \"vector_count\": 4096,\n" +
                "    \"hash_length_ab\": 5,\n" +
                "    \"hash_length_ba\": 2,\n" +
                "    \"hash_length_password\": 8,\n" +
                "    \"seed_min_max\": 4,\n" +
                "    \"aes_passed_vectors_count\": 8,\n" +
                "    \"update_probability\": 0.75,\n" +
                "    \"termination_threshold_log2\": -512,\n" +
                "    \"cut_limit_probability_log2\": -8,\n" +
                "    \"candidate_max_probability_log2\": -8,\n" +
                "    \"cut_limit_safety_attempts\": 1000,\n" +
                "    \"max_iterations\": 500\n" +
                "  },\n" +
                "  \"rng\": { \"type\": \"crypto\", \"seed\": null }\n" +
                "}\n";
            return LoadFromJsonString(refJson, "<built-in reference>");
        }

        // ---- JSON DTOs ----
        private sealed class ConfigDto
        {
            [JsonPropertyName("schema_version")] public string SchemaVersion { get; set; }
            [JsonPropertyName("name")] public string Name { get; set; }
            [JsonPropertyName("description")] public string Description { get; set; }
            [JsonPropertyName("protocol")] public ProtocolDto Protocol { get; set; }
            [JsonPropertyName("rng")] public RngDto Rng { get; set; }
        }

        private sealed class ProtocolDto
        {
            [JsonPropertyName("vector_length")] public int? VectorLength { get; set; }
            [JsonPropertyName("vector_count")] public int? VectorCount { get; set; }
            [JsonPropertyName("hash_length_ab")] public int? HashLengthAtoB { get; set; }
            [JsonPropertyName("hash_length_ba")] public int? HashLengthBtoA { get; set; }
            [JsonPropertyName("hash_length_password")] public int? HashLengthPassword { get; set; }
            [JsonPropertyName("seed_min_max")] public int? SeedMinMax { get; set; }
            [JsonPropertyName("aes_passed_vectors_count")] public int? AesPassedVectorsCount { get; set; }
            [JsonPropertyName("update_probability")] public double? UpdateProbability { get; set; }
            [JsonPropertyName("termination_threshold_log2")] public double? TerminationThresholdLog2 { get; set; }
            [JsonPropertyName("cut_limit_probability_log2")] public double? CutLimitProbabilityLog2 { get; set; }
            [JsonPropertyName("candidate_max_probability_log2")] public double? CandidateMaxProbabilityLog2 { get; set; }
            [JsonPropertyName("cut_limit_safety_attempts")] public int? CutLimitSafetyAttempts { get; set; }
            [JsonPropertyName("max_iterations")] public int? MaxIterations { get; set; }
        }

        private sealed class RngDto
        {
            [JsonPropertyName("type")] public string Type { get; set; }
            [JsonPropertyName("seed")] public long? Seed { get; set; }
        }
    }
}
