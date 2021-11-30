﻿using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Barotrauma.Extensions;
using Barotrauma.Items.Components;

namespace Barotrauma
{
    class MonsterEvent : Event
    {
        private readonly string speciesName;
        private readonly int minAmount, maxAmount;
        private List<Character> monsters;

        private readonly float scatter;
        private readonly float offset;

        private Vector2? spawnPos;

        private bool disallowed;

        private readonly Level.PositionType spawnPosType;
        private readonly string spawnPointTag;

        private bool spawnPending;

        private readonly int maxAmountPerLevel = int.MaxValue;

        public List<Character> Monsters => monsters;
        public Vector2? SpawnPos => spawnPos;
        public bool SpawnPending => spawnPending;
        public int MinAmount => minAmount;
        public int MaxAmount => maxAmount;

        public override Vector2 DebugDrawPos
        {
            get { return spawnPos ?? Vector2.Zero; }
        }

        public override string ToString()
        {
            if (maxAmount <= 1)
            {
                return "MonsterEvent (" + speciesName + ")";
            }
            else if (minAmount < maxAmount)
            {
                return "MonsterEvent (" + speciesName + " x" + minAmount + "-" + maxAmount + ")";
            }
            else
            {
                return "MonsterEvent (" + speciesName + " x" + maxAmount + ")";
            }
        }

        public MonsterEvent(EventPrefab prefab)
            : base (prefab)
        {
            speciesName = prefab.ConfigElement.GetAttributeString("characterfile", "");
            CharacterPrefab characterPrefab = CharacterPrefab.FindByFilePath(speciesName);
            if (characterPrefab != null)
            {
                speciesName = characterPrefab.Identifier;
            }

            if (string.IsNullOrEmpty(speciesName))
            {
                throw new Exception("speciesname is null!");
            }

            int defaultAmount = prefab.ConfigElement.GetAttributeInt("amount", 1);
            minAmount = prefab.ConfigElement.GetAttributeInt("minamount", defaultAmount);
            maxAmount = Math.Max(prefab.ConfigElement.GetAttributeInt("maxamount", 1), minAmount);

            maxAmountPerLevel = prefab.ConfigElement.GetAttributeInt("maxamountperlevel", int.MaxValue);

            var spawnPosTypeStr = prefab.ConfigElement.GetAttributeString("spawntype", "");
            if (string.IsNullOrWhiteSpace(spawnPosTypeStr) ||
                !Enum.TryParse(spawnPosTypeStr, true, out spawnPosType))
            {
                spawnPosType = Level.PositionType.MainPath;
            }

            //backwards compatibility
            if (prefab.ConfigElement.GetAttributeBool("spawndeep", false))
            {
                spawnPosType = Level.PositionType.Abyss;
            }

            spawnPointTag = prefab.ConfigElement.GetAttributeString("spawnpointtag", string.Empty);

            offset = prefab.ConfigElement.GetAttributeFloat("offset", 0);
            scatter = Math.Clamp(prefab.ConfigElement.GetAttributeFloat("scatter", 500), 0, 3000);

            if (GameMain.NetworkMember != null)
            {
                List<string> monsterNames = GameMain.NetworkMember.ServerSettings.MonsterEnabled.Keys.ToList();
                string tryKey = monsterNames.Find(s => speciesName.ToLower() == s.ToLower());

                if (!string.IsNullOrWhiteSpace(tryKey))
                {
                    if (!GameMain.NetworkMember.ServerSettings.MonsterEnabled[tryKey])
                    {
                        disallowed = true; //spawn was disallowed by host
                    }
                }
            }
        }

        private Submarine GetReferenceSub()
        {
            return EventManager.GetRefEntity() as Submarine ?? Submarine.MainSub;
        }

        public override IEnumerable<ContentFile> GetFilesToPreload()
        {
            string path = CharacterPrefab.FindBySpeciesName(speciesName)?.FilePath;
            if (string.IsNullOrWhiteSpace(path))
            {
                DebugConsole.ThrowError($"Failed to find config file for species \"{speciesName}\"");
                yield break;
            }
            else
            {
                yield return new ContentFile(path, ContentType.Character);
            }
        }

        public override bool CanAffectSubImmediately(Level level)
        {
            float maxRange = Sonar.DefaultSonarRange * 0.8f;
            return GetAvailableSpawnPositions().Any(p => Vector2.DistanceSquared(p.Position.ToVector2(), GetReferenceSub().WorldPosition) < maxRange * maxRange);
        }

        public override void Init(bool affectSubImmediately)
        {
            if (GameSettings.VerboseLogging)
            {
                DebugConsole.NewMessage("Initialized MonsterEvent (" + speciesName + ")", Color.White);
            }
        }

