using System.Collections.Generic;
using UnityEngine;

namespace WiresStackify
{
    // While your pouch is hovered, the hover preview updates the moment a
    // container comes into or goes out of quick stack / restock range as
    // you walk. Done with the physics engine's own trigger events, not by
    // checking every frame: two invisible spheres the size of the ranges
    // follow you, and a tiny marker sits at the middle of each container
    // around you (the point the range is measured to). Unity tells us when
    // one crosses the other. All of it exists only while hovering.
    internal static class RangeWatch
    {
        private static readonly List<GameObject> Made = new List<GameObject>();
        // Containers this far beyond the range get a marker; when you've
        // walked Rebuild metres from where they were placed, they're placed
        // again around you (also an event: you leaving a sphere), so every
        // container that could come into range always has one.
        private const float Margin = 50f;
        private const float Rebuild = 40f;

        internal static void Start()
        {
            Stop();
            Player p = Player.m_localPlayer;
            if (p == null || !NearbyChests.AreaAllowed()) return;
            float stack = StackifyPlugin.QuickStackRange.Value, restock = StackifyPlugin.RestockRange.Value;
            if (stack <= 0f && restock <= 0f) return;

            if (stack > 0f) Sphere(p, stack, false);
            if (restock > 0f && Mathf.Abs(restock - stack) > 0.01f) Sphere(p, restock, false);
            Sphere(p, 0.01f, true); // a point at you, to notice leaving home

            // Home: where the markers were placed.
            GameObject home = new GameObject("WireStackifyRangeHome");
            home.layer = 0;
            home.transform.position = p.transform.position;
            SphereCollider h = home.AddComponent<SphereCollider>();
            h.isTrigger = true;
            h.radius = Rebuild;
            home.AddComponent<Home>();
            Made.Add(home);

            foreach (Container c in NearbyChests.InRange(p.transform.position, Mathf.Max(stack, restock) + Margin))
                AddMark(c);
        }

        // A container that loads in while you hover gets a marker too.
        internal static void Added(Container c)
        {
            if (Made.Count > 0) AddMark(c);
        }

        private static void AddMark(Container c)
        {
            {
                if (c == null) return;
                GameObject go = new GameObject("WireStackifyRangeMark");
                go.layer = 0;
                go.transform.SetParent(c.transform, false); // moves with carts and ships
                SphereCollider col = go.AddComponent<SphereCollider>();
                col.isTrigger = true;
                col.radius = 0.01f;
                go.AddComponent<Mark>();
                Made.Add(go);
            }
        }

        internal static void Stop()
        {
            foreach (GameObject go in Made) if (go != null) Object.Destroy(go);
            Made.Clear();
        }

        private static void Sphere(Player p, float range, bool point)
        {
            GameObject go = new GameObject("WireStackifyRange");
            go.layer = 0;
            go.transform.SetParent(p.transform, false);
            SphereCollider col = go.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = range / Mathf.Max(0.0001f, p.transform.lossyScale.x);
            Rigidbody body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            go.AddComponent<Edge>().Point = point;
            Made.Add(go);
        }

        private sealed class Mark : MonoBehaviour { }
        private sealed class Home : MonoBehaviour { }

        private sealed class Edge : MonoBehaviour
        {
            public bool Point;

            private void OnTriggerEnter(Collider other)
            {
                if (!Point && other != null && other.GetComponent<Mark>() != null)
                    StackifyButtons.RefreshHoverSoon();
            }

            private void OnTriggerExit(Collider other)
            {
                if (other == null) return;
                if (Point)
                {
                    if (other.GetComponent<Home>() != null) Replace();
                }
                else if (other.GetComponent<Mark>() != null)
                    StackifyButtons.RefreshHoverSoon();
            }
        }

        // Walked far from where the markers were placed: place them again.
        // Deferred a frame so nothing is destroyed inside its own event.
        private static bool _replacing;
        private static void Replace()
        {
            if (_replacing) return;
            _replacing = true;
            StackifyEffects.Runner.Get().StartCoroutine(ReplaceSoon());
        }

        private static System.Collections.IEnumerator ReplaceSoon()
        {
            yield return null;
            _replacing = false;
            if (Made.Count == 0) yield break; // hover ended meanwhile
            Start();
            StackifyButtons.RefreshHoverSoon();
        }
    }
}
