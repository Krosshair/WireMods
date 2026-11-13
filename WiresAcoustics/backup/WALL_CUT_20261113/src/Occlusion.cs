using HarmonyLib;
using UnityEngine;

namespace WiresAcoustics
{
    // Sounds behind walls, buildings or hills. Checked when a sound starts (the game's ZSFX.UpdateReverb),
    // and a looping one again whenever you or it move or a building changes (WatchLoop): one ray from the listener to the
    // sound, every solid thing in between is a layer. Each layer muffles (low-pass) and quietens it,
    // and in the room's reverb (ReverbSend). Also sets
    // each sound's reverb send from the measured space instead of distance alone.
    [HarmonyPatch(typeof(ZSFX), "UpdateReverb")]
    internal static class Occlusion
    {
        private static readonly RaycastHit[] Hits = new RaycastHit[MaxHits];
        internal const int MaxHits = 16; // hits recorded per ray
        // Soft layers (anything that isn't a wall, ground or a sized obstacle): no source, a judgement value.
        private static readonly float[] Gain = { 1f, .6f, .4f, .28f };
        private static readonly float[] Cutoff = { 22000f, 1500f, 800f, 500f };
        internal const float SourceClearance = .05f; // a ray stops just short of the sound (what makes it is left out by Counts)
        internal const float Twin = .08f; // the two rays beside each line: a seam between pieces is a centimetre or two
        private static int s_checked, s_muffled, s_added, s_loopChecks;
        private static float s_worstOcc;
        private static string s_worst;

        private static void Postfix(ZSFX __instance, AudioSource ___m_audioSource)
        {
            long t0 = Plugin.DebugLog.Value ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            Run(__instance, ___m_audioSource);
            if (t0 != 0L) Cost.Add(ref Cost.Sound, t0);
        }

