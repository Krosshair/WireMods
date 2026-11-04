using System;
using UnityEngine;

namespace WiresEnemyVariation
{
    // Pure deterministic math: no Unity scene access, no frame callbacks.
    internal sealed class Individual
    {
        internal readonly uint Seed;
        internal readonly float[] Axis = new float[6]; // health damage attackSpeed move turn stagger
        internal readonly float[] Z = new float[6];
        internal readonly float[] Mind = new float[5]; // aggression courage tactics retaliation cohesion
        internal readonly float Size;
        internal readonly string Species;
        internal readonly int Stars;

        private static readonly float[] Caps = { .30f, .20f, .25f, .10f, .15f, .25f };

        internal Individual(uint seed, string species, int stars)
        {
            Seed = seed; Species = species; Stars = stars;
            var random = new Deterministic(seed);
            float[] raw = new float[6];
            float sum = 0;
            for (int i = 0; i < 6; i++) { raw[i] = random.Gaussian(); sum += raw[i]; }
            float mean = sum / 6f;
            const float coupling = .28f; // soft negative correlation: rare extraordinary combinations remain possible
            float divisor = Mathf.Sqrt(1f - (2f * coupling - coupling * coupling) / 6f);
            float starSpread = 1f + .2f * Mathf.Min(stars, 5); // starred enemies are more extreme
            for (int i = 0; i < 6; i++)
            {
                Z[i] = (raw[i] - coupling * mean) / divisor;
                Axis[i] = 1f + Caps[i] * (float)Math.Tanh(Z[i] * starSpread / 1.1f);
            }

            float[] psyche = new float[5];
            sum = 0f;
            for (int i = 0; i < 5; i++) { psyche[i] = random.Gaussian(); sum += psyche[i]; }
            mean = sum / 5f;
            const float psiCoupling = .42f;
            divisor = Mathf.Sqrt(1f - (2f * psiCoupling - psiCoupling * psiCoupling) / 5f);
            float physicalPower = .26f * Z[0] + .30f * Z[1] + .21f * Z[2] +
                                  .11f * Z[3] + .06f * Z[4] + .06f * Z[5];
            SpeciesMind(species, out float smarts, out float spread);
            for (int i = 0; i < 5; i++)
                Mind[i] = spread * (psyche[i] - psiCoupling * mean) / divisor;
            Mind[2] += smarts;
            // Soft skews, never hard compensation, never ban exceptional combinations.
            Mind[0] -= .27f * physicalPower + .15f * Z[0];
            Mind[1] += .10f * physicalPower + .55f * Mathf.Min(stars, 5); // stars bring confidence
            Mind[2] -= .15f * physicalPower;
            // Size is downstream ONLY; it never feeds back into traits or threat estimates.
            Size = 1f + .08f * (float)Math.Tanh(
                (.42f * Z[0] + .27f * Z[1] + .24f * Z[5] + .25f * random.Gaussian()) / 1.1f);
        }

        internal float Aggression => (float)Math.Tanh(Mind[0] / 1.8f);
        internal float Courage => (float)Math.Tanh(Mind[1] / 1.8f);
        internal float Tactics => (float)Math.Tanh(Mind[2] / 1.8f);
        internal float Retaliation => (float)Math.Tanh(Mind[3] / 1.8f);
        internal float Cohesion => (float)Math.Tanh(Mind[4] / 1.8f);

        // Species tactics bias and personality spread. Low spread = hive-like, everyone acts alike.
        private static readonly (string key, float smarts, float spread)[] Minds =
        {
            ("Jotun", 1.0f, 1.1f), ("Dverger", 1.0f, 1.1f), ("Cultist", .8f, 1f),
            ("Fuling", .5f, 1.1f), ("Troll", .5f, 1.15f), ("Charred", .4f, .55f),
            ("Fenring", .3f, 1f), ("Ulv", .3f, 1f), ("Wolf", .3f, 1f),
            ("Morgen", .2f, 1f), ("Draugr", .2f, 1f), ("Greydwarf", 0f, 1.1f),
            ("Asksvin", -.2f, 1f), ("Wraith", -.2f, 1f), ("Hatchling", -.3f, 1f),
            ("Lox", -.3f, 1f), ("Boar", -.4f, 1f), ("Serpent", -.4f, 1f),
            ("Bonemaw", -.4f, 1f), ("Golem", -.5f, .7f), ("Gjall", -.5f, .7f),
            ("Skeleton", -.6f, .5f), ("Greyling", -.6f, 1f), ("Neck", -.6f, 1f),
            ("Abomination", -.6f, .7f), ("Volture", -.6f, 1f), ("Seeker", -.8f, .45f),
            ("Surtling", -.8f, .5f), ("Tick", -1f, .4f), ("Deathsquito", -1f, .4f),
            ("Bat", -1f, .5f), ("Leech", -1f, .5f), ("Blob", -1.2f, .4f),
        };

        private static void SpeciesMind(string species, out float smarts, out float spread)
        {
            foreach (var m in Minds)
                if (species.IndexOf(m.key, StringComparison.OrdinalIgnoreCase) >= 0)
                { smarts = m.smarts; spread = m.spread; return; }
            smarts = 0f; spread = 1f;
        }

        internal float SpeciesGroupFactor
        {
            get
            {
                if (Species.Contains("Wolf")) return 1.35f;
                if (Species.Contains("Seeker")) return 1.30f;
                if (Species.Contains("Fuling")) return 1.12f;
                if (Species.Contains("Greydwarf")) return .82f;
                if (Species.Contains("Skeleton")) return .60f;
                if (Species.Contains("Troll")) return .60f;
                return 1f;
            }
        }

        internal float FearSensitivity
        {
            get
            {
                if (Species.Contains("Skeleton")) return .12f;
                if (Species.Contains("Seeker")) return .34f;
                if (Species.Contains("Troll")) return .68f;
                return 1f;
            }
        }

        internal float LossRageBias
        {
            get
            {
                if (Species.Contains("Skeleton")) return .10f;
                if (Species.Contains("Greydwarf")) return 1.20f;
                if (Species.Contains("Troll")) return 1.00f;
                if (Species.Contains("Wolf")) return .72f;
                if (Species.Contains("Seeker")) return .40f;
                return .80f;
            }
        }

        internal string Summary()
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "HP={0:+0.0%;-0.0%;0%} dmg={1:+0.0%;-0.0%;0%} atkSpeed={2:+0.0%;-0.0%;0%} move={3:+0.0%;-0.0%;0%} turn={4:+0.0%;-0.0%;0%} stagger={5:+0.0%;-0.0%;0%} size={6:+0.0%;-0.0%;0%} A={7:+0.00;-0.00;0} C={8:+0.00;-0.00;0} O={9:+0.00;-0.00;0} R={10:+0.00;-0.00;0} G={11:+0.00;-0.00;0}",
                Axis[0]-1f, Axis[1]-1f, Axis[2]-1f, Axis[3]-1f, Axis[4]-1f, Axis[5]-1f, Size-1f,
                Aggression, Courage, Tactics, Retaliation, Cohesion);
        }
    }

    internal struct Deterministic
    {
        private uint _state;
        internal Deterministic(uint seed) { _state = (seed == 0 ? 0x6D2B79F5u : seed); }
        private uint Next()
        {
            uint x = _state;
            x ^= x << 13; x ^= x >> 17; x ^= x << 5;
            return _state = x;
        }
        private double Unit() => ((double)Next() + .5) / 4294967296.0;
        internal float Gaussian()
        {
            double u = Unit();
            double v = Unit();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u)) * Math.Cos(6.28318530718 * v));
        }
    }
}