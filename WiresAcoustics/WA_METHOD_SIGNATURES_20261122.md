# Wire's Acoustics 0.7.0 — exhaustive method inventory

### AcousticMath.cs
- `OcclusionStrength(property) (14, 4 lines)`
- `LogLerp (19, 2 lines)`

### AcousticTrace.cs
- `Record (31, 19 lines)`
- `EmitTransitions (51, 23 lines)`
- `RecordGeometry (86, 11 lines)`
- `EmitGeometryChanges (98, 20 lines)`
- `Emit (119, 125 lines)`

### Ambience.cs
- `RememberPan (62, 6 lines)`
- `RestorePlaced (69, 9 lines)`
- `IsAmbient (79, 2 lines)`
- `Pan (87, 45 lines)`
- `Lean (134, 10 lines)`
- `Update (146, 90 lines)`
- `Sounding (238, 9 lines)`
- `Bed (249, 19 lines)`
- `Glide (281, 40 lines)`
- `WindFrom (321, 7 lines)`
- `Play (331, 32 lines)`
- `Fade (367, 12 lines)`
- `Make (380, 22 lines)`
- `Apply (403, 16 lines)`
- `Forget (421, 6 lines)`
- `Need (429, 6 lines)`
- `Reset (436, 26 lines)`
- `Now (464, 13 lines)`
- `Db (478, 1 lines)`
- `Summary (480, 1 lines)`

### Animated.cs
- `Prefix (15, 19 lines)`

### Area.cs
- `Invalidate (26, 17 lines)`
- `InvalidateAll (44, 6 lines)`
- `Measure (52, 28 lines)`
- `Voice (85, 18 lines)`
- `TopOf (106, 7 lines)`
- `CenterOf (115, 7 lines)`
- `FlameOf (124, 13 lines)`
- `Cached (138, 13 lines)`
- `Of (152, 24 lines)`
- `AddCollider (181, 28 lines)`
- `AddCorners (210, 15 lines)`
- `FireLevel (231, 6 lines)`
- `WidthAt (239, 2 lines)`

### AudioMeterHistory.cs
- `Publish (26, 13 lines)`
- `Read (41, 36 lines)`

### AudioProvenance.cs
- `Summary (18, 6 lines)`
- `ReplacePlay (32, 29 lines)`
- `MarkAndPlay (62, 12 lines)`
- `PlayRandom (75, 1 lines)`
- `PlayLava (76, 1 lines)`
- `Transpiler (81, 3 lines)`
- `Transpiler (89, 3 lines)`

### Batch.cs
- `Summary (49, 13 lines)`
- `GeometryChanged (66, 42 lines)`
- `Enqueue (109, 46 lines)`
- `Tidy (181, 13 lines)`
- `Step (196, 152 lines)`
- `EnsureFlight (349, 8 lines)`
- `Fly (358, 6 lines)`
- `StartProbes (369, 17 lines)`
- `Cast3 (388, 2 lines)`
- `Probe (392, 11 lines)`
- `ReadProbe (407, 22 lines)`
- `Open (431, 10 lines)`
- `Cast (442, 8 lines)`
- `DirectWalls (459, 25 lines)`
- `DirectWalls (485, 5 lines)`
- `VolumeRay (493, 8 lines)`
- `Ray (503, 11 lines)`
- `HeardFrom (524, 20 lines)`
- `Toward (547, 8 lines)`
- `ChangedDuringFlight (559, 25 lines)`
- `Apply (587, 363 lines)`
- `Flush (952, 9 lines)`
- `Shutdown (962, 6 lines)`
- `Begin (982, 1 lines)`
- `Over(property) (983, 1 lines)`

### Budget.cs
- `Note (20, 13 lines)`
- `Toggle (37, 9 lines)`
- `Toggles (47, 7 lines)`
- `Step (56, 54 lines)`
- `Silence (113, 8 lines)`
- `Heard (128, 1 lines)`
- `Open (132, 12 lines)`
- `Clear (145, 6 lines)`
- `Loudness (154, 29 lines)`
- `Virtual (185, 11 lines)`
- `Summary (197, 2 lines)`

