using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace WiresAcoustics
{
    // The existing batched ray pipeline, shared without changing ownership,
    // ray order, allocation sizes, minimum batches, or MaxHits semantics.
    // Owners still decide when to enqueue, wait, interpret, and dispose.
    internal static class RayJobs
    {
        // Preserve all Unity physics flags identically for every producer.
        internal static QueryParameters Query(int mask) =>
            new QueryParameters(mask, false, QueryTriggerInteraction.Ignore,
                false);

        internal static void Ensure(ref NativeArray<RaycastCommand> commands,
            ref NativeArray<RaycastHit> hits, int required, int hitsPerRay)
        {
            if (commands.IsCreated && commands.Length >= required) return;
            if (commands.IsCreated) { commands.Dispose(); hits.Dispose(); }
            int cap = Mathf.NextPowerOfTwo(Mathf.Max(64, required));
            commands = new NativeArray<RaycastCommand>(cap, Allocator.Persistent);
            hits = new NativeArray<RaycastHit>(cap * hitsPerRay, Allocator.Persistent);
        }

        // Room and shoreline measurements use fixed-size, single-hit
        // buffers. Keep their original one-time allocation behavior.
        internal static void EnsureFixed(ref NativeArray<RaycastCommand> commands,
            ref NativeArray<RaycastHit> hits, int count)
        {
            if (commands.IsCreated) return;
            commands = new NativeArray<RaycastCommand>(count, Allocator.Persistent);
            hits = new NativeArray<RaycastHit>(count, Allocator.Persistent);
        }

        internal static JobHandle Submit(NativeArray<RaycastCommand> commands,
            NativeArray<RaycastHit> hits, int minimumCommandsPerJob, int maxHits)
        {
            JobHandle job = RaycastCommand.ScheduleBatch(commands, hits,
                minimumCommandsPerJob, maxHits);
            JobHandle.ScheduleBatchedJobs();
            return job;
        }

        internal static void Cast3(NativeArray<RaycastCommand> commands,
            Vector3 from, Vector3 to, QueryParameters query, ref int index)
        {
            Occlusion.Lines(from, to, out Vector3 side, out Vector3 up);
            commands[index++] = Batch.Cast(from, to, query);
            commands[index++] = Batch.Cast(from + side, to + side, query);
            commands[index++] = Batch.Cast(from + up, to + up, query);
        }

        // Unity leaves hit entries after the first empty collider untouched.
        // The same bounded extraction is used by obstruction and open-air
        // source placement; older buffer contents must never be interpreted.
        internal static int ReadHits(NativeArray<RaycastHit> hits, int first,
            int maxHits, RaycastHit[] destination)
        {
            int n = 0;
            for (int h = 0; h < maxHits; h++)
            {
                RaycastHit hit = hits[first * maxHits + h];
                if (hit.colliderInstanceID == 0) break;
                destination[n++] = hit;
            }
            return n;
        }

        internal static void Dispose(ref NativeArray<RaycastCommand> commands,
            ref NativeArray<RaycastHit> hits)
        {
            if (commands.IsCreated) { commands.Dispose(); hits.Dispose(); }
        }
    }
}