        private static void Run(ZSFX __instance, AudioSource ___m_audioSource)
        {
            AudioSource src = ___m_audioSource;
            if (!Plugin.Enabled.Value || src == null) return;
            AcousticFilter f = __instance.GetComponent<AcousticFilter>();
            if (f != null && f.Cue) return; // a game cue: as the game made it
            if (Stackify.Active) Stackify.Restore(src); // a chest's sound from Wire's Stackify: a world sound at the chest
            if (Plugin.DebugLog.Value) Describe(__instance, src, f);
            // The game's random ambience (a bird, a gust, an insect) is put at a made-up spot round you
            // at your own height: indoors that's often inside your own room, a bird singing next to you
            // with nothing in the way. It's the world outside, so it's moved up into the open air
            // above that spot (InOpenAir) and then heard like any sound out there: through the walls by
            // the mass law, through the windows and doors from their direction. Without a place (the
            // game playing it flat) it's muffled the way the rain and wind are (Ambience).
            if (f != null && f.WorldRandomAmbient && src.spatialBlend < .5f)
            {
                Ambience.Apply(__instance.gameObject, !src.isPlaying);
                return;
            }
            if (f != null && f.WorldRandomAmbient)
            {
                Ambience.Forget(f);
                if (!src.isPlaying) InOpenAir(__instance.transform);
            }
            if (src.spatialBlend < .5f)
            {
                // Positionless ambience (birds, biome loops): muffled by how closed in you are.
                if (Ambience.IsAmbient(src)) { Ambience.Apply(__instance.gameObject, !src.isPlaying); return; }
                // a player's own unplaced sounds are placed at the player; anything else stays as it is
                if (!PlayerSounds.Place(__instance, src)) return;
            }
            AudioListener listener = AudioMan.instance != null ? AudioMan.instance.GetActiveAudioListener() : null;
            if (listener == null) return;

            Vector3 from = listener.transform.position, to = __instance.transform.position; // how far, what's in the way: from your head (the game's listener)
            Vector3 d = to - from;
            float dist = d.magnitude;
            Mist.Undo(__instance, src, dist); // no pitch drop in the mist

            // Reverb send, ours everywhere (vanilla raises it with distance only, /64 m). What tells you how far
            // a sound is, is how much of it is direct and how much is the room: near sounds are mostly
            // direct, past the room's critical distance mostly reverb. That distance grows with how
            // much the room swallows (furniture).
            bool reverb = Plugin.RoomReverb.Value && !src.bypassReverbZones;

            bool hrtf = Plugin.Hrtf.Value;
            bool under = Underwater.Muffles(to, __instance.transform);
            bool inWater = Underwater.ListenerBelow && Underwater.Below(to);
            bool starting = !src.isPlaying;
            // A sound starting now gets its direct ray at once and jumps to it (a frame late would let
            // its start through the wall). If walls are in the way, the paths round them (openings,
            // corners) go to the worker threads (Batch) and it glides open if one is better. A playing
            // loop is checked there entirely and glides to its value.
            float first = float.MaxValue;
            Collider firstCollider = null;
            if (f != null)
            {
                Area.Measure(f, __instance.transform);
                f.Stereo = src.clip != null && src.clip.channels > 1;
            }
            Own = f != null && f.ShapeRoot != null ? f.ShapeRoot.root : null;
            Block block = Block.Clear;
            float occ = starting && Plugin.Occlusion.Value ? Blocking(from, to, out first, out firstCollider, out block) : 0f;
            Own = null;
            s_checked++;
            if (occ > .05f) s_muffled++;
            if (occ <= .05f && !under && f == null && !hrtf && !AcousticFilter.GlobalActive && starting) return;
            if (f == null) { f = __instance.gameObject.AddComponent<AcousticFilter>(); s_added++; }
            f.Kind = AcousticFilter.Kinds.Sfx;
            f.Placed = true;
            f.InWater = inWater;
            // The game's random ambience (a bird, a gust, an insect somewhere round you) is placed at a
            // made-up spot 5 to 20 m off with its own gentle fall-off: it stands for the world around
            // you, not one creature, so it keeps that (on a real distance curve it was 15 to 20 dB
            // quieter at 10 to 20 m). Told apart by where the game plays it (AudioProvenance): the lava's
            // sounds use the same prefab.
            bool ambience = f.WorldRandomAmbient;
            f.NativeReverbLoop = __instance.m_distanceReverb;
            f.Physical(Plugin.RealDistance.Value && !f.Voice && !ambience);
            Budget.Note(f, dist); // processed now if it's among the nearest playing sounds
            float gain = 1f, cutoff = 22000f;
            if (starting)
            {
                f.StartedAt = Time.time;
                f.LastOcc = occ;
                f.LastBlockDist = first < float.MaxValue ? first : dist * .5f;
                f.LastBlocker = firstCollider;
                Wall(block, out gain, out cutoff);
                f.Routed = false;
                f.RoutePoint = to;
                if (under) { gain *= Underwater.Gain; cutoff = Mathf.Min(cutoff, Underwater.Cutoff); } // the sound is under water
                f.DistanceGain = f.PastRange(dist);
                f.AirCutoff = AcousticFilter.AirAt(dist);
                f.Set(gain, cutoff, true);
                if (occ >= .3f && f.Active) Batch.Enqueue(f, to, under, reverb);
            }
            // the game's once-a-second call for a playing loop: a watched loop is checked on its own (WatchLoop)
            else if (f.Active && !f.WatchLoop) Batch.Enqueue(f, to, under, reverb);
            // Direction is ours (AcousticFilter): Unity's panning is flattened to "both ears equally".
            if (hrtf)
            {
                f.TakePanning(BaseSpreadOf(__instance, src));
                Transform cam = listener.transform.parent != null ? listener.transform.parent : listener.transform; // which way: from the camera
                Vector3 hd = to - cam.position;
                f.SetDirection(hd.sqrMagnitude > 1e-4f ? cam.InverseTransformDirection(hd.normalized) : Vector3.forward);
            }
            else if (f.BaseSpread >= 0f) { src.spread = f.BaseSpread; f.BaseSpread = -1f; }
            f.Hrtf = hrtf;
            // what's muffled is quieter in the room's reverb too (ReverbSend)
            if (reverb) Send(f, ReverbSend(dist, starting ? gain : f.TargetGain));
            // Debug: the most muffled sound of each report (one line per report, not one per sound).
            if (Plugin.DebugLog.Value && occ > s_worstOcc)
            {
                s_worstOcc = occ;
                s_worst = __instance.name + " " + dist.ToString("F1") + " m, " + occ.ToString("F2") + " walls: gain " + gain.ToString("F2") + ", low-pass " + cutoff.ToString("F0") + " Hz";
            }
        }

        private static readonly RaycastHit[] OutHits = new RaycastHit[8];

        // How much of the room's reverb a sound gets: past the room's critical distance it's mostly
        // reverb (Room.Critical). Times what gets through to you: Unity's room reverb hears a sound
        // before its muffling, so a bird behind the wall rang through the room as bright as if it were
        // inside ("reverbed, not muffled"). What comes through a wall is quieter in the room too.
        internal static void Send(AcousticFilter f, float send)
        {
            AudioSource src = f.Source;
            if (src == null) return;
            if (f.OwnReverbMix < 0f) f.OwnReverbMix = src.reverbZoneMix; // the game's, put back when the mod's off
            src.reverbZoneMix = send;
        }