        private List<Level.InterestingPosition> GetAvailableSpawnPositions()
        {
            var availablePositions = Level.Loaded.PositionsOfInterest.FindAll(p => spawnPosType.HasFlag(p.PositionType));
            var removals = new List<Level.InterestingPosition>();
            foreach (var position in availablePositions)
            {
                if (SpawnPosFilter != null && !SpawnPosFilter(position))
                {
                    removals.Add(position);
                    continue;
                }
                if (position.Submarine != null)
                {
                    if (position.Submarine.WreckAI != null && position.Submarine.WreckAI.IsAlive)
                    {
                        removals.Add(position);
                    }
                    else
                    {
                        continue;
                    }
                }
                if (position.PositionType != Level.PositionType.MainPath && 
                    position.PositionType != Level.PositionType.SidePath) 
                { 
                    continue; 
                }
                if (Level.Loaded.ExtraWalls.Any(w => w.IsPointInside(position.Position.ToVector2())))
                {
                    removals.Add(position);
                }
                if (position.Position.Y < Level.Loaded.GetBottomPosition(position.Position.X).Y)
                {
                    removals.Add(position);
                }
            }
            removals.ForEach(r => availablePositions.Remove(r));         
            return availablePositions;
        }

        private void FindSpawnPosition(bool affectSubImmediately)
        {
            if (disallowed) { return; }

            spawnPos = Vector2.Zero;
            var availablePositions = GetAvailableSpawnPositions();
            var chosenPosition = new Level.InterestingPosition(Point.Zero, Level.PositionType.MainPath, isValid: false);
            bool isRuinOrWreck = spawnPosType.HasFlag(Level.PositionType.Ruin) || spawnPosType.HasFlag(Level.PositionType.Wreck);
            if (affectSubImmediately && !isRuinOrWreck && !spawnPosType.HasFlag(Level.PositionType.Abyss))
            {
                if (availablePositions.None())
                {
                    //no suitable position found, disable the event
                    spawnPos = null;
                    Finished();
                    return;
                }
                Submarine refSub = GetReferenceSub();
                if (Submarine.MainSubs.Length == 2 && Submarine.MainSubs[1] != null)
                {
                    refSub = Submarine.MainSubs.GetRandom(Rand.RandSync.Unsynced);
                }
                float closestDist = float.PositiveInfinity;
                //find the closest spawnposition that isn't too close to any of the subs
                foreach (var position in availablePositions)
                {
                    Vector2 pos = position.Position.ToVector2();
                    float dist = Vector2.DistanceSquared(pos, refSub.WorldPosition);
                    foreach (Submarine sub in Submarine.Loaded)
                    {
                        if (sub.Info.Type != SubmarineType.Player && sub != GameMain.NetworkMember?.RespawnManager?.RespawnShuttle) { continue; }
                        
                        float minDistToSub = GetMinDistanceToSub(sub);
                        if (dist < minDistToSub * minDistToSub) { continue; }

                        if (closestDist == float.PositiveInfinity)
                        {
                            closestDist = dist;
                            chosenPosition = position;
                            continue;
                        }

                        //chosen position behind the sub -> override with anything that's closer or to the right
                        if (chosenPosition.Position.X < refSub.WorldPosition.X)
                        {
                            if (dist < closestDist || pos.X > refSub.WorldPosition.X)
                            {
                                closestDist = dist;
                                chosenPosition = position;
                            }
                        }
                        //chosen position ahead of the sub -> only override with a position that's also ahead
                        else if (chosenPosition.Position.X > refSub.WorldPosition.X)
                        {
                            if (dist < closestDist && pos.X > refSub.WorldPosition.X)
                            {
                                closestDist = dist;
                                chosenPosition = position;
                            }
                        }                        
                    }
                }
                //only found a spawnpos that's very far from the sub, pick one that's closer
                //and wait for the sub to move further before spawning
                if (closestDist > 15000.0f * 15000.0f)
                {
                    foreach (var position in availablePositions)
                    {
                        float dist = Vector2.DistanceSquared(position.Position.ToVector2(), refSub.WorldPosition);
                        if (dist < closestDist)
                        {
                            closestDist = dist;
                            chosenPosition = position;
                        }
                    }
                }
            }
            else
            {
                if (!isRuinOrWreck)
                {
                    float minDistance = 20000;
                    var refSub = GetReferenceSub();
                    availablePositions.RemoveAll(p => Vector2.DistanceSquared(refSub.WorldPosition, p.Position.ToVector2()) < minDistance * minDistance);
                    if (Submarine.MainSubs.Length > 1)
                    {
                        for (int i = 1; i < Submarine.MainSubs.Length; i++)
                        {
                            if (Submarine.MainSubs[i] == null) { continue; }
                            availablePositions.RemoveAll(p => Vector2.DistanceSquared(Submarine.MainSubs[i].WorldPosition, p.Position.ToVector2()) < minDistance * minDistance);
                        }
                    }
                }
                if (availablePositions.None())
                {
                    //no suitable position found, disable the event
                    spawnPos = null;
                    Finished();
                    return;
                }
                chosenPosition = availablePositions.GetRandom();
            }
            if (chosenPosition.IsValid)
            {
                spawnPos = chosenPosition.Position.ToVector2();
                if (chosenPosition.Submarine != null || chosenPosition.Ruin != null)
                {
                    var spawnPoint =
                        WayPoint.GetRandom(SpawnType.Enemy, sub: chosenPosition.Submarine ?? chosenPosition.Ruin?.Submarine, useSyncedRand: false, spawnPointTag: spawnPointTag);
                    if (spawnPoint != null)
                    {
                        System.Diagnostics.Debug.Assert(spawnPoint.Submarine == (chosenPosition.Submarine ?? chosenPosition.Ruin?.Submarine));
                        spawnPos = spawnPoint.WorldPosition;
                    }
                    else
                    {
                        //no suitable position found, disable the event
                        spawnPos = null;
                        Finished();
                        return;
                    }
                }
                else if ((chosenPosition.PositionType == Level.PositionType.MainPath || chosenPosition.PositionType == Level.PositionType.SidePath)
                    && offset > 0)
                {
                    Vector2 dir;
                    var waypoints = WayPoint.WayPointList.FindAll(wp => wp.Submarine == null);
                    var nearestWaypoint = waypoints.OrderBy(wp => Vector2.DistanceSquared(wp.WorldPosition, spawnPos.Value)).FirstOrDefault();
                    if (nearestWaypoint != null)
                    {
                        int currentIndex = waypoints.IndexOf(nearestWaypoint);
                        var nextWaypoint = waypoints[Math.Min(currentIndex + 20, waypoints.Count - 1)];
                        dir = Vector2.Normalize(nextWaypoint.WorldPosition - nearestWaypoint.WorldPosition);
                        // Ensure that the spawn position is not offset to the left.
                        if (dir.X < 0)
                        {
                            dir.X = 0;
                        }
                    }
                    else
                    {
                        dir = new Vector2(1, Rand.Range(-1, 1));
                    }
                    Vector2 targetPos = spawnPos.Value + dir * offset;
                    var targetWaypoint = waypoints.OrderBy(wp => Vector2.DistanceSquared(wp.WorldPosition, targetPos)).FirstOrDefault();
                    if (targetWaypoint != null)
                    {
                        spawnPos = targetWaypoint.WorldPosition;
                    }
                }
                spawnPending = true;
            }
        }

