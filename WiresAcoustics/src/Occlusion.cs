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
        internal const int MaxHits = 16; // hits recorded per ray
        // Soft layers (anything that isn't a wall, ground or a sized obstacle): no source, a judgement value.
        private static readonly float[] Gain = { 1f, .6f, .4f, .28f };
        private static readonly float[] Cutoff = { 22000f, 1500f, 800f, 500f };
        internal const float SourceClearance = .05f; // a ray stops just short of the sound (what makes it is left out by Counts)
        internal const float Twin = .08f; // the two rays beside each line: a seam between pieces is a centimetre or two
        private static int s_checked, s_muffled, s_added, s_loopChecks;
        private static int s_openProxyHits, s_saturatedRayBuffers;
        private static float s_worstOcc;
        private static string s_worst;

        // A new sound's wall check came back (Starts): the rest of its start, with that answer.
        private static bool s_resume;
        private static float s_resumeOcc, s_resumeGain, s_resumeCutoff, s_resumeCoverage;
        private static bool s_resumeHasSpectrum;
        private static Block s_resumeBlock;
        internal static void Resume(ZSFX z, AudioSource src, float occ, Block block)
        {
            s_resume = true; s_resumeHasSpectrum = false;
            s_resumeOcc = occ; s_resumeBlock = block;
            try { Run(z, src); }
            finally { s_resume = false; s_resumeHasSpectrum = false; }
        }

        internal static void Resume(ZSFX z, AudioSource src, float occ, Block block,
                                    float gain, float cutoff, float coverage)
        {
            s_resume = true; s_resumeHasSpectrum = true;
            s_resumeOcc = occ; s_resumeBlock = block;
            s_resumeGain = gain; s_resumeCutoff = cutoff;
            s_resumeCoverage = coverage;
            try { Run(z, src); }
            finally { s_resume = false; s_resumeHasSpectrum = false; }
        }

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
                if (!src.isPlaying && !s_resume && Plugin.Placement.Value) { Starts.HoldAir(__instance, src); return; } // its open-air spot first (Starts)
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

            Vector3 from = listener.transform.position, to = __instance.transform.position; // how far: from your head (the game's listener); occlusion and distance use the acoustic listener
            Vector3 d = to - from;
            float dist = d.magnitude;
            Mist.Undo(__instance, src, dist); // no pitch drop in the mist

            // Reverb send, ours everywhere (vanilla raises it with distance only, /64 m). What tells you how far
            // a sound is, is how much of it is direct and how much is the room: near sounds are mostly
            // direct, past the room's critical distance mostly reverb. That distance grows with how
            // much the room swallows (furniture).
            bool reverb = Plugin.RoomReverb.Value && !src.bypassReverbZones;

            bool hrtf = Plugin.Hrtf.Value;
            bool starting = !src.isPlaying;
            // ZSFX/AudioSource instances may be reused. Never carry a cached
            // wall from an earlier sound incarnation into a new playback.
            if (starting && f != null)
            {
                f.Interacted = f.MadeByYou;
                if (!f.Interacted && Interaction.From(__instance.transform, f)) { f.Interacted = true; f.Touch = Interaction.Last; }
                // A pooled/restarted source needs current owner geometry
                // before its very first occlusion rays and HRTF width.
                f.AreaKnown = false;
                f.AnalyticActive = false;
                f.AnalyticPoseValid = false;
                f.AnalyticHasPortal = false;
                f.AnalyticWall = null;
                f.AnalyticIndirectGain = 0f;
            }
            // A sound starting now gets its direct ray at once and jumps to it (a frame late would let
            // its start through the wall). If walls are in the way, the paths round them (openings,
            // corners) go to the worker threads (Batch) and it glides open if one is better. A playing
            // loop is checked there entirely and glides to its value.
            if (f != null)
            {
                Area.Measure(f, __instance.transform);
                f.Stereo = src.clip != null && src.clip.channels > 1;
            }
            Own = f != null && f.ShapeRoot != null ? f.ShapeRoot.root : null;
            Block block = Block.Clear;
            // your own sounds (footsteps, swings, your gear): nothing stands between your head and
            // your own body, so no check (three rays each, at every step)
            float occ = 0f;
            if (s_resume) { occ = s_resumeOcc; block = s_resumeBlock; } // its check is in (Starts)
            else if (starting && Plugin.Occlusion.Value && !Mine(__instance.transform, f))
            {
                // what's in the way, from the camera (Hearing), on the worker threads; it starts next
                // frame with the answer. From the sound's own spot only: its own object never counts
                // (Counts), and what you're using is clear (Interaction).
                Own = null;
                Vector3 touched = default;
                bool aimed = f != null && f.Interacted && f.Touch.Now(out touched);
                Starts.Hold(__instance, src, aimed ? touched : to, aimed);
                return;
            }
            Own = null;
            s_checked++;
            if (occ > .05f) s_muffled++;
            if (occ <= .05f && f == null && !hrtf && starting) return;
            if (f == null) { f = __instance.gameObject.AddComponent<AcousticFilter>(); s_added++; }
            f.Kind = AcousticFilter.Kinds.Sfx;
            f.Placed = true;
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
                f.LastCoverage = s_resumeHasSpectrum ? s_resumeCoverage :
                                 (block.Walls > 0 || block.Terrain || block.Soft > 0f ? 1f : 0f);
                f.AcousticPath = f.LastCoverage <= 0f ? "start/clear" :
                                 f.LastCoverage < 1f ? "start/partial" : "start/through";
                f.AcousticRouteCount = 0;
                f.AcousticResolvedAt = Time.time;
                f.AcousticGeneration = WorldGen;
                f.Walls = Corners.From(block);
                if (s_resumeHasSpectrum) { gain = s_resumeGain; cutoff = s_resumeCutoff; }
                else Wall(block, out gain, out cutoff);
                f.Routed = false;
                f.RouteSpread = 0f;
                f.RoutePoint = to;
                f.DistanceGain = f.PastRange(dist);
                f.SetAir(dist);
                f.Set(gain, cutoff, true);
                if (f.Active && f.Walls.Near)
                    Batch.Enqueue(f, to, reverb);
            }
            // the game's once-a-second call for a playing loop: a watched loop is checked on its own (WatchLoop)
            else if (f.Active && !f.WatchLoop) Batch.Enqueue(f, to, reverb);
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
        internal const float CallHeight = 1f, CallStep = 4f, NotOver = 10f; // found on the worker threads (Starts.PlaceInOpenAir)

        // How much is between the listener and a sound, in walls. A wall, floor, roof, beam, hill or
        // rock counts 1, furniture and props 0.25 (sound bends around them), a trunk 0.1, anything
        // else 0.3. The thing the sound comes from doesn't count (a chest, a workbench, a hearth:
        // anything whose box holds the sound's position, Counts).
        // Three lines, not one: the line itself and two beside it, 8 cm off sideways and up (Lines),
        // and what two of them agree on is what's in the way. Pieces snapped together often leave a
        // hairline gap between their colliders, and one ray that happened to run along it got through
        // a closed wall as if it were open; three rays 8 cm apart can't all find it. A real gap a
        // sound gets through (a door left ajar) is wider than that.
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

        // The hits on one line, nearest first, as layers. A wall in Valheim is several colliders (the
        // wall, a beam, a pole) a few centimetres apart: one wall all the same, so anything built
        // within 0.6 m of the last wall counted is part of it.
        internal const float SameWall = .6f;
        // While the two legs of a way round an edge are read (Batch): the edge. The edge sits on the
        // wall's near face, and a leg leaving it steeply (you or the sound right by a half wall) would
        // clip the wall's own top or end and count the way over as going through it.
        internal static bool Round;
        internal static Vector3 RoundAt;
        // Full: the ray had more hits than it could keep, and which ones Unity kept isn't defined, so
        // the wall among them may be one it dropped. A line through that much counts as through a wall
        // at least (as a plank wall, 15 dB).
        internal static float Along(RaycastHit[] hits, int n, Vector3 from, Vector3 to, ref Block b, bool full)
        {
            for (int i = 1; i < n; i++) // nearest first (Unity returns them in no order)
            {
                RaycastHit h = hits[i];
                int k = i - 1;
                while (k >= 0 && hits[k].distance > h.distance) { hits[k + 1] = hits[k]; k--; }
                hits[k + 1] = h;
            }
            b.LastWall = -10f;
            float occ = 0f;
            s_groups = 0;
            for (int i = 0; i < n; i++)
            {
                // a way round an edge touches that edge: the wall it bends round isn't in its way
                if (Round && (hits[i].point - RoundAt).sqrMagnitude < SameWall * SameWall) continue;
                Solids.Info s = Solids.Of(hits[i]);
                if (s == null) continue;
                float w = Counts(s, to);
                if (w <= 0f) continue;
                if (!s.Wall && !s.Ground) { Group(s, w); continue; } // an obstacle: once per object, below
                if (!Layer(s, w, from, to, hits[i].distance, ref b)) continue;
                if (s.Wall)
                    b.FirstBox = b.Walls == 1 && s.C is BoxCollider ? s : null;
                if (s.Wall && !s.AcousticProxy)
                {
                    // the wall nearest each end, for the edges round them (Corners)
                    if (!b.HasWall) { b.HasWall = true; b.NearAt = hits[i].point; b.NearN = hits[i].normal; b.NearGain = s.First; }
                    b.FarAt = hits[i].point; b.FarN = hits[i].normal; b.FarGain = s.First;
                }
                occ += w;
            }
            // Obstacles (a rock, furniture, a trunk, a ship, a cart): the sound goes over and round
            // each object once, by the box of its colliders this line ran through. Bent once per
            // collider, a longship (dozens of them) took the loss over and over and shut a sound out.
            for (int g = 0; g < s_groups; g++)
            {
                if (Diffraction.RoundObstacle(GroupBox[g], from, to, out float gain, out float cut))
                {
                    b.BendGain *= gain;
                    b.BendHigh *= SpectralPaths.Response(cut);
                }
                else b.Soft += GroupW[g]; // you or the sound inside its box: the old weight
                occ += GroupW[g];
            }
            if (full && Plugin.DebugLog.Value) s_saturatedRayBuffers++;
            // A saturated physics hit buffer does not prove that an
            // additional acoustic wall exists. Do not fabricate a 15 dB
            // wall when the recorded hits are open-frame collider proxies.
            // The capacity limit is observable as a diagnostic, not a
            // material/transmission coefficient.
            return occ;
        }

        // How much one hit collider counts between you and a sound at "to". Not at all if it's part
        // of what makes the sound (Own: its shape, Area), or furniture, stations, props whose box
        // (+30 cm) holds it, or anything the sound is actually inside. A wall, floor or roof right
        // next to a sound (a wall torch, a sconce) still counts: from the other side it's behind it.
        internal static Transform Own;
        internal static float Counts(Solids.Info s, Vector3 to)
        {
            if (s.AcousticProxy && Plugin.DebugLog.Value) s_openProxyHits++;
            // Open panel is a physical composite surface, never entirely
            // absent; its open area and solid transmission are combined
            // by energy, rather than an all-clear/all-blocked switch.
            if (Own != null && s.Root == Own) return 0f;
            Bounds b = Solids.BoxOf(s);
            b.Expand(.3f);
            if (!b.Contains(to)) return s.Weight;
            if (!s.Structural) return 0f;
            return Inside(s.C, to) ? 0f : s.Weight;
        }

        private static bool Inside(Collider c, Vector3 p)
        {
            if (c == null) return false;
            // Closest point only exists for these (Unity): anything else just counts.
            bool ok = c is BoxCollider || c is SphereCollider || c is CapsuleCollider || (c is MeshCollider mc && mc.convex);
            if (!ok || !c.enabled || !c.gameObject.activeInHierarchy) return false;
            return (c.ClosestPoint(p) - p).sqrMagnitude < 1e-4f;
        }

        // What a collider is to a sound going through it, worked out once (Solids): how much it counts
        // toward "something's in the way" (routing, the reverb boost, the debug log; a wall is a wall
        // here, how much it lets through is the loss below, by material).
        internal static void Classify(Solids.Info s, WearNTear part, int layer)
        {
            if (part != null && s.Structural) s.Weight = 1f;
            else if (part != null) s.Weight = .25f;
            else if (s.Tree) s.Weight = .1f; // a trunk shadows a sound a little, it doesn't hide it
            else if (s.Ground || layer == Solids.StaticSolid) s.Weight = 1f;
            else s.Weight = .3f;
            Loss(s, part);
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
            string domain = f == null ? "untracked point" : f.LocalizedEvent ? "localized point" :
                f.WeaponSource ? "visual weapon" : f.FireLoop ? "fire/flame" :
                f.AreaRadius > 0f ? "volume" : "point";
            string owner = f != null && f.ShapeRoot != null ? f.ShapeRoot.name : "none";
            Plugin.Log.LogInfo("[acoustics] sound " + n + ": " + how +
                "; emission " + domain + ", owner " + owner +
                "; blend " + src.spatialBlend.ToString("F2") + ", " +
                (src.outputAudioMixerGroup != null ? src.outputAudioMixerGroup.name : "no mixer group") + ", " + src.rolloffMode + " " +
                src.minDistance.ToString("F0") + "-" + src.maxDistance.ToString("F0") + " m, spread " +
                src.spread.ToString("F0") + ", volume " + src.volume.ToString("F2") + shape);
        }

        // Ambient-shelter low-pass remains separate from material transmission.
        internal const float WallCut = 551.7f;

        // Steam Audio C API IPLMaterial preset transmission ENERGY fractions.
        // WA's existing two-band filter uses low-frequency and mid-band
        // (~2 kHz) values; it cannot represent Steam Audio's third band.
        // Valheim material types map to the published Steam Audio presets.
        internal static void TransmissionEnergy(WearNTear.MaterialType material,
            out float low, out float mid)
        {
            switch (material)
            {
                case WearNTear.MaterialType.Stone:
                case WearNTear.MaterialType.Ashstone:
                case WearNTear.MaterialType.Marble:
                    low = .015f; mid = .002f; return; // rock
                case WearNTear.MaterialType.Iron:
                    low = .200f; mid = .025f; return; // metal
                case WearNTear.MaterialType.Ice:
                    low = .060f; mid = .044f; return; // glass proxy
                case WearNTear.MaterialType.Wood:
                case WearNTear.MaterialType.HardWood:
                case WearNTear.MaterialType.Timberwood:
                case WearNTear.MaterialType.Ancient:
                    low = .070f; mid = .014f; return; // wood
                default:
                    low = .100f; mid = .050f; return; // Steam Audio generic
            }
        }

        // Material transmission per independent wall. Input fractions are
        // ENERGY, not amplitude. SpectralPaths.Decode fits their low/mid
        // ratio to the existing two-pole digital filter.
        private static void Loss(Solids.Info s, WearNTear part)
        {
            if (part == null || !s.Structural) return;
            TransmissionEnergy(part.m_materialType, out float low, out float mid);
            if (s.AcousticProxy)
            {
                // ASHRAE area-weighted composite transmission by ENERGY.
                // Free aperture fraction phi has unit transmission.
                float phi = Mathf.Clamp01(s.OpenFraction);
                low = phi + (1f - phi) * low;
                mid = phi + (1f - phi) * mid;
            }
            SpectralPaths.Decode(low, mid, out s.First, out s.Cut);
            s.Wall = true;
        }
        // What one line from the listener to a sound runs through.
        internal struct Block
        {
            internal float Soft;      // rocks, furniture, trees: the old weights (sound bends round them)
            internal float Through;   // product of Steam Audio material transmission amplitudes
            internal int Walls;       // how many walls counted
            internal float LastWall;  // how far along the line the last one was (SameWall)
            internal float ThroughHigh; // product of per-layer 2-kHz intensity transfers
            internal bool Terrain;    // the ground is in the way: a hill, worked out over its ridge
            internal float BendGain, BendHigh; // bend gain and 2-kHz intensity response
            internal bool HasWall;    // where the first and last walls were hit, and how much each takes
            internal Vector3 NearAt, NearN, FarAt, FarN;
            internal Solids.Info FirstBox; // measured single planar wall collider for analytic source coverage
            internal float NearGain, FarGain;
            internal static Block Clear => new Block { Through = 1f, ThroughHigh = 1f, BendGain = 1f, BendHigh = 1f, LastWall = -10f };
        }

        // One hit: a wall by what it measures, the ground marked for the ridge. False: part of the
        // wall already counted. Anything else is an obstacle (Group).
        internal static bool Layer(Solids.Info s, float w, Vector3 from, Vector3 to, float at, ref Block b)
        {
            if (s.Wall)
            {
                if (at - b.LastWall < SameWall) return false;
                b.LastWall = at;
                b.Walls++;
                b.Through *= s.First;
                // Serial wall transfers multiply at 2 kHz. Keep the
                // high-band energy, NOT an approximate cutoff per layer.
                float layerCut = s.Cut;
                b.ThroughHigh *= SpectralPaths.Response(layerCut);
                return true;
            }
            if (s.Ground && !b.Terrain) { b.Terrain = true; b.Soft += w; } // undone by Ridge
            return true;
        }

        // The obstacles on one line, by object (what each collider belongs to): its colliders' boxes
        // together, and its weight once. A line holds at most MaxHits hits.
        private static int s_groups;
        private static readonly Transform[] GroupRoot = new Transform[MaxHits];
        private static readonly Bounds[] GroupBox = new Bounds[MaxHits];
        private static readonly float[] GroupW = new float[MaxHits];
        private static void Group(Solids.Info s, float w)
        {
            Bounds box = Solids.BoxOf(s);
            for (int g = 0; g < s_groups; g++)
                if (GroupRoot[g] == s.Root)
                {
                    GroupBox[g].Encapsulate(box);
                    GroupW[g] = Mathf.Max(GroupW[g], w);
                    return;
                }
            if (s_groups >= MaxHits) return;
            GroupRoot[s_groups] = s.Root;
            GroupBox[s_groups] = box;
            GroupW[s_groups] = w;
            s_groups++;
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
                // Heightmap chunks can be transiently invalid while terrain
                // loads/unloads. Never let one missing height sample abort
                // the whole audio update. Retain the original soft-ground
                // transmission if the ridge cannot be measured completely.
                float h;
                try
                {
                    if (!Heightmap.GetHeight(p, out h) || h <= p.y) continue;
                }
                catch (System.ArgumentOutOfRangeException)
                {
                    return;
                }
                var e = new Vector3(p.x, h, p.z);
                float delta = (e - from).magnitude + (to - e).magnitude;
                if (delta > best) { best = delta; edge = e; }
            }
            if (best <= 0f) return;
            b.Soft = Mathf.Max(0f, b.Soft - 1f); // the ground's weight, replaced by the ridge
            Diffraction.Round(from, edge, to, out float g, out float c);
            b.BendGain *= g;
            b.BendHigh *= SpectralPaths.Response(c);
        }


        // What's in the way as a gain and a low-pass (strength applied): soft layers by the table,
        // walls by what they measure, a hill by its ridge.
        internal static void Wall(in Block b, out float gain, out float cutoff)
        {
            Muffle(b.Soft, out gain, out cutoff);
            float s = AcousticMath.OcclusionStrength;
            if (s <= 0f || (b.Through >= 1f && b.BendGain >= 1f)) return;
            // Geometry sampling accumulates the low-frequency amplitude
            // and 2-kHz energy independently. Applying strength as a
            // logarithmic attenuation exponent scales BOTH bands
            // consistently. Fit to the actual DSP ONCE per sound route.
            float dc = gain * gain;
            float high = SpectralPaths.HighEnergy(gain, cutoff);
            if (b.Through < 1f)
            {
                float g = Mathf.Pow(b.Through, s);
                float energy = g * g;
                dc *= energy;
                high *= energy * Mathf.Pow(b.ThroughHigh, s);
            }
            if (b.BendGain < 1f)
            {
                float g = Mathf.Pow(b.BendGain, s);
                float energy = g * g;
                dc *= energy;
                high *= energy * Mathf.Pow(b.BendHigh, s);
            }
            SpectralPaths.Decode(dc, high, out gain, out cutoff);
        }

        internal static void Muffle(float occ, out float gain, out float cutoff)
        {
            float s = AcousticMath.OcclusionStrength;
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
                if (!Plugin.VoiceChatOn.Value) { f.Hrtf = false; f.Placed = false; f.Set(1f, 22000f, false); f.ClearAir(); continue; } // as the voice chat plays it
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
                if (!placed) { f.Set(1f, 22000f, false); f.ClearAir(); continue; }
                Batch.Enqueue(f, f.transform.position, false);
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
                // a longer one-off (a roar): checked again only when it could sound different, as loops
                if (Unchanged(f, at, Hearing.At)) continue;
                Batch.Enqueue(f, at, Plugin.RoomReverb.Value && !src.bypassReverbZones);
            }
        }

        // A playing loop is checked when the listener/source moves or geometry changes.
        // No elapsed-time fallback: remaining idle must not schedule raycast jobs.
        // Minimum intervals only limit bursts of actual movement/events.
        // The distances and times are judgement values, no source.
        // Every frame from Directions, which already has where you and it are: no work when nothing changed.
        // A building change only counts for a loop if it's within Corners.Reach of the line from you to
        // it (the furthest a way round is looked for): the game loads and unloads far-off pieces all
        // the time as you move, and each of those used to recheck every loop.
        internal static int WorldGen, DoorGen;
        private const int Kept = 64;
        private static readonly Vector3[] ChangeAt = new Vector3[Kept];
        private static readonly bool[] ChangeAll = new bool[Kept];

        // A piece built or removed, or a door turned, at "at"; everywhere at once when a base has
        // just loaded in (GeometryEvents).
        internal static void WorldChanged(Vector3 at, bool everywhere, bool door)
        {
            WorldGen++;
            ChangeAt[WorldGen % Kept] = at;
            ChangeAll[WorldGen % Kept] = everywhere;
            if (door || everywhere) DoorGen++;
            AcousticTrace.RecordGeometry(at, everywhere, door, WorldGen);
            // Existing audible loops should not wait for their half-metre
            // movement threshold or the periodic safety recheck after a
            // piece is created, removed, or a door changes.
            Batch.GeometryChanged(at, everywhere);
        }

        // Anything changed since "since" within "margin" of the line from a to b.
        internal static bool NearLine(Vector3 p, Vector3 a, Vector3 b, float margin)
        {
            Vector3 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 > 1e-6f ?
                Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2) : 0f;
            return (a + ab * t - p).sqrMagnitude <= margin * margin;
        }

        internal static bool ChangedNear(int since, Vector3 a, Vector3 b, float margin)
        {
            if (since == WorldGen) return false;
            if (WorldGen - since >= Kept || since < 0) return true;
            float ax = a.x, ay = a.y, az = a.z, bx = b.x - ax, by = b.y - ay, bz = b.z - az;
            float len2 = bx * bx + by * by + bz * bz, inv = len2 > 1e-6f ? 1f / len2 : 0f, m2 = margin * margin;
            for (int g = since + 1; g <= WorldGen; g++)
            {
                int k = g % Kept;
                if (ChangeAll[k]) return true;
                Vector3 p = ChangeAt[k];
                float px = p.x - ax, py = p.y - ay, pz = p.z - az;
                float t = (px * bx + py * by + pz * bz) * inv;
                if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
                float dx = bx * t - px, dy = by * t - py, dz = bz * t - pz;
                if (dx * dx + dy * dy + dz * dz <= m2) return true;
            }
            return false;
        }

        private const float LoopMoved = .12f, EdgeMoved = .08f, ModelMoved = .25f;
        private const float LoopMinGap = .06f, EdgeMinGap = .04f;

        // Near a real blocking surface, small motion can change the
        // diffraction detour significantly. Sample that continuous
        // geometry more finely; in clear areas retain the existing cheap
        // 0.12 m threshold for uncached geometry.
        private static bool Unchanged(AcousticFilter f, Vector3 at, Vector3 ear)
        {
            float moved = f.EdgeModelValid && f.EdgeCanOverride && f.EdgePositionKnown
                ? ModelMoved : HasNearbyWall(f, ear) ? EdgeMoved : LoopMoved;
            return !ChangedNear(f.CheckedGen, ear, at, Corners.Reach) &&
                (ear - f.CheckedFrom).sqrMagnitude < moved * moved &&
                (at - f.CheckedAt).sqrMagnitude < moved * moved;
        }

        private static bool HasNearbyWall(AcousticFilter f, Vector3 ear)
        {
            if (f.Walls.Near && (ear - f.Walls.NearAt).sqrMagnitude < Corners.Reach * Corners.Reach) return true;
            if (f.Records != null)
                foreach (Corners.Record rec in f.Records)
                    if (rec != null && rec.Valid &&
                        (ear - rec.At).sqrMagnitude < Corners.Reach * Corners.Reach &&
                        !ChangedNear(rec.Gen, rec.At, rec.At, rec.Span + SameWall)) return true;
            return false;
        }

        // A sound of your own character's (its own, or one of its own made placed, PlayerSounds).
        internal static bool Mine(Transform sound, AcousticFilter f)
        {
            Player p = Player.m_localPlayer;
            if (p == null) return false;
            Transform me = p.transform;
            return s_myStep || (f != null && f.Player == me) || (sound != null && sound.IsChildOf(me));
        }

        // Footsteps are made at the foot, on their own (FootStep.DoEffect): yours are marked while
        // they're made (their sound starts inside it).
        private static bool s_myStep;
        [HarmonyPatch(typeof(FootStep), "DoEffect")]
        private static class MyStep
        {
            private static void Prefix(FootStep __instance)
            {
                Player p = Player.m_localPlayer;
                s_myStep = p != null && __instance.transform.IsChildOf(p.transform);
            }
            private static System.Exception Finalizer(System.Exception __exception) { s_myStep = false; return __exception; }
        }

        internal static void WatchLoop(AcousticFilter f, Vector3 at, Vector3 ear, float now)
        {
            float since = now - f.CheckedTime;
            if (since < (HasNearbyWall(f, ear) ? EdgeMinGap : LoopMinGap) || !f.Active) return; // past the count: left to Unity, as in Recheck
            if (Unchanged(f, at, ear)) return;
            AudioSource src = f.Source;
            if (src == null || !src.isPlaying) { f.WatchLoop = false; return; }
            Vector3 p = f.transform.position;
            // Listener/source motion is NOT a change to world geometry.
            // Keep the ordinary audio-time glide for position updates;
            // Batch.GeometryChanged sets FastGeometryResponse for an actual
            // piece placement, removal, terrain edit or door change.
            Batch.Enqueue(f, p, Plugin.RoomReverb.Value && !src.bypassReverbZones);
            s_loopChecks++;
        }

        internal static string Summary()
        {
            string s = "sounds checked " + s_checked + ", muffled " + s_muffled + (s_worst != null ? " (most: " + s_worst + ")" : "") +
                ", filters added late " + s_added +
                ", loop checks " + s_loopChecks +
                ", composite open-panel hits " + s_openProxyHits +
                ", saturated ray buffers " + s_saturatedRayBuffers +
                "; " + AudioProvenance.Summary() + "; " + AcousticFilter.Format();
            s_checked = s_muffled = s_added = s_loopChecks = 0;
            s_openProxyHits = s_saturatedRayBuffers = 0;
            s_worstOcc = 0f; s_worst = null;
            return s;
        }
    }
}