        internal static float ReverbSend(float dist, float through) => dist / (dist + Room.Critical) * Mathf.Clamp01(through);
        // The game puts a random call (a bird, a gull, an insect) at your own height, so in a basement
        // or on a hillside it was in the ground. A call comes from the open air: it keeps the game's
        // direction from you and sits on top of whatever is there (ground, rock, a treetop, a roof),
        // 1 m up (a bird on a branch). Not right over you: a call that would land on a building
        // within 10 m of you is moved further out, 4 m at a time, till it's past it or 10 m off (a
        // bird right above your bed sounded like it was in the room; pushed past a whole base, behind
        // five walls, it was gone). Distances by ear, no source.
        private const float CallHeight = 1f, CallStep = 4f, NotOver = 10f;
        private static void InOpenAir(Transform t)
        {
            Vector3 p = t.position, away = p - Hearing.At;
            away.y = 0f;
            away = away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.forward;
            Vector3 last = t.position;
            bool any = false;
            for (int step = 0; step < 6; step++, p += away * CallStep)
            {
                int n = Physics.RaycastNonAlloc(new Vector3(p.x, p.y + 150f, p.z), Vector3.down, OutHits, 300f, Room.Mask, QueryTriggerInteraction.Ignore);
                float highest = float.MinValue;
                Collider top = null;
                for (int i = 0; i < n; i++)
                    if (OutHits[i].collider != null && OutHits[i].point.y > highest) { highest = OutHits[i].point.y; top = OutHits[i].collider; }
                if (top == null) return;
                float sea = ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : highest;
                last = new Vector3(p.x, Mathf.Max(highest, sea) + CallHeight, p.z);
                any = true;
                Vector3 flat = p - Hearing.At; flat.y = 0f;
                if (flat.sqrMagnitude < NotOver * NotOver && top.GetComponentInParent<WearNTear>() != null) continue; // a building right by you: further out
                t.position = last;
                return;
            }
            if (any) t.position = last;
        }

        // How much is between the listener and a sound, in walls. A wall, floor, roof, beam, hill or
        // rock counts 1, furniture and props 0.25 (sound bends around them), a trunk 0.1, anything
        // else 0.3. The thing the sound comes from doesn't count (a chest, a workbench, a hearth:
        // anything whose box holds the sound's position, Counts).
        // Three lines, not one: the line itself and two beside it, 8 cm off sideways and up (Lines),
        // and what two of them agree on is what's in the way. Pieces snapped together often leave a
        // hairline gap between their colliders, and one ray that happened to run along it got through
        // a closed wall as if it were open; three rays 8 cm apart can't all find it. A real gap a
        // sound gets through (a door left ajar) is wider than that.
        internal static float Blocking(Vector3 from, Vector3 to, out float first, out Collider firstCollider, out Block block)
        {
            first = float.MaxValue; firstCollider = null; block = Block.Clear;
            if ((to - from).magnitude <= SourceClearance + .1f) return 0f;
            Lines(from, to, out Vector3 side, out Vector3 up);
            Block b0 = Block.Clear, b1 = Block.Clear, b2 = Block.Clear;
            float o0 = One(from, to, to, ref b0, out float f0, out Collider c0);
            float o1 = One(from + side, to + side, to, ref b1, out float f1, out Collider c1);
            float o2 = One(from + up, to + up, to, ref b2, out float f2, out Collider c2);
            int k = Median(b0, b1, b2);
            block = k == 0 ? b0 : k == 1 ? b1 : b2;
            first = k == 0 ? f0 : k == 1 ? f1 : f2;
            firstCollider = k == 0 ? c0 : k == 1 ? c1 : c2;
            return k == 0 ? o0 : k == 1 ? o1 : o2;
        }

        private static float One(Vector3 from, Vector3 end, Vector3 to, ref Block b, out float first, out Collider firstCollider)
        {
            Vector3 d = end - from;
            float dist = d.magnitude;
            int n = Physics.RaycastNonAlloc(from, d / dist, Hits, dist - SourceClearance, Room.Mask, QueryTriggerInteraction.Ignore);
            float occ = Along(Hits, n, from, to, ref b, out first, out firstCollider, n >= Hits.Length);
            if (b.Terrain) Ridge(from, end, ref b);
            return occ;
        }

        // The offsets of the two lines beside one from a to b: sideways and up, square to it.
        internal static void Lines(Vector3 a, Vector3 b, out Vector3 side, out Vector3 up)
        {
            Vector3 d = b - a;
            side = Vector3.Cross(Vector3.up, d);
            if (side.sqrMagnitude < 1e-8f) side = Vector3.Cross(Vector3.right, d);
            side = side.normalized;
            up = Vector3.Cross(d, side).normalized;
            side *= Twin; up *= Twin;
        }