### ContinuousEdges.cs
- `Measured (11, 27 lines)`
- `EvaluateVolume (44, 52 lines)`
- `Evaluate (97, 97 lines)`

### Corners.cs
- `From (33, 11 lines)`
- `Dir (46, 8 lines)`
- `Probes (57, 6 lines)`
- `Probe (64, 1 lines)`
- `Covers (86, 8 lines)`
- `Match (95, 5 lines)`
- `Illuminated (110, 34 lines)`
- `LengthGain (145, 6 lines)`

### Cost.cs
- `Start (9, 1 lines)`
- `Add (11, 7 lines)`
- `AddTicks (19, 5 lines)`
- `AddTick (27, 7 lines)`
- `Ms (35, 1 lines)`
- `Report (37, 8 lines)`

### Diagnostics.cs
- `Summary (10, 7 lines)`

### Diffraction.cs
- `Diffraction (16, 15 lines)`
- `Loss (32, 8 lines)`
- `RoundObstacle (45, 27 lines)`
- `Opening (78, 18 lines)`
- `Lit (98, 8 lines)`
- `LitEdge (112, 7 lines)`
- `DetourFor (122, 17 lines)`
- `RoundDetour (142, 9 lines)`
- `Round (153, 5 lines)`

### Directions.cs
- `Execute (39, 27 lines)`
- `Want (69, 6 lines)`
- `Step (77, 86 lines)`
- `Apply (164, 32 lines)`
- `Grow (197, 11 lines)`
- `Idle (210, 5 lines)`
- `Flush (217, 4 lines)`
- `Shutdown (222, 11 lines)`

### Doppler.cs
- `ApplyOther (25, 14 lines)`
- `Postfix (48, 17 lines)`

### Equip.cs
- `Take (14, 10 lines)`
- `Release (25, 6 lines)`
- `Vis (32, 1 lines)`
- `Prefix (37, 1 lines)`
- `Finalizer (38, 1 lines)`
- `Prefix (44, 5 lines)`
- `Finalizer (49, 1 lines)`
- `Prefix (55, 1 lines)`
- `Finalizer (56, 1 lines)`
- `Prefix (62, 2 lines)`
- `Finalizer (64, 1 lines)`
- `Prefix (70, 1 lines)`
- `Finalizer (71, 1 lines)`

### Filter.cs
- `GlobalGain(property) (49, 1 lines)`
- `GlobalCutoff(property) (50, 1 lines)`
- `RefreshOutputFormat (58, 10 lines)`
- `For (71, 3 lines)`
- `SetGlobal (75, 6 lines)`
- `DiagnosticsSummary (93, 12 lines)`
- `TakeTicks (105, 6 lines)`
- `Hrtf.get (118, 1 lines)`
- `Hrtf.set (119, 7 lines)`
- `Placed.get (192, 1 lines)`
- `Placed.set (193, 1 lines)`
- `NeedsWork(property) (197, 3 lines)`
- `AirAt (201, 8 lines)`
- `AirTable (223, 18 lines)`
- `AirAbsorption (243, 9 lines)`
- `PastRange (259, 5 lines)`
- `TakePanning (271, 15 lines)`
- `Pan (296, 6 lines)`
- `Tracked(property) (324, 1 lines)`
- `Awake (328, 16 lines)`
- `OnDestroy (345, 16 lines)`
- `Active.get (369, 1 lines)`
- `Active.set (370, 7 lines)`
- `OnEnable (379, 5 lines)`
- `OnDisable (385, 4 lines)`
- `Set (390, 16 lines)`
- `TargetGain(property) (407, 1 lines)`
- `TargetCutoff(property) (408, 1 lines)`
- `DspGain(property) (410, 1 lines)`
- `DspCutoff(property) (411, 1 lines)`
- `EstimatedCompositeGain(property) (412, 1 lines)`
- `EstimatedCompositeCutoff(property) (413, 1 lines)`
- `SetDirection (415, 1 lines)`
- `ReleaseHrtf (418, 10 lines)`
- `ReleaseAll (430, 19 lines)`
- `Explain (451, 4 lines)`
- `Line (471, 13 lines)`
- `Physical (485, 35 lines)`
- `Format (521, 5 lines)`
- `EnsureMeterHistory (531, 4 lines)`
- `ReadMeterHistory (535, 7 lines)`
- `Energy (547, 6 lines)`
- `OnAudioFilterRead (554, 73 lines)`
- `Process (628, 55 lines)`
- `SmoothOcclusion (687, 14 lines)`
- `Spatialize (702, 118 lines)`
- `Glide (822, 1 lines)`

