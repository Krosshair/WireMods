$ErrorActionPreference='Stop'
$r='C:\Users\kajet\WireMods\WiresAcoustics\src'
$nl=[string][char]10
$f=Join-Path $r 'Spots.cs'
$s=[IO.File]::ReadAllText($f).Replace(([string][char]13+[char]10),$nl)
$old='            Transform root = baseParent != null ? baseParent : given;'
$new=$old+$nl+
'            // The producing game method supplies the exact owner when EffectList'+$nl+
'            // was created at a pivot without a parent (chests/doors/etc).'+$nl+
'            if (root == null && InteractionOrigin.Active != null &&'+$nl+
'                (basePos - InteractionOrigin.Active.position).sqrMagnitude < .01f)'+$nl+
'                root = given = InteractionOrigin.Active;'
if(!$s.Contains($old)){throw 'root missing'};$s=$s.Replace($old,$new)
$old='            if (root != null && PlaceSmelter(list, made, root, ref given)) return;'
$new='            if (root != null && PlaceInteraction(list, made, root, ref given)) return;'
if(!$s.Contains($old)){throw 'interaction first missing'};$s=$s.Replace($old,$new)
$old='            if (PlaceSmelter(list, made, piece, ref given)) return;'
$new='            if (PlaceInteraction(list, made, piece, ref given)) return;'
if(!$s.Contains($old)){throw 'interaction fallback missing'};$s=$s.Replace($old,$new)
$start=$s.IndexOf('        // A single event-time change; no added Update or sound scanning.')
$end=$s.IndexOf('        private static int s_noted;',$start)
if($start -lt 0 -or $end -lt 0){throw 'interaction method range missing'}
$method=@'
        // Exact EffectList identity and the producer's actual component.
        // Only change sounds belonging to this interaction, never operation,
        // crafting ambience, breaking, smoke, or machine output effects.
        private static bool PlaceInteraction(EffectList list, GameObject[] made,
            Transform owner, ref Transform given)
        {
            Vector3 at = default;
            bool part = false, recognized = false;

            if (owner.GetComponent<Smelter>() is Smelter smelter)
            {
                Switch sw = ReferenceEquals(list, smelter.m_oreAddedEffects)
                    ? smelter.m_addOreSwitch
                    : ReferenceEquals(list, smelter.m_fuelAddedEffects)
                        ? smelter.m_addWoodSwitch : null;
                recognized = sw != null;
                if (recognized) { at = sw.transform.position; part = true; }
            }
            else if (owner.GetComponent<Fermenter>() is Fermenter fermenter)
            {
                Switch sw = ReferenceEquals(list, fermenter.m_addedEffects)
                    ? fermenter.m_addSwitch
                    : ReferenceEquals(list, fermenter.m_tapEffects)
                        ? fermenter.m_tapSwitch : null;
                recognized = sw != null;
                if (recognized) { at = sw.transform.position; part = true; }
            }
            else if (owner.GetComponent<CookingStation>() is CookingStation cooking)
            {
                recognized = ReferenceEquals(list, cooking.m_fuelAddedEffects) &&
                    cooking.m_addFuelSwitch != null;
                if (recognized) { at = cooking.m_addFuelSwitch.transform.position; part = true; }
            }
            else if (owner.GetComponent<Container>() is Container chest)
            {
                recognized = ReferenceEquals(list, chest.m_openEffects) ||
                    ReferenceEquals(list, chest.m_closeEffects);
                if (recognized) recognized = Area.TopOf(owner, out at);
                // A chest opens/closes as a whole: use its COMPLETE bounds.
            }
            else if (owner.GetComponent<Door>() is Door door)
            {
                recognized = ReferenceEquals(list, door.m_openEffects) ||
                    ReferenceEquals(list, door.m_closeEffects);
                if (recognized) recognized = Hinge(owner, out at);
            }
            else if (owner.GetComponent<Fireplace>() is Fireplace fire)
            {
                recognized = ReferenceEquals(list, fire.m_fuelAddedEffects) ||
                    ReferenceEquals(list, fire.m_toggleOnEffects);
                if (recognized) recognized = Area.FlameOf(owner, out at);
            }
            else if (owner.GetComponent<ArmorStand>() is ArmorStand stand)
            {
                recognized = ReferenceEquals(list, stand.m_effects);
                if (recognized) recognized = Area.CenterOf(owner, out at);
            }
            if (!recognized) return false;

            given = owner;
            Move(made, owner, at, null);
            foreach (GameObject go in made)
            {
                if (go == null || go.GetComponent<ZSFX>() == null) continue;
                AcousticFilter filter = go.GetComponent<AcousticFilter>();
                if (filter == null)
                    filter = go.AddComponent<AcousticFilter>();
                filter.ShapeRoot = owner;
                filter.PartInteraction = part;
                filter.AreaKnown = false;
            }
            Note(owner.name + (part ? " operated part" : " interaction"), made);
            return true;
        }

'@.Replace(([string][char]13+[char]10),$nl)
$s=$s.Substring(0,$start)+$method+$s.Substring($end)
[IO.File]::WriteAllText($f,$s,[Text.UTF8Encoding]::new($false))

# Owner provenance is set by game method at creation, not by physics proximity.
# If a filter isn't on a newly spawned effect (other modded sound prefabs),
# insert one now so source bounds still work for chest closing.
$f=Join-Path $r 'Follow.cs'
$s=[IO.File]::ReadAllText($f).Replace(([string][char]13+[char]10),$nl)
$old='                Spots.Place(__instance, __result, basePos, baseParent, ref given);'
$new=$old+$nl+
'                if (given == null && InteractionOrigin.Active != null &&'+$nl+
'                    (basePos - InteractionOrigin.Active.position).sqrMagnitude < .01f)'+$nl+
'                    given = InteractionOrigin.Active;'
if(!$s.Contains($old)){throw 'Follow placement missing'}
$s=$s.Replace($old,$new)
$old='                            go.GetComponentsInChildren(true, Filters;'
if($s.Contains($old)){throw 'Bad source assumption'}
$old='                            go.GetComponentsInChildren(true, Filters);'
$new='                            // Make all owned, newly spawned ZSFX discoverable even'+$nl+
'                            // when their prefab lacked the preattached filter.'+$nl+
'                            if (go.GetComponent<ZSFX>() != null &&'+$nl+
'                                go.GetComponent<AcousticFilter>() == null)'+$nl+
'                                go.AddComponent<AcousticFilter>();'+$nl+$old
if(!$s.Contains($old)){throw 'Follow filters missing'}
$s=$s.Replace($old,$new)
[IO.File]::WriteAllText($f,$s,[Text.UTF8Encoding]::new($false))

dotnet build (Join-Path $r 'WiresAcoustics.csproj') -c Release --no-restore -v:q
if($LASTEXITCODE -ne 0){throw 'BUILD FAILURE'}