        // Which of three lines lets the middle amount through (what two of them agree on).
        internal static int Median(in Block b0, in Block b1, in Block b2)
        {
            Wall(b0, out float g0, out _); Wall(b1, out float g1, out _); Wall(b2, out float g2, out _);
            if ((g0 >= g1 && g0 <= g2) || (g0 <= g1 && g0 >= g2)) return 0;
            if ((g1 >= g0 && g1 <= g2) || (g1 <= g0 && g1 >= g2)) return 1;
            return 2;
        }

        // The hits on one line, nearest first, as layers. A wall in Valheim is several colliders (the
        // wall, a beam, a pole) a few centimetres apart: one wall all the same, so anything built
        // within 0.6 m of the last wall counted is part of it.
        private const float SameWall = .6f;
        // Full: the ray had more hits than it could keep, and which ones Unity kept isn't defined, so
        // the wall among them may be one it dropped. A line through that much counts as through a wall
        // at least (as a plank wall, 15 dB).
        internal static float Along(RaycastHit[] hits, int n, Vector3 from, Vector3 to, ref Block b, out float first, out Collider firstCollider, bool full)
        {
            first = float.MaxValue; firstCollider = null;
            for (int i = 1; i < n; i++) // nearest first (Unity returns them in no order)
            {
                RaycastHit h = hits[i];
                int k = i - 1;
                while (k >= 0 && hits[k].distance > h.distance) { hits[k + 1] = hits[k]; k--; }
                hits[k + 1] = h;
            }
            b.LastWall = -10f;
            float occ = 0f;
            for (int i = 0; i < n; i++)
            {
                Collider c = hits[i].collider;
                if (c == null) continue;
                float w = Counts(c, to);
                if (w <= 0f) continue;
                if (!Layer(c, w, from, to, hits[i].distance, ref b)) continue;
                occ += w;
                if (hits[i].distance < first) { first = hits[i].distance; firstCollider = c; }
            }
            if (full && b.Walls == 0)
            {
                b.Walls = 1;
                b.Through *= Mathf.Pow(10f, -15f / 20f);
                b.ThroughCut = Mathf.Min(b.ThroughCut, WallCut);
                occ = Mathf.Max(occ, 1f);
            }
            return occ;
        }

        // How much one hit collider counts between you and a sound at "to". Not at all if it's part
        // of what makes the sound (Own: its shape, Area), or furniture, stations, props whose box
        // (+30 cm) holds it, or anything the sound is actually inside. A wall, floor or roof right
        // next to a sound (a wall torch, a sconce) still counts: from the other side it's behind it.
        internal static Transform Own;
        internal static float Counts(Collider c, Vector3 to)
        {
            if (Own != null && c.transform.root == Own) return 0f;
            Bounds b = c.bounds;
            b.Expand(.3f);
            if (!b.Contains(to)) return Weight(c);
            if (!Room.Structural(c)) return 0f;
            return Inside(c, to) ? 0f : Weight(c);
        }

        private static bool Inside(Collider c, Vector3 p)
        {
            // Closest point only exists for these (Unity): anything else just counts.
            bool ok = c is BoxCollider || c is SphereCollider || c is CapsuleCollider || (c is MeshCollider mc && mc.convex);
            if (!ok || !c.enabled || !c.gameObject.activeInHierarchy) return false;
            return (c.ClosestPoint(p) - p).sqrMagnitude < 1e-4f;
        }

        private static readonly System.Collections.Generic.Dictionary<int, float> Weights = new System.Collections.Generic.Dictionary<int, float>();
        private static float Weight(Collider c)
        {
            int id = c.GetInstanceID();
            if (Weights.TryGetValue(id, out float w)) return w;
            if (Weights.Count > 8192) Weights.Clear();
            WearNTear part = c.GetComponentInParent<WearNTear>();
            // How much a layer counts toward "something's in the way" (routing, the reverb boost, the
            // debug log). How much it lets through is LossOf's job, by material; here a wall is a wall.
            if (part != null && Room.Structural(c)) w = 1f;
            else if (part != null) w = .25f;
            else if (Room.IsTree(c)) w = .1f; // a trunk shadows a sound a little, it doesn't hide it
            else if (c.GetComponentInParent<Heightmap>() != null || c.gameObject.layer == LayerMask.NameToLayer("static_solid")) w = 1f;
            else w = .3f;
            Weights[id] = w;
            return w;
        }

