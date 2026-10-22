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
        }

        private static ConditionalWeakTable<Switch, Owner>
            _owners = new ConditionalWeakTable<Switch, Owner>();

        internal static void Register(ArmorStand stand)
        {
            if(stand == null || stand.m_slots == null)
                return;
            foreach(var slot in stand.m_slots)
                RegisterSwitch(slot.m_switch,stand);
            RegisterSwitch(stand.m_changePoseSwitch,stand);
        }

        private static void RegisterSwitch(Switch sw, ArmorStand stand)
        {
            if(sw == null)
                return;
            _owners.Remove(sw);
            _owners.Add(sw,new Owner {Stand=stand});
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