### Follow.cs
- `Postfix (27, 49 lines)`
- `Postfix (87, 16 lines)`
- `Prefix (108, 1 lines)`
- `Finalizer (109, 1 lines)`
- `Prefix (115, 1 lines)`
- `Finalizer (116, 1 lines)`
- `Prefix (126, 5 lines)`
- `Finalizer (131, 1 lines)`
- `Moves (137, 11 lines)`
- `Step (150, 13 lines)`
- `Track (166, 6 lines)`
- `Networked (177, 1 lines)`
- `Clear (179, 1 lines)`
- `Count(property) (180, 1 lines)`
- `Tracked (182, 5 lines)`

### FresnelEdge.cs
- `FresnelEdge (28, 22 lines)`
- `Sample (51, 7 lines)`
- `Field (61, 7 lines)`
- `PathDistances (72, 6 lines)`
- `Parameter (82, 7 lines)`
- `Intensity (92, 10 lines)`
- `Transfer (103, 23 lines)`

### GeometryEvents.cs
- `Changed (24, 6 lines)`
- `Apply (33, 6 lines)`
- `AreaLoaded (43, 8 lines)`
- `AreaReady (61, 14 lines)`
- `Step (77, 8 lines)`
- `Postfix (89, 5 lines)`
- `Prefix (99, 5 lines)`
- `Postfix (112, 4 lines)`
- `Postfix (123, 4 lines)`
- `Prefix (132, 6 lines)`
- `Postfix (143, 5 lines)`

### Hearing.cs
- `Update (21, 5 lines)`

### Indoor.cs
- `Prefix (16, 6 lines)`
- `Prefix (27, 5 lines)`

### Menu.cs
- `Postfix (12, 18 lines)`

### Mist.cs
- `Undo (17, 10 lines)`

### Occlusion.cs
- `Resume (30, 7 lines)`
- `Resume (38, 10 lines)`
- `Postfix (49, 6 lines)`
- `Run (56, 138 lines)`
- `Send (200, 7 lines)`
- `ReverbSend (208, 1 lines)`
- `FacingPoint (228, 17 lines)`
- `Lines (247, 9 lines)`
- `Along (269, 51 lines)`
- `Counts (326, 13 lines)`
- `Inside (340, 8 lines)`
- `Classify (352, 9 lines)`
- `Describe (365, 12 lines)`
- `MassLaw (381, 1 lines)`
- `TransmissionEnergy (387, 22 lines)`
- `Loss (413, 15 lines)`
- `Clear(property) (441, 1 lines)`
- `Layer (446, 17 lines)`
- `Group (470, 16 lines)`
- `Ridge (490, 20 lines)`
- `Wall (514, 27 lines)`
- `Mass (544, 18 lines)`
- `Thickness (563, 11 lines)`
- `Muffle (575, 11 lines)`
- `Voices (589, 22 lines)`
- `BaseSpreadOf (614, 4 lines)`
- `Recheck (625, 29 lines)`
- `WorldChanged (670, 12 lines)`
- `NearLine (684, 8 lines)`
- `ChangedNear (693, 16 lines)`
- `Unchanged (717, 9 lines)`
- `HasNearbyWall (727, 10 lines)`
- `Mine (739, 7 lines)`
- `Prefix (753, 5 lines)`
- `Finalizer (758, 1 lines)`
- `WatchLoop (761, 13 lines)`
- `Summary (775, 13 lines)`