        // Walls in between as gain and low-pass cutoff (strength applied), between the table's steps.
        // Debug: each sound once by name, as the game made it and how it's handled here.
        private static readonly System.Collections.Generic.HashSet<string> Described = new System.Collections.Generic.HashSet<string>();
        private static void Describe(ZSFX z, AudioSource src, AcousticFilter f)
        {
            string n = z.name.Replace("(Clone)", "");
            if (Described.Count > 2000 || !Described.Add(n)) return;
            if (f != null) Area.Measure(f, z.transform);
            string how = src.spatialBlend < .5f ? (Ambience.IsAmbient(src) ? "ambience (muffled by how enclosed you are)" : "unplaced, left as the game plays it") : "placed";
            string shape = f != null && f.AreaRadius > 0f ? ", shape " + (2f * f.AreaRadius).ToString("F1") + " m wide" : "";
            Plugin.Log.LogInfo("[acoustics] sound " + n + ": " + how + "; blend " + src.spatialBlend.ToString("F2") + ", " +
                (src.outputAudioMixerGroup != null ? src.outputAudioMixerGroup.name : "no mixer group") + ", " + src.rolloffMode + " " +
                src.minDistance.ToString("F0") + "-" + src.maxDistance.ToString("F0") + " m, spread " +
                src.spread.ToString("F0") + ", volume " + src.volume.ToString("F2") + shape);
        }

        // Walls, floors, roofs and doors as real ones measure. A wall's mass alone (the mass law) would
        // block far more than any real wall does: real ones leak through joints, gaps and the
        // structure, and the leaks carry the detail. So each is the closest construction that has been
        // measured, by its sound transmission class (STC), spread over frequency with the measured
        // average shape (Engineering ToolBox: -9 dB at 125 Hz, -5 at 250, -1 at 500, +3 at 1k, +7 at
        // 2k, +11 at 4k against the rating):
        //  - wood: bare cross-laminated timber walls, STC 33 at 78 mm and 38 at 175 mm thick
        //    (WoodWorks, Mass Timber Acoustics), by the piece's thickness between those, never past them;
        //  - doors: a solid-core wood door, STC 30 to 35 (Commercial Acoustics), the lower end;
        //  - stone and marble: 230 mm brickwork plastered both sides, STC 55 (Engineering ToolBox), the
        //    heaviest measured;
        //  - iron and ice: nothing measured found, so the mass law: TL = 20 lg(m f) - 48 (random
        //    incidence; Warnock, NRC Canadian Building Digest 239, 1985).
        // Those measured losses are for real walls between real rooms: through two or three of them a
        // sound was simply gone (a hearth in the next house at -120 dB). Here the first wall a sound
        // comes through takes 12 to 22 dB, by how heavy it is (15 for a plank wall, 22 for stone), and
        // each wall after it 6 dB more and duller, so the next room is muffled, not silent. A choice
        // for the game, not from a source; the measurements above only set which walls are heavier.
        // Each wall's low-pass takes the measured shape's extra loss from 125 Hz to 2 kHz.
        internal const float WallCut = 318.3f; // 2000 / sqrt(10^1.6 - 1): the measured shape's extra 16 dB from 125 Hz to 2 kHz
        private const float MassCut = 50f;
        internal static float MassLaw(float m) => m <= 0f ? 1f : Mathf.Min(1f, Mathf.Pow(10f, 48f / 20f) / MassCut / m);

        private struct Loss { internal float First, Cut; internal bool Wall; }
        private const float NextWall = .5f; // each wall after the first: 6 dB
        private static readonly System.Collections.Generic.Dictionary<int, Loss> Losses = new System.Collections.Generic.Dictionary<int, Loss>();
        private static Loss LossOf(Collider c)
        {
            int id = c.GetInstanceID();
            if (Losses.TryGetValue(id, out Loss l)) return l;
            if (Losses.Count > 8192) Losses.Clear();
            l = new Loss { First = 1f, Cut = 22000f };
            WearNTear part = c.GetComponentInParent<WearNTear>();
            if (part != null && Room.Structural(c) && part.name.IndexOf("grate", System.StringComparison.OrdinalIgnoreCase) < 0)
            {
                float stc = -1f;
                switch (part.m_materialType)
                {
                    case WearNTear.MaterialType.Stone:
                    case WearNTear.MaterialType.Ashstone:
                    case WearNTear.MaterialType.Marble: stc = 55f; break;
                    case WearNTear.MaterialType.Iron:
                    case WearNTear.MaterialType.Ice: break;
                    default:
                        if (c.GetComponentInParent<Door>() != null) stc = 30f;
                        else
                        {
                            float t = Thickness(c);
                            stc = t <= .078f ? 33f : t >= .175f ? 38f : Mathf.Lerp(33f, 38f, Mathf.Log(t / .078f) / Mathf.Log(.175f / .078f));
                        }
                        break;
                }
                // how heavy, as the measured loss at 125 Hz, into the game's 12 to 22 dB
                float lowDb = stc > 0f ? stc - 9f : -20f * Mathf.Log10(Mathf.Max(1e-6f, MassLaw(Mass(c))));
                float firstDb = Mathf.Clamp(15f + .5f * (lowDb - 24f), 12f, 22f);
                l = new Loss { First = Mathf.Pow(10f, -firstDb / 20f), Cut = WallCut, Wall = true };
            }
            Losses[id] = l;
            return l;
        }

