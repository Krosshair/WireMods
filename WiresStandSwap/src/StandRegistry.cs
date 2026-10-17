using System.Runtime.CompilerServices;
using UnityEngine;

namespace WiresStandSwap
{
    // No scene scan, no inventory scan. ArmorStand.Awake registers
    // the exact Switch references once; weak keys disappear with
    // destroyed world objects (no saved-world strong reference leak).
    internal static class StandRegistry
    {
        private sealed class Owner
        {
            internal ArmorStand Stand;
            internal int Index;
        }

        private static ConditionalWeakTable<Switch, Owner>
            _owners = new ConditionalWeakTable<Switch, Owner>();

        internal static void Register(ArmorStand stand)
        {
            if(stand == null || stand.m_slots == null)
                return;
            for (int i = 0; i < stand.m_slots.Count; ++i)
                RegisterSwitch(stand.m_slots[i].m_switch, stand, i);
            RegisterSwitch(stand.m_changePoseSwitch, stand, -1);
        }

        private static void RegisterSwitch(Switch sw, ArmorStand stand, int index)
        {
            if(sw == null)
                return;
            _owners.Remove(sw);
            _owners.Add(sw,new Owner {Stand=stand, Index=index});
        }

        internal static ArmorStand ForSwitch(Switch sw)
        {
            if(sw == null)
                return null;
            Owner owner;
            return _owners.TryGetValue(sw,out owner) &&
                   owner.Stand != null
                ? owner.Stand : null;
        }

        internal static int IndexForSwitch(Switch sw)
        {
            if (sw == null) return -1;
            Owner owner;
            return _owners.TryGetValue(sw, out owner) &&
                   owner.Stand != null ? owner.Index : -1;
        }

        internal static ArmorStand Resolve(GameObject hovered)
        {
            if(hovered == null)
                return null;
            Switch sw=hovered.GetComponentInParent<Switch>();
            ArmorStand mapped=ForSwitch(sw);
            if(mapped != null)
                return mapped;
            // Covers stand geometry with colliders outside
            // the small per-item switches.
            return hovered.GetComponentInParent<ArmorStand>();
        }

        internal static void Clear()
        {
            _owners=new ConditionalWeakTable<Switch,Owner>();
        }
    }
}