### Ocean.cs
- `Postfix (44, 55 lines)`
- `RestoreUnlocated (104, 12 lines)`
- `Shade (125, 40 lines)`
- `Cast (166, 20 lines)`
- `Read (192, 31 lines)`
- `SeaZone (231, 12 lines)`
- `Shutdown (244, 10 lines)`
- `Dispose (255, 5 lines)`
- `Summary (261, 7 lines)`

### OpenPanelArea.cs
- `FromPrefab (11, 19 lines)`

### PlayerSounds.cs
- `RestoreAll (17, 17 lines)`
- `Place (37, 27 lines)`
- `Of (65, 14 lines)`
- `IsCue (80, 6 lines)`
- `MarkCue (87, 15 lines)`

### Plugin.cs
- `Setting (36, 3 lines)`
- `Awake (40, 36 lines)`
- `MoreVoices (86, 9 lines)`
- `OnDestroy (96, 11 lines)`

### PortalLoop.cs
- `Prefix (39, 2 lines)`
- `Postfix (46, 16 lines)`
- `Take (64, 33 lines)`
- `Step (99, 43 lines)`
- `Hand (144, 39 lines)`
- `Let (185, 5 lines)`
- `BaseOf (191, 1 lines)`
- `Drop (194, 15 lines)`
- `Clear (211, 6 lines)`
- `Reset (218, 7 lines)`

### Portals.cs
- `Find (35, 51 lines)`
- `NeighboursOf (87, 5 lines)`
- `BuildNeighbours (93, 40 lines)`
- `LengthGain (134, 6 lines)`
- `Summary (141, 7 lines)`

### Prefabs.cs
- `Postfix (16, 26 lines)`

### RayJobs.cs
- `Query (13, 3 lines)`
- `Ensure (17, 9 lines)`
- `EnsureFixed (29, 7 lines)`
- `Submit (37, 8 lines)`
- `Cast3 (46, 8 lines)`
- `ReadHits (58, 12 lines)`
- `Dispose (71, 5 lines)`

### Room.cs
- `Invalidate (47, 1 lines)`
- `SpeedAmbientAtBoundary (76, 13 lines)`
- `RestoreAmbientFade (90, 8 lines)`
- `Mask.get (101, 5 lines)`
- `BuildDirs (108, 11 lines)`
- `Lerp (123, 7 lines)`
- `ApplyTo (131, 11 lines)`
- `Differs (143, 5 lines)`
- `ToString (149, 3 lines)`
- `Postfix (160, 69 lines)`
- `Run (230, 57 lines)`
- `EnsureArrays (289, 2 lines)`
- `Setup (292, 24 lines)`
- `RemoveZone (319, 14 lines)`
- `RemoveVanilla (334, 6 lines)`
- `RestoreZones (341, 11 lines)`
- `Restore (353, 17 lines)`
- `Shutdown (371, 8 lines)`
- `Exposure (389, 16 lines)`
- `Out (407, 1 lines)`
- `PortalEvidence (410, 1 lines)`
- `WriteLog (418, 11 lines)`
- `Pick (434, 8 lines)`
- `Measure (443, 191 lines)`
- `Surface (644, 23 lines)`
- `Furniture (671, 10 lines)`
- `Word (684, 11 lines)`
- `GroundClassAt (708, 28 lines)`
- `GroundAt (737, 5 lines)`
- `Absorption (745, 27 lines)`
- `Of (805, 6 lines)`
- `Of (812, 6 lines)`
- `BoxOf (819, 6 lines)`
- `IsCoarseOpenPanel (831, 30 lines)`
- `Make (862, 41 lines)`