        internal static bool IsWall(Collider c) => LossOf(c).Wall;
        // A wall's loss as the first one a sound comes through (amplitude), 0 if it isn't a wall.
        internal static float FirstWall(Collider c) { Loss l = LossOf(c); return l.Wall ? l.First : 0f; }

        // What one line from the listener to a sound runs through.
        internal struct Block
        {
            internal float Soft;      // rocks, furniture, trees: the old weights (sound bends round them)
            internal float Through;   // walls (amplitude, LossOf: the first one, then 6 dB each)
            internal int Walls;       // how many walls counted
            internal float LastWall;  // how far along the line the last one was (SameWall)
            internal float ThroughCut; // and the lowest of their low-passes
            internal bool Terrain;    // the ground is in the way: a hill, worked out over its ridge
            internal float BendGain, BendCut; // the ridge's loss (Ridge)
            internal static Block Clear => new Block { Through = 1f, ThroughCut = 22000f, BendGain = 1f, BendCut = 22000f, LastWall = -10f };
        }

        // One hit: a wall by what it measures, the ground marked for the ridge, anything else (a rock,
        // furniture, a trunk) as something the sound goes over and round (Diffraction.RoundObstacle).
        // False: part of the wall already counted.
        internal static bool Layer(Collider c, float w, Vector3 from, Vector3 to, float at, ref Block b)
        {
            Loss l = LossOf(c);
            if (l.Wall)
            {
                if (at - b.LastWall < SameWall) return false;
                b.LastWall = at;
                b.Walls++;
                b.Through *= b.Walls == 1 ? l.First : NextWall;
                b.ThroughCut = b.Walls == 1 ? Mathf.Min(b.ThroughCut, l.Cut) : Mathf.Max(200f, b.ThroughCut * .7f);
                return true;
            }
            else if (IsGround(c)) { if (!b.Terrain) { b.Terrain = true; b.Soft += w; } } // undone by Ridge
            else if (Diffraction.RoundObstacle(c.bounds, from, to, out float g, out float cut))
            {
                b.BendGain *= g;
                b.BendCut = Mathf.Min(b.BendCut, cut);
            }
            else b.Soft += w; // you or the sound inside its box: the old weight
            return true;
        }

        // A hill in the way: sound goes over it, not through, so it's a barrier with the ridge as its
        // edge (Diffraction). The ridge is the highest point of the ground under the line, from the
        // game's height map (16 samples, no rays). If none shows above the line, the hit stays soft.
        internal static void Ridge(Vector3 from, Vector3 to, ref Block b)
        {
            if (!b.Terrain) return;
            float best = 0f;
            Vector3 edge = default;
            for (int i = 1; i < 16; i++)
            {
                float t = i / 16f;
                Vector3 p = Vector3.Lerp(from, to, t);
                if (!Heightmap.GetHeight(p, out float h) || h <= p.y) continue;
                var e = new Vector3(p.x, h, p.z);
                float delta = (e - from).magnitude + (to - e).magnitude;
                if (delta > best) { best = delta; edge = e; }
            }
            if (best <= 0f) return;
            b.Soft = Mathf.Max(0f, b.Soft - 1f); // the ground's weight, replaced by the ridge
            Diffraction.Round(from, edge, to, out float g, out float c);
            b.BendGain *= g;
            b.BendCut = Mathf.Min(b.BendCut, c);
        }

        private static readonly System.Collections.Generic.Dictionary<int, bool> Grounds = new System.Collections.Generic.Dictionary<int, bool>();
        private static bool IsGround(Collider c)
        {
            int id = c.GetInstanceID();
            if (Grounds.TryGetValue(id, out bool g)) return g;
            if (Grounds.Count > 8192) Grounds.Clear();
            g = c.GetComponentInParent<Heightmap>() != null;
            Grounds[id] = g;
            return g;
        }