        private float GetMinDistanceToSub(Submarine submarine)
        {
            return Math.Max(Math.Max(submarine.Borders.Width, submarine.Borders.Height), Sonar.DefaultSonarRange * 0.9f);
        }

        public override void Update(float deltaTime)
        {
            if (disallowed)
            {
                Finished();
                return;
            }

            if (isFinished) { return; }

            if (spawnPos == null)
            {
                if (maxAmountPerLevel < int.MaxValue)
                {
                    if (Character.CharacterList.Count(c => c.SpeciesName == speciesName) >= maxAmountPerLevel)
                    {
                        disallowed = true;
                        return;
                    }
                }

                FindSpawnPosition(affectSubImmediately: true);
                //the event gets marked as finished if a spawn point is not found
                if (isFinished) { return; }
                spawnPending = true;
            }

            bool spawnReady = false;
            if (spawnPending)
            {
                //wait until there are no submarines at the spawnpos
                if (spawnPosType.HasFlag(Level.PositionType.MainPath) || spawnPosType.HasFlag(Level.PositionType.SidePath) || spawnPosType.HasFlag(Level.PositionType.Abyss))
                {
                    foreach (Submarine submarine in Submarine.Loaded)
                    {
                        if (submarine.Info.Type != SubmarineType.Player) { continue; }
                        float minDist = GetMinDistanceToSub(submarine);
                        if (Vector2.DistanceSquared(submarine.WorldPosition, spawnPos.Value) < minDist * minDist) { return; }
                    }
                }

                //if spawning in a ruin/cave, wait for someone to be close to it to spawning 
                //unnecessary monsters in places the players might never visit during the round
                if (spawnPosType.HasFlag(Level.PositionType.Ruin) || spawnPosType.HasFlag(Level.PositionType.Cave) || spawnPosType.HasFlag(Level.PositionType.Wreck))
                {
                    bool someoneNearby = false;
                    float minDist = Sonar.DefaultSonarRange * 0.8f;
                    foreach (Submarine submarine in Submarine.Loaded)
                    {
                        if (submarine.Info.Type != SubmarineType.Player) { continue; }
                        if (Vector2.DistanceSquared(submarine.WorldPosition, spawnPos.Value) < minDist * minDist)
                        {
                            someoneNearby = true;
                            break;
                        }
                    }
                    foreach (Character c in Character.CharacterList)
                    {
                        if (c == Character.Controlled || c.IsRemotePlayer)
                        {
                            if (Vector2.DistanceSquared(c.WorldPosition, spawnPos.Value) < minDist * minDist)
                            {
                                someoneNearby = true;
                                break;
                            }
                        }
                    }
                    if (!someoneNearby) { return; }
                }


                if (spawnPosType.HasFlag(Level.PositionType.Abyss) || spawnPosType.HasFlag(Level.PositionType.AbyssCave))
                {
                    bool anyInAbyss = false;
                    foreach (Submarine submarine in Submarine.Loaded)
                    {
                        if (submarine.Info.Type != SubmarineType.Player || submarine == GameMain.NetworkMember?.RespawnManager?.RespawnShuttle) { continue; }
                        if (submarine.WorldPosition.Y < 0)
                        {
                            anyInAbyss = true;
                            break;
                        }
                    }
                    if (!anyInAbyss) { return; }
                }

                spawnPending = false;

                //+1 because Range returns an integer less than the max value
                int amount = Rand.Range(minAmount, maxAmount + 1);
                monsters = new List<Character>();
                float scatterAmount = scatter;
                if (spawnPosType.HasFlag(Level.PositionType.SidePath))
                {
                    var sidePaths = Level.Loaded.Tunnels.Where(t => t.Type == Level.TunnelType.SidePath);
                    if (sidePaths.Any())
                    {
                        scatterAmount = Math.Min(scatter, sidePaths.Min(t => t.MinWidth) / 2);
                    }
                    else
                    {
                        scatterAmount = scatter;
                    }
                }
                else if (!spawnPosType.HasFlag(Level.PositionType.MainPath))
                {
                    scatterAmount = 0;
                }
                for (int i = 0; i < amount; i++)
                {
                    string seed = Level.Loaded.Seed + i.ToString();
                    CoroutineManager.Invoke(() =>
                    {
                        //round ended before the coroutine finished
                        if (GameMain.GameSession == null || Level.Loaded == null) { return; }
						
                        System.Diagnostics.Debug.Assert(GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer, "Clients should not create monster events.");

                        Vector2 pos = spawnPos.Value + Rand.Vector(scatterAmount);
                        if (scatterAmount > 0)
                        {
                            if (Submarine.Loaded.Any(s => ToolBox.GetWorldBounds(s.Borders.Center, s.Borders.Size).ContainsWorld(pos)))
                            {
                                // Can't use the offset position, let's use the exact spawn position.
                                pos = spawnPos.Value;
                            }
                            else if (Level.Loaded.Ruins.Any(r => ToolBox.GetWorldBounds(r.Area.Center, r.Area.Size).ContainsWorld(pos)))
                            {
                                // Can't use the offset position, let's use the exact spawn position.
                                pos = spawnPos.Value;
                            }
                        }

                        Character createdCharacter = Character.Create(speciesName, pos, seed, characterInfo: null, isRemotePlayer: false, hasAi: true, createNetworkEvent: true);
                        if (GameMain.GameSession.IsCurrentLocationRadiated())
                        {
                            AfflictionPrefab radiationPrefab = AfflictionPrefab.RadiationSickness;
                            Affliction affliction = new Affliction(radiationPrefab, radiationPrefab.MaxStrength);
                            createdCharacter?.CharacterHealth.ApplyAffliction(null, affliction);
                            // TODO test multiplayer
                            createdCharacter?.Kill(CauseOfDeathType.Affliction, affliction, log: false);
                        }
                        monsters.Add(createdCharacter);

                        if (monsters.Count == amount)
                        {
                            spawnReady = true;
                            //this will do nothing if the monsters have no swarm behavior defined, 
                            //otherwise it'll make the spawned characters act as a swarm
                            SwarmBehavior.CreateSwarm(monsters.Cast<AICharacter>());
                        }
                    }, Rand.Range(0f, amount / 2f));
                }
            }

            if (!spawnReady) { return; }

            Entity targetEntity = Submarine.FindClosest(GameMain.GameScreen.Cam.WorldViewCenter);
#if CLIENT
            if (Character.Controlled != null) { targetEntity = Character.Controlled; }
#endif
            
            bool monstersDead = true;
            foreach (Character monster in monsters)
            {
                if (!monster.IsDead)
                {
                    monstersDead = false;

                    if (targetEntity != null && Vector2.DistanceSquared(monster.WorldPosition, targetEntity.WorldPosition) < 5000.0f * 5000.0f)
                    {
                        break;
                    }
                }
            }

            if (monstersDead) { Finished(); }
        }
    }
}