### SharedEdges.cs
- `Clear (28, 7 lines)`
- `TryCopy (38, 37 lines)`
- `Publish (76, 22 lines)`
- `Summary (99, 7 lines)`

### SourceVolume.cs
- `Halton (21, 11 lines)`
- `Project (33, 41 lines)`
- `Count (75, 15 lines)`
- `Point (91, 12 lines)`

### Spawned.cs
- `Take (14, 15 lines)`
- `Postfix (33, 1 lines)`
- `TargetMethod (43, 5 lines)`
- `Postfix (48, 4 lines)`
- `Summary (54, 6 lines)`

### SpectralPaths.cs
- `BandK (27, 5 lines)`
- `SetSampleRate (33, 6 lines)`
- `Response (40, 12 lines)`
- `HighEnergy (53, 4 lines)`
- `Add (58, 6 lines)`
- `CutoffFor (69, 25 lines)`
- `Decode (95, 18 lines)`
- `Cascade (114, 8 lines)`
- `Combine (123, 9 lines)`

### Spots.cs
- `Mask(property) (36, 2 lines)`
- `Clear (39, 1 lines)`
- `Place (42, 66 lines)`
- `Note (110, 5 lines)`
- `Note (116, 6 lines)`
- `Attacking (124, 9 lines)`
- `WeaponHand (136, 12 lines)`
- `ItemSound (150, 9 lines)`
- `CharacterAt (160, 6 lines)`
- `KindOf (173, 9 lines)`
- `Move (184, 5 lines)`
- `MoveOne (190, 11 lines)`
- `Hinge (208, 14 lines)`
- `StationPart (226, 25 lines)`
- `PieceAt (253, 13 lines)`
- `BackSlot (268, 17 lines)`

### Stackify.cs
- `Active(property) (19, 1 lines)`
- `Restore (22, 4 lines)`
- `Prepare (30, 1 lines)`
- `TargetMethod (31, 1 lines)`
- `Prefix (32, 1 lines)`
- `Finalizer (33, 1 lines)`
- `Prepare (39, 1 lines)`
- `TargetMethod (40, 1 lines)`
- `Postfix (41, 4 lines)`
- `Prepare (50, 1 lines)`
- `TargetMethod (51, 1 lines)`
- `Prefix (52, 6 lines)`

### Starts.cs
- `Summary (40, 8 lines)`
- `Hold (53, 5 lines)`
- `HoldAir (60, 7 lines)`
- `Postfix (73, 8 lines)`
- `Travel (84, 9 lines)`
- `Step (96, 103 lines)`
- `Schedule (201, 60 lines)`
- `Cast3 (262, 2 lines)`
- `PlaceInOpenAir (267, 27 lines)`
- `Flush (296, 9 lines)`
- `Shutdown (306, 5 lines)`

### ThreeRayTransmission.cs
- `Blend (11, 10 lines)`
- `IndirectAmplitude (25, 2 lines)`
- `Combine (28, 15 lines)`

### Underwater.cs
- `Apply (24, 4 lines)`
- `Apply (29, 5 lines)`
- `High (37, 1 lines)`
- `Below (39, 1 lines)`
- `SubmergedAmount (41, 7 lines)`
- `Muffles (53, 1 lines)`
- `SourceAmount (58, 19 lines)`
- `CharacterAt (80, 15 lines)`
- `Update (97, 6 lines)`
- `Reset (104, 6 lines)`
- `Summary (111, 1 lines)`

### VanillaShortAmbient.cs
- `Prefix (17, 33 lines)`

### VoiceChat.cs
- `Prepare (20, 1 lines)`
- `TargetMethod (22, 7 lines)`
- `Postfix (30, 7 lines)`
- `Prepare (44, 1 lines)`
- `TargetMethod (46, 5 lines)`
- `Postfix (52, 10 lines)`
- `Prepare (68, 1 lines)`
- `TargetMethod (70, 5 lines)`
- `Prefix (76, 6 lines)`

### VolumeFresnel.cs
- `Transfer (10, 26 lines)`