        // What's in the way as a gain and a low-pass (strength applied): soft layers by the table,
        // walls by what they measure, a hill by its ridge.
        internal static void Wall(in Block b, out float gain, out float cutoff)
        {
            Muffle(b.Soft, out gain, out cutoff);
            float s = Plugin.Occlusion.Value ? Mathf.Clamp(Plugin.OcclusionStrength.Value, 0f, 1.5f) : 0f;
            if (s <= 0f) return;
            float k = Mathf.Min(1f, s);
            if (b.Through < 1f)
            {
                gain *= Mathf.Pow(b.Through, s);
                cutoff = Mathf.Min(cutoff, Mathf.Exp(Mathf.Lerp(Mathf.Log(22000f), Mathf.Log(b.ThroughCut), k)));
            }
            if (b.BendGain < 1f)
            {
                gain *= Mathf.Pow(b.BendGain, s);
                cutoff = Mathf.Min(cutoff, Mathf.Exp(Mathf.Lerp(Mathf.Log(22000f), Mathf.Log(b.BendCut), k)));
            }
        }

        // kg per square metre of a building piece (iron and ice walls, the mass law): its material's
        // density times its thickness (the thinnest side of its collider).
        private static readonly System.Collections.Generic.Dictionary<int, float> Masses = new System.Collections.Generic.Dictionary<int, float>();
        internal static float Mass(Collider c)
        {
            int id = c.GetInstanceID();
            if (Masses.TryGetValue(id, out float m)) return m;
            if (Masses.Count > 8192) Masses.Clear();
            m = 0f;
            WearNTear part = c.GetComponentInParent<WearNTear>();
            if (part != null && Room.Structural(c) && part.name.IndexOf("grate", System.StringComparison.OrdinalIgnoreCase) < 0)
            {
                float density;
                switch (part.m_materialType)
                {
                    case WearNTear.MaterialType.Stone:
                    case WearNTear.MaterialType.Ashstone: density = 2400f; break; // stone masonry
                    case WearNTear.MaterialType.Marble: density = 2700f; break;
                    case WearNTear.MaterialType.Iron: density = 7870f; break;
                    case WearNTear.MaterialType.Ice: density = 917f; break;
                    case WearNTear.MaterialType.HardWood:
                    case WearNTear.MaterialType.Timberwood:
                    case WearNTear.MaterialType.Ancient: density = 700f; break; // hardwood
                    default: density = 500f; break; // softwood (pine, spruce)
                }
                m = density * Thickness(c);
            }
            Masses[id] = m;
            return m;
        }

        private static float Thickness(Collider c)
        {
            Vector3 scale = c.transform.lossyScale;
            scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            Vector3 size;
            if (c is BoxCollider box) size = Vector3.Scale(box.size, scale);
            else if (c is MeshCollider mc && mc.sharedMesh != null) size = Vector3.Scale(mc.sharedMesh.bounds.size, scale);
            else size = c.bounds.size;
            float t = Mathf.Min(size.x, Mathf.Min(size.y, size.z));
            return Mathf.Clamp(t, .02f, 2f);
        }

        internal static void Muffle(float occ, out float gain, out float cutoff)
        {
            float s = Plugin.Occlusion.Value ? Mathf.Clamp(Plugin.OcclusionStrength.Value, 0f, 1.5f) : 0f;
            float x = Mathf.Clamp(occ, 0f, Gain.Length - 1);
            int i = Mathf.Min((int)x, Gain.Length - 2);
            float t = x - i;
            float g = Mathf.Lerp(Gain[i], Gain[i + 1], t);
            float lc = Mathf.Lerp(Mathf.Log(Cutoff[i]), Mathf.Log(Cutoff[i + 1]), t);
            gain = Mathf.Clamp(Mathf.LerpUnclamped(1f, g, s), .05f, 1f);
            cutoff = Mathf.Clamp(Mathf.Exp(Mathf.LerpUnclamped(Mathf.Log(22000f), lc, s)), 200f, 22000f);
        }

        // Voice chat streams, 5 times a second: placed or global (its own switch), then a batched wall
        // check.
        internal static void Voices(Vector3 listener)
        {
            foreach (AcousticFilter f in AcousticFilter.Live)
            {
                if (f == null || !f.Voice || f.Source == null) continue;
                bool placed = f.Source.spatialBlend >= .5f;
                bool on = Plugin.Hrtf.Value && placed;
                f.Kind = AcousticFilter.Kinds.Voice;
                if (on != f.Hrtf)
                {
                    f.Hrtf = on;
                    if (on) f.TakePanning(f.BaseSpread >= 0f ? f.BaseSpread : f.Source.spread);
                    else if (f.BaseSpread >= 0f) f.Source.spread = f.BaseSpread;
                }
                f.Placed = placed;
                Budget.Note(f, (f.transform.position - listener).magnitude);
                if (!placed) { f.Set(1f, 22000f, false); f.AirCutoff = 22000f; continue; }
                f.InWater = Underwater.ListenerBelow && Underwater.Below(f.transform.position);
                Batch.Enqueue(f, f.transform.position, Underwater.Muffles(f.transform.position, f.transform), false);
            }
        }

        // The sound's own spread before the game widens it with distance (ZSFX.m_baseSpread).
        private static readonly HarmonyLib.AccessTools.FieldRef<ZSFX, float> ZsfxSpread = HarmonyLib.AccessTools.FieldRefAccess<ZSFX, float>("m_baseSpread");
        private static float BaseSpreadOf(ZSFX z, AudioSource src)
        {
            try { return ZsfxSpread(z); } catch { return src.spread; }
        }

        // Twice a second (Room's tick): one-off sounds still playing after 0.4 s (a long roar moving
        // behind a wall), other placed sources (Others) and the menu's are rechecked on worker threads.
        // Playing loops are only marked here (WatchLoop); their checks come from Directions when
        // something changed.
        // Other placed sources also get the HRTF here when they're playing (their filter comes from
        // the prefab, see Prefabs).
        internal static void Recheck(Vector3 listener)
        {
            bool occlusion = Plugin.Occlusion.Value, hrtf = Plugin.Hrtf.Value;
            float now = Time.time;
            foreach (AcousticFilter f in AcousticFilter.Live)
            {
                if (f == null) continue;
                f.WatchLoop = false;
                if (f.Source == null || f.Voice || f.Cue || f.Kind == AcousticFilter.Kinds.Ocean || f.Kind == AcousticFilter.Kinds.Ambient || f.Kind == AcousticFilter.Kinds.Outside) continue;
                AudioSource src = f.Source;
                if (!src.isPlaying || src.spatialBlend < .5f) continue;
                if (f.Kind == AcousticFilter.Kinds.Other && hrtf && !f.Hrtf) { f.TakePanning(src.spread); f.Hrtf = true; }
                Area.Measure(f, f.transform);
                f.Placed = true;
                // Biome-random positional ambience keeps its authored
                // rolloff; true lava/world SFX uses physical distance.
                f.Physical(Plugin.RealDistance.Value && !f.WorldRandomAmbient);
                Budget.Note(f, (f.transform.position - listener).magnitude);
                if (!f.Active) continue; // past the count: left to Unity until it's among the nearest
                if (!occlusion) continue;
                if (src.loop) { f.WatchLoop = true; continue; }
                if (f.Kind == AcousticFilter.Kinds.Sfx && now - f.StartedAt < .4f) continue;
                Vector3 at = f.transform.position;
                f.InWater = Underwater.ListenerBelow && Underwater.Below(at);
                Batch.Enqueue(f, at, Underwater.Muffles(at, f.transform), Plugin.RoomReverb.Value && !src.bypassReverbZones);
            }
        }

        // A playing loop is checked again when it could sound different: you or it moved half a metre,
        // a piece or door changed, or 2 s went by (anything the hooks miss: terrain, other mods). At
        // most every 0.1 s, so loading a base (thousands of pieces waking) doesn't check every frame.
        // The distances and times are judgement values, no source.
        // Every frame from Directions, which already has where you and it are: no work when nothing changed.
        internal static int WorldGen;
        internal static void WorldChanged() => WorldGen++;
        private const float LoopMoved = .5f, LoopMinGap = .1f, LoopFallback = 2f;

        internal static void WatchLoop(AcousticFilter f, Vector3 at, Vector3 ear, float now)
        {
            float since = now - f.CheckedTime;
            if (since < LoopMinGap || !f.Active) return; // past the count: left to Unity, as in Recheck
            if (since < LoopFallback && f.CheckedGen == WorldGen &&
                (ear - f.CheckedFrom).sqrMagnitude < LoopMoved * LoopMoved &&
                (at - f.CheckedAt).sqrMagnitude < LoopMoved * LoopMoved) return;
            AudioSource src = f.Source;
            if (src == null || !src.isPlaying) { f.WatchLoop = false; return; }
            Vector3 p = f.transform.position;
            f.InWater = Underwater.ListenerBelow && Underwater.Below(p);
            Batch.Enqueue(f, p, Underwater.Muffles(p, f.transform), Plugin.RoomReverb.Value && !src.bypassReverbZones);
            s_loopChecks++;
        }

        internal static string Summary()
        {
            string s = "sounds checked " + s_checked + ", muffled " + s_muffled + (s_worst != null ? " (most: " + s_worst + ")" : "") +
                ", filters added late " + s_added +
                ", loop checks " + s_loopChecks +
                "; " + AudioProvenance.Summary() + "; " + AcousticFilter.Format();
            s_checked = s_muffled = s_added = s_loopChecks = 0;
            s_worstOcc = 0f; s_worst = null;
            return s;
        }
    }
}
