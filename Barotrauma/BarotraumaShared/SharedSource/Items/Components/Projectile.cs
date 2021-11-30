﻿using Barotrauma.Networking;
using FarseerPhysics;
using FarseerPhysics.Dynamics;
using FarseerPhysics.Dynamics.Contacts;
using FarseerPhysics.Dynamics.Joints;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Voronoi2;

namespace Barotrauma.Items.Components
{
    partial class Projectile : ItemComponent, IServerSerializable
    {
        struct HitscanResult
        {
            public Fixture Fixture;
            public Vector2 Point;
            public Vector2 Normal;
            public float Fraction;
            public HitscanResult(Fixture fixture, Vector2 point, Vector2 normal, float fraction)
            {
                Fixture = fixture;
                Point = point;
                Normal = normal;
                Fraction = fraction;
            }
        }
        struct Impact
        {
            public Fixture Fixture;
            public Vector2 Normal;
            public Vector2 LinearVelocity;

            public Impact(Fixture fixture, Vector2 normal, Vector2 velocity)
            {
                Fixture = fixture;
                Normal = normal;
                LinearVelocity = velocity;
            }
        }

        private readonly Queue<Impact> impactQueue = new Queue<Impact>();

        private bool removePending;

        //continuous collision detection is used while the projectile is moving faster than this
        const float ContinuousCollisionThreshold = 5.0f;

        //a duration during which the projectile won't drop from the body it's stuck to
        private const float PersistentStickJointDuration = 1.0f;
        private PrismaticJoint stickJoint;

        public Attack Attack { get; private set; }

        private Vector2 launchPos;

        private readonly HashSet<Body> hits = new HashSet<Body>();

        public List<Body> IgnoredBodies;

        private Character _user;
        public Character User
        {
            get { return _user; }
            set
            {
                _user = value;
                Attack?.SetUser(_user);                
            }
        }

        public Character Attacker { get; set; }

        public IEnumerable<Body> Hits
        {
            get { return hits; }
        }

        private float persistentStickJointTimer;

        [Serialize(10.0f, false, description: "The impulse applied to the physics body of the item when it's launched. Higher values make the projectile faster.")]
        public float LaunchImpulse { get; set; }

        [Serialize(0.0f, false, description: "The random percentage modifier used to add variance to the launch impulse.")]
        public float ImpulseSpread { get; set; }

        [Serialize(0.0f, false, description: "The rotation of the item relative to the rotation of the weapon when launched (in degrees).")]

        public float LaunchRotation
        {
            get { return MathHelper.ToDegrees(LaunchRotationRadians); }
            set { LaunchRotationRadians = MathHelper.ToRadians(value); }
        }

        public float LaunchRotationRadians
        {
            get;
            private set;
        }

        [Serialize(false, false, description: "When set to true, the item can stick to any target it hits.")]
        //backwards compatibility, can stick to anything
        public bool DoesStick
        {
            get;
            set;
        }

        [Serialize(false, false, description: "When set to true, the item won't fall of a target it's stuck to unless removed.")]
        public bool StickPermanently
        {
            get;
            set;
        }

        [Serialize(false, false, description: "Can the item stick to the character it hits.")]
        public bool StickToCharacters
        {
            get;
            set;
        }

        [Serialize(false, false, description: "Can the item stick to the structure it hits.")]
        public bool StickToStructures
        {
            get;
            set;
        }

        [Serialize(false, false, description: "Can the item stick to the item it hits.")]
        public bool StickToItems
        {
            get;
            set;
        }

        [Serialize(false, false, description: "Can the item stick even to deflective targets.")]
        public bool StickToDeflective
        {
            get;
            set;
        }

        [Serialize(false, false, description: "Hitscan projectiles cast a ray forwards and immediately hit whatever the ray hits. "+
            "It is recommended to use hitscans for very fast-moving projectiles such as bullets, because using extremely fast launch velocities may cause physics glitches.")]
        public bool Hitscan
        {
            get;
            set;
        }

        [Serialize(1, false, description: "How many hitscans should be done when the projectile is launched. "
            + "Multiple hitscans can be used to simulate weapons that fire multiple projectiles at the same time" +
            " without having to actually use multiple projectile items, for example shotguns.")]
        public int HitScanCount
        {
            get;
            set;
        }

        [Serialize(1, false, description: "How many targets the projectile can hit before it stops.")]
        public int MaxTargetsToHit
        {
            get;
            set;
        }

        [Serialize(false, false, description: "Should the item be deleted when it hits something.")]
        public bool RemoveOnHit
        {
            get;
            set;
        }

        [Serialize(0.0f, false, description: "Random spread applied to the launch angle of the projectile (in degrees).")]
        public float Spread
        {
            get;
            set;
        }

        [Serialize(false, false, description: "Override random spread with static spread; hitscan are launched with an equal amount of angle between them. Only applies when firing multiple hitscan.")]
        public bool StaticSpread
        {
            get;
            set;
        }

        public Body StickTarget 
        { 
            get; 
            private set; 
        }

        public bool IsStuckToTarget
        {
            get { return StickTarget != null; }
        }

        public Projectile(Item item, XElement element) 
            : base (item, element)
        {
            IgnoredBodies = new List<Body>();

            foreach (XElement subElement in element.Elements())
            {
                if (!subElement.Name.ToString().Equals("attack", StringComparison.OrdinalIgnoreCase)) { continue; }
                Attack = new Attack(subElement, item.Name + ", Projectile", item);
            }
            InitProjSpecific(element);
        }
        partial void InitProjSpecific(XElement element);

        public override void OnItemLoaded()
        {
            if (Attack != null && Attack.DamageRange <= 0.0f && item.body != null)
            {
                switch (item.body.BodyShape)
                {
                    case PhysicsBody.Shape.Circle:
                        Attack.DamageRange = item.body.radius;
                        break;
                    case PhysicsBody.Shape.Capsule:
                        Attack.DamageRange = item.body.height / 2 + item.body.radius;
                        break;
                    case PhysicsBody.Shape.Rectangle:
                        Attack.DamageRange = new Vector2(item.body.width / 2.0f, item.body.height / 2.0f).Length();
                        break;
                }
                Attack.DamageRange = ConvertUnits.ToDisplayUnits(Attack.DamageRange);
            }
        }

        private void Launch(Character user, Vector2 simPosition, float rotation, float damageMultiplier = 1f)
        {
            Item.body.ResetDynamics();
            Item.SetTransform(simPosition, rotation);
            if (Attack != null)
            {
                Attack.DamageMultiplier = damageMultiplier;
            }
            // Set user for hitscan projectiles to work properly.
            User = user;
            // Need to set null for non-characterusable items.
            Use(character: null);
            // Set user for normal projectiles to work properly.
            User = user;
            if (Item.Removed) { return; }
            launchPos = simPosition;
            //set the rotation of the projectile again because dropping the projectile resets the rotation
            Item.SetTransform(simPosition, rotation + (Item.body.Dir * LaunchRotationRadians));
        }

        public void Shoot(Character user, Vector2 weaponPos, Vector2 spawnPos, float rotation, List<Body> ignoredBodies, bool createNetworkEvent, float damageMultiplier = 1f)
        {
            //add the limbs of the shooter to the list of bodies to be ignored
            //so that the player can't shoot himself
            IgnoredBodies = ignoredBodies;
            Vector2 projectilePos = weaponPos;
            //make sure there's no obstacles between the base of the weapon (or the shoulder of the character) and the end of the barrel
            if (Submarine.PickBody(weaponPos, spawnPos, IgnoredBodies, Physics.CollisionWall | Physics.CollisionLevel | Physics.CollisionItemBlocking) == null)
            {
                //no obstacles -> we can spawn the projectile at the barrel
                projectilePos = spawnPos;
            }
            else if ((weaponPos - spawnPos).LengthSquared() > 0.0001f)
            {
                //spawn the projectile body.GetMaxExtent() away from the position where the raycast hit the obstacle
                Vector2 newPos = weaponPos - Vector2.Normalize(spawnPos - projectilePos) * Math.Max(Item.body.GetMaxExtent(), 0.1f);
                if (MathUtils.IsValid(newPos))
                {
                    projectilePos = newPos;
                }
            }
            Launch(user, projectilePos, rotation, damageMultiplier);
            if (createNetworkEvent && !Item.Removed && GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
            {
#if SERVER
                launchRot = rotation;               
                Item.CreateServerEvent(this, new object[] { true }); //true = indicate that this is a launch event          
#endif
            }
        }

        public bool Use(Character character = null)
        {
            if (character != null && !characterUsable) { return false; }

            for (int i = 0; i < HitScanCount; i++)
            {
                float launchAngle;
                
                if (StaticSpread)
                {
                    float staticSpread = Spread / (HitScanCount - 1);
                    // because the position of the item changes as hitscan are fired, we will set an
                    // initial offset on the first hitscan and then increase the item's angle by a set amount as hitscan are fired
                    float offset = i == 0 ? -staticSpread * (HitScanCount -1) : 0f; 
                    launchAngle = item.body.Rotation + MathHelper.ToRadians(staticSpread + offset);
                }
                else
                {
                    launchAngle = item.body.Rotation + MathHelper.ToRadians(Spread * Rand.Range(-0.5f, 0.5f));
                }

                Vector2 launchDir = new Vector2((float)Math.Cos(launchAngle), (float)Math.Sin(launchAngle));
                if (Hitscan)
                {
                    Vector2 prevSimpos = item.SimPosition;
                    item.body.SetTransformIgnoreContacts(item.body.SimPosition, launchAngle);
                    DoHitscan(launchDir);
                    if (i < HitScanCount - 1)
                    {
                        item.SetTransform(prevSimpos, item.body.Rotation);
                    }
                }
                else
                {
                    item.body.SetTransform(item.body.SimPosition, launchAngle);
                    float modifiedLaunchImpulse = LaunchImpulse * (1 + Rand.Range(-ImpulseSpread, ImpulseSpread));
                    DoLaunch(launchDir * modifiedLaunchImpulse * item.body.Mass);
                }
            }
            User = character;
            return true;
        }

        public override bool Use(float deltaTime, Character character = null) => Use(character);

        private void DoLaunch(Vector2 impulse)
        {
            hits.Clear();

            if (item.AiTarget != null)
            {
                item.AiTarget.SightRange = item.AiTarget.MaxSightRange;
                item.AiTarget.SoundRange = item.AiTarget.MaxSoundRange;
            }

            item.Drop(null, createNetworkEvent: false);

            launchPos = item.SimPosition;

            item.body.Enabled = true;            
            item.body.ApplyLinearImpulse(impulse, maxVelocity: NetConfig.MaxPhysicsBodyVelocity * 0.9f);
            
            item.body.FarseerBody.OnCollision += OnProjectileCollision;
            item.body.FarseerBody.IsBullet = true;

            item.body.CollisionCategories = Physics.CollisionProjectile;
            item.body.CollidesWith = Physics.CollisionCharacter | Physics.CollisionWall | Physics.CollisionLevel;

            IsActive = true;

            if (stickJoint == null) { return; }

            StickTarget = null;            
            GameMain.World.Remove(stickJoint);
            stickJoint = null;
        }
        
        private void DoHitscan(Vector2 dir)
        {
            float rotation = item.body.Rotation;
            Vector2 simPositon = item.SimPosition;
            Vector2 rayStartWorld = item.WorldPosition;
            item.Drop(null);

            item.body.Enabled = true;
            //set the velocity of the body because the OnProjectileCollision method
            //uses it to determine the direction from which the projectile hit
            item.body.LinearVelocity = dir;
            IsActive = true;

            Vector2 rayStart = simPositon;
            Vector2 rayEnd = rayStart + dir * 500.0f;

            float worldDist = 1000.0f;
#if CLIENT
            worldDist = Screen.Selected?.Cam?.WorldView.Width ?? GameMain.GraphicsWidth;
#endif
            Vector2 rayEndWorld = rayStartWorld + dir * worldDist;

            List<HitscanResult> hits = new List<HitscanResult>();

            hits.AddRange(DoRayCast(rayStart, rayEnd, submarine: item.Submarine));

            if (item.Submarine != null)
            {
                //shooting indoors, do a hitscan outside as well
                hits.AddRange(DoRayCast(rayStart + item.Submarine.SimPosition, rayEnd + item.Submarine.SimPosition, submarine: null));
                //also in the coordinate space of docked subs
                foreach (Submarine dockedSub in item.Submarine.DockedTo)
                {
                    if (dockedSub == item.Submarine) { continue; }
                    hits.AddRange(DoRayCast(rayStart + item.Submarine.SimPosition - dockedSub.SimPosition, rayEnd + item.Submarine.SimPosition - dockedSub.SimPosition, dockedSub));
                }
            }
            else
            {
                //shooting outdoors, see if we can hit anything inside a sub
                foreach (Submarine submarine in Submarine.Loaded)
                {
                    var inSubHits = DoRayCast(rayStart - submarine.SimPosition, rayEnd - submarine.SimPosition, submarine);
                    //transform back to world coordinates
                    for (int i = 0; i < inSubHits.Count; i++)
                    {
                        inSubHits[i] = new HitscanResult(
                            inSubHits[i].Fixture, 
                            inSubHits[i].Point + submarine.SimPosition, 
                            inSubHits[i].Normal, 
                            inSubHits[i].Fraction);
                    }

                    hits.AddRange(inSubHits);
                }
            }

            int hitCount = 0;
            Vector2 lastHitPos = item.WorldPosition;
            hits = hits.OrderBy(h => h.Fraction).ToList();
            for (int i = 0; i < hits.Count; i++)
            {
                var h = hits[i];
                item.SetTransform(h.Point, rotation);
                if (HandleProjectileCollision(h.Fixture, h.Normal, Vector2.Zero))
                {
                    hitCount++;
                    if (hitCount >= MaxTargetsToHit || i == hits.Count - 1)
                    {
                        LaunchProjSpecific(rayStartWorld, item.WorldPosition);
                        break;
                    }
                }
            }
            //the raycast didn't hit anything (or didn't hit enough targets to stop the projectile) -> the projectile flew somewhere outside the level and is permanently lost
            if (hitCount < MaxTargetsToHit)
            {
                item.body.SetTransformIgnoreContacts(item.body.SimPosition, rotation);
                LaunchProjSpecific(rayStartWorld, rayEndWorld);
                if (Entity.Spawner == null)
                {
                    item.Remove();
                }
                else
                {
                    if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsClient)
                    {
                        //clients aren't allowed to remove items by themselves, so lets hide the projectile until the server tells us to remove it
                        item.HiddenInGame = Hitscan;
                    }
                    else
                    {
                        Entity.Spawner.AddToRemoveQueue(item);
                    }
                }
            }
        }
        
        private List<HitscanResult> DoRayCast(Vector2 rayStart, Vector2 rayEnd, Submarine submarine)
        {
            List<HitscanResult> hits = new List<HitscanResult>();

            Vector2 dir = rayEnd - rayStart;
            dir = dir.LengthSquared() < 0.00001f ? Vector2.UnitY : Vector2.Normalize(dir);

            //do an AABB query first to see if the start of the ray is inside a fixture
            var aabb = new FarseerPhysics.Collision.AABB(rayStart - Vector2.One * 0.001f, rayStart + Vector2.One * 0.001f);
            GameMain.World.QueryAABB((fixture) =>
            {
                if (fixture?.Body.UserData is LevelObject levelObj)
                {
                    if (!levelObj.Prefab.TakeLevelWallDamage) { return true; }
                }
                else if (fixture?.Body == null || fixture.IsSensor) 
                { 
                    //ignore sensors and items
                    return true; 
                }
                if (fixture.Body.UserData is VineTile) { return true; }
                if (fixture.Body.UserData is Item item && (item.GetComponent<Door>() == null && !item.Prefab.DamagedByProjectiles || item.Condition <= 0)) { return true; }
                if (fixture.Body.UserData as string == "ruinroom" || fixture.Body.UserData is Hull || fixture.UserData is Hull) { return true; }

                //if doing the raycast in a submarine's coordinate space, ignore anything that's not in that sub
                if (submarine != null)
                {
                    if (fixture.Body.UserData is VoronoiCell) { return true; }
                    if (fixture.Body.UserData is Entity entity && entity.Submarine != submarine) { return true; }
                }

                //ignore everything else than characters, sub walls and level walls
                if (!fixture.CollisionCategories.HasFlag(Physics.CollisionCharacter) &&
                    !fixture.CollisionCategories.HasFlag(Physics.CollisionWall) &&
                    !fixture.CollisionCategories.HasFlag(Physics.CollisionLevel)) { return true; }

                if (fixture.Body.UserData is VoronoiCell && (this.item.Submarine != null || submarine != null)) { return true; }

                fixture.Body.GetTransform(out FarseerPhysics.Common.Transform transform);
                if (!fixture.Shape.TestPoint(ref transform, ref rayStart)) { return true; }

                hits.Add(new HitscanResult(fixture, rayStart, -dir, 0.0f));
                return true;
            }, ref aabb);

            GameMain.World.RayCast((fixture, point, normal, fraction) =>
            {
                //ignore sensors and items
                if (fixture?.Body.UserData is LevelObject levelObj)
                {
                    if (!levelObj.Prefab.TakeLevelWallDamage) { return -1; }
                }
                else if (fixture?.Body == null || fixture.IsSensor)
                {
                    //ignore sensors and items
                    return -1;
                }
                if (fixture.Body.UserData is VineTile) { return -1; }

                if (fixture.Body.UserData is Item item && (item.GetComponent<Door>() == null && !item.Prefab.DamagedByProjectiles || item.Condition <= 0)) { return -1; }
                if (fixture.Body.UserData as string == "ruinroom" || fixture.Body?.UserData is Hull || fixture.UserData is Hull) { return -1; }

                //ignore everything else than characters, sub walls and level walls
                if (!fixture.CollisionCategories.HasFlag(Physics.CollisionCharacter) &&
                    !fixture.CollisionCategories.HasFlag(Physics.CollisionWall) &&
                    !fixture.CollisionCategories.HasFlag(Physics.CollisionLevel)) { return -1; }

                //if doing the raycast in a submarine's coordinate space, ignore anything that's not in that sub
                if (submarine != null)
                {
                    if (fixture.Body.UserData is VoronoiCell) { return -1; }
                    if (fixture.Body.UserData is Entity entity && entity.Submarine != submarine) { return -1; }
                }

                //ignore level cells if the item and the point of impact are inside a sub
                if (fixture.Body.UserData is VoronoiCell) 
                { 
                    if (Hull.FindHull(ConvertUnits.ToDisplayUnits(point), this.item.CurrentHull) != null && this.item.Submarine != null)
                    {
                        return -1;
                    }
                }

                if (hits.Count > 50)
                {
                    float furthestHit = 0.0f;
                    int furthestHitIndex = -1;
                    for (int i = 0; i < hits.Count; i++)
                    {
                        if (hits[i].Fraction > furthestHit)
                        {
                            furthestHitIndex = i;
                            furthestHit = hits[i].Fraction;
                        }
                    }
                    if (furthestHitIndex > -1)
                    {
                        hits.RemoveAt(furthestHitIndex);
                    }
                }

                hits.Add(new HitscanResult(fixture, point, normal, fraction));

                return 1;
            }, rayStart, rayEnd, Physics.CollisionCharacter | Physics.CollisionWall | Physics.CollisionLevel);

            return hits;
        }

        public override void Drop(Character dropper)
        {
            if (dropper != null)
            {
                Deactivate();
                Unstick();
            }
            base.Drop(dropper);
        }

        public override void Update(float deltaTime, Camera cam)
        {
            while (impactQueue.Count > 0)
            {
                var impact = impactQueue.Dequeue();
                HandleProjectileCollision(impact.Fixture, impact.Normal, impact.LinearVelocity);
            }

            if (!removePending)
            {
                Entity useTarget = lastTarget?.Body.UserData is Limb limb ? limb.character : lastTarget?.Body.UserData as Entity;
                ApplyStatusEffects(ActionType.OnActive, deltaTime, useTarget: useTarget, user: _user);
            }

            if (item.body != null && item.body.FarseerBody.IsBullet)
            {
                if (item.body.LinearVelocity.LengthSquared() < ContinuousCollisionThreshold * ContinuousCollisionThreshold)
                {
                    item.body.FarseerBody.IsBullet = false;
                }
            }
            //projectiles with a stickjoint don't become inactive until the stickjoint is detached
            if (stickJoint == null && !item.body.FarseerBody.IsBullet) 
            { 
                IsActive = false; 
            }

            if (stickJoint == null) { return; }

            if (persistentStickJointTimer > 0.0f && !StickPermanently)
            {
                persistentStickJointTimer -= deltaTime;
                return;
            }

            //target very far from the item -> update the item's transform to make sure it's inside the same sub as the target (or outside)
            if (Math.Abs(stickJoint.JointTranslation) > 100.0f)
            {
                item.UpdateTransform();
            }

            if (GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer)
            {
                if (StickTargetRemoved() ||
                    (!StickPermanently && (stickJoint.JointTranslation < stickJoint.LowerLimit * 0.9f || stickJoint.JointTranslation > stickJoint.UpperLimit * 0.9f)) ||
                    Math.Abs(stickJoint.JointTranslation) > 100.0f) //failsafe unstick if the target is still extremely far
                {
                    Unstick();
#if SERVER
                    item.CreateServerEvent(this);                
#endif
                }
            }
        }

        private bool StickTargetRemoved()
        {
            if (StickTarget == null) { return true; }
            if (StickTarget.UserData is Limb limb) { return limb.character.Removed; }
            if (StickTarget.UserData is Entity entity) { return entity.Removed; }
            return false;
        }

        private bool OnProjectileCollision(Fixture f1, Fixture target, Contact contact)
        {
            if (User != null && User.Removed) { User = null; return false; }
            if (IgnoredBodies.Contains(target.Body)) { return false; }
            //ignore character colliders (the projectile only hits limbs)
            if (target.CollisionCategories == Physics.CollisionCharacter && target.Body.UserData is Character)
            {
                return false;
            }
            if (hits.Contains(target.Body)) { return false; }
            if (target.Body.UserData is Submarine sub)
            {
                Vector2 dir = item.body.LinearVelocity.LengthSquared() < 0.001f ?
                    contact.Manifold.LocalNormal : Vector2.Normalize(item.body.LinearVelocity);

                //do a raycast in the sub's coordinate space to see if it hit a structure
                var wallBody = Submarine.PickBody(
                    item.body.SimPosition - ConvertUnits.ToSimUnits(sub.Position) - dir,
                    item.body.SimPosition - ConvertUnits.ToSimUnits(sub.Position) + dir,
                    collisionCategory: Physics.CollisionWall);
                if (wallBody?.FixtureList?.First() != null && (wallBody.UserData is Structure || wallBody.UserData is Item) &&
                    //ignore the hit if it's behind the position the item was launched from, and the projectile is travelling in the opposite direction
                    Vector2.Dot(item.body.SimPosition - launchPos, dir) > 0) 
                {
                    target = wallBody.FixtureList.First();
                    if (hits.Contains(target.Body)) { return false; }
                }
                else
                {
                    return false;
                }
            }
            else if (target.Body.UserData is Limb limb)
            {
                if (limb.IsSevered)
                {
                    //push the severed limb around a bit, but let the projectile pass through it
                    limb.body?.ApplyLinearImpulse(item.body.LinearVelocity * item.body.Mass * 0.1f, item.SimPosition);
                    return false;
                }
            }
            else if (target.Body.UserData is Item item)
            {
                if (item.Condition <= 0.0f) { return false; }
            }

            //ignore character colliders (the projectile only hits limbs)
            if (target.CollisionCategories == Physics.CollisionCharacter && target.Body.UserData is Character)
            {
                return false;
            }

            hits.Add(target.Body);
            impactQueue.Enqueue(new Impact(target, contact.Manifold.LocalNormal, item.body.LinearVelocity));
            IsActive = true;
            if (RemoveOnHit)
            {
                item.body.FarseerBody.ResetDynamics();
            }
            if (hits.Count() >= MaxTargetsToHit || target.Body.UserData is VoronoiCell)
            {
                Deactivate();
                return true;
            }
            else
            {
                return false;
            }
        }

        private readonly List<ISerializableEntity> targets = new List<ISerializableEntity>();
        private Fixture lastTarget;

        private bool HandleProjectileCollision(Fixture target, Vector2 collisionNormal, Vector2 velocity)
        {
            if (User != null && User.Removed) { User = null; }
            if (IgnoredBodies.Contains(target.Body)) { return false; }
            //ignore character colliders (the projectile only hits limbs)
            if (target.CollisionCategories == Physics.CollisionCharacter && target.Body.UserData is Character)
            {
                return false;
            }
            lastTarget = target;

            float projectileNewSpeed = 0.5f;
            float projectileDeflectedNewSpeed = 0.1f;

            AttackResult attackResult = new AttackResult();
            Character character = null;
            if (target.Body.UserData is Submarine submarine)
            {
                item.Move(-submarine.Position);
                item.Submarine = submarine;
                item.body.Submarine = submarine;
                return !Hitscan;
            }
            else if (target.Body.UserData is Limb limb)
            {
                // when hitting limbs with piercing ammo, don't lose as much speed
                if (MaxTargetsToHit > 1)
                {
                    projectileNewSpeed = 1f;
                    projectileDeflectedNewSpeed = 0.8f;
                }
                if (limb.IsSevered || limb.character == null || limb.character.Removed) { return false; }

                limb.character.LastDamageSource = item;
                if (Attack != null) { attackResult = Attack.DoDamageToLimb(User ?? Attacker, limb, item.WorldPosition, 1.0f); }
                if (limb.character != null) { character = limb.character; }
            }
            else if (target.Body.UserData is Item targetItem)
            {
                if (targetItem.Removed) { return false; }
                if (Attack != null && targetItem.Prefab.DamagedByProjectiles && targetItem.Condition > 0) 
                {
                    attackResult = Attack.DoDamage(User ?? Attacker, targetItem, item.WorldPosition, 1.0f); 
                }
            }
            else if (target.Body.UserData is IDamageable damageable)
            {
                if (Attack != null) 
                {
                    Vector2 pos = item.WorldPosition;
                    if (item.Submarine == null && damageable is Structure structure && structure.Submarine != null && Vector2.DistanceSquared(item.WorldPosition, structure.WorldPosition) > 10000.0f * 10000.0f)
                    {
                        item.Submarine = structure.Submarine;
                    }
                    attackResult = Attack.DoDamage(User ?? Attacker, damageable, pos, 1.0f); 
                }
            }
            else if (target.Body.UserData is VoronoiCell voronoiCell && voronoiCell.IsDestructible && Attack != null && Math.Abs(Attack.LevelWallDamage) > 0.0f)
            {
                if (Level.Loaded?.ExtraWalls.Find(w => w.Body == target.Body) is DestructibleLevelWall destructibleWall)
                {
                    attackResult = Attack.DoDamage(User ?? Attacker, destructibleWall, item.WorldPosition, 1.0f);
                }
            }

            if (character != null) { character.LastDamageSource = item; }

            ActionType actionType = ActionType.OnUse;
            if (_user != null && Rand.Range(0.0f, 0.5f) > DegreeOfSuccess(_user))
            {
                actionType = ActionType.OnFailure;
            }

#if CLIENT
            PlaySound(actionType, user: _user);
            PlaySound(ActionType.OnImpact, user: _user);
#endif

            if (GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer)
            {
                if (target.Body.UserData is Limb targetLimb)
                {
                    ApplyStatusEffects(actionType, 1.0f, character, targetLimb, user: _user);
                    ApplyStatusEffects(ActionType.OnImpact, 1.0f, character, targetLimb, user: _user);
                    var attack = targetLimb.attack;
                    if (attack != null)
                    {
                        // Apply the status effects defined in the limb's attack that was hit
                        foreach (var effect in attack.StatusEffects)
                        {
                            if (effect.type == ActionType.OnImpact)
                            {
                                //effect.Apply(effect.type, 1.0f, targetLimb.character, targetLimb.character, targetLimb.WorldPosition);

                                if (effect.HasTargetType(StatusEffect.TargetType.This))
                                {
                                    effect.Apply(effect.type, 1.0f, targetLimb.character, targetLimb.character, targetLimb.WorldPosition);
                                }
                                if (effect.HasTargetType(StatusEffect.TargetType.NearbyItems) ||
                                    effect.HasTargetType(StatusEffect.TargetType.NearbyCharacters))
                                {
                                    targets.Clear();
                                    targets.AddRange(effect.GetNearbyTargets(targetLimb.WorldPosition, targets));
                                    effect.Apply(ActionType.OnActive, 1.0f, targetLimb.character, targets);
                                }

                            }
                        }
                    }
#if SERVER
                    if (GameMain.NetworkMember.IsServer)
                    {
                        GameMain.Server?.CreateEntityEvent(item, new object[] { NetEntityEvent.Type.ApplyStatusEffect, actionType, this, targetLimb.character.ID, targetLimb, (ushort)0, item.WorldPosition });
                        GameMain.Server?.CreateEntityEvent(item, new object[] { NetEntityEvent.Type.ApplyStatusEffect, ActionType.OnImpact, this, targetLimb.character.ID, targetLimb, (ushort)0, item.WorldPosition });
                    }
#endif
                }
                else
                {
                    ApplyStatusEffects(actionType, 1.0f, useTarget: target.Body.UserData as Entity, user: _user);
                    ApplyStatusEffects(ActionType.OnImpact, 1.0f, useTarget: target.Body.UserData as Entity, user: _user);
#if SERVER
                    if (GameMain.NetworkMember.IsServer)
                    {
                        GameMain.Server?.CreateEntityEvent(item, new object[] { NetEntityEvent.Type.ApplyStatusEffect, actionType, this, (ushort)0, null, (target.Body.UserData as Entity)?.ID ?? 0, item.WorldPosition });
                        GameMain.Server?.CreateEntityEvent(item, new object[] { NetEntityEvent.Type.ApplyStatusEffect, ActionType.OnImpact, this, (ushort)0, null, (target.Body.UserData as Entity)?.ID ?? 0, item.WorldPosition });
                    }
#endif
                }
            }

            target.Body.ApplyLinearImpulse(velocity * item.body.Mass);
            target.Body.LinearVelocity = target.Body.LinearVelocity.ClampLength(NetConfig.MaxPhysicsBodyVelocity * 0.5f);

            if (hits.Count() >= MaxTargetsToHit || hits.LastOrDefault()?.UserData is VoronoiCell)
            {
                Deactivate();
            }

            if (attackResult.AppliedDamageModifiers != null &&
                (attackResult.AppliedDamageModifiers.Any(dm => dm.DeflectProjectiles) && !StickToDeflective))
            {
                item.body.LinearVelocity *= projectileDeflectedNewSpeed;
            }
            else if (   // When hitting characters the collision normal seems to sometimes point into wrong direction, resulting in a failed attempt to stick
                        //Vector2.Dot(Vector2.Normalize(velocity), collisionNormal) < 0.0f && 
                        hits.Count() >= MaxTargetsToHit &&
                        target.Body.Mass > item.body.Mass * 0.5f &&
                        (DoesStick ||
                        (StickToCharacters && (target.Body.UserData is Limb || target.Body.UserData is Character)) ||
                        (StickToStructures && target.Body.UserData is Structure) ||
                        (StickToItems && target.Body.UserData is Item)))                
            {
                Vector2 dir = new Vector2(
                    (float)Math.Cos(item.body.Rotation),
                    (float)Math.Sin(item.body.Rotation));

                if (GameMain.NetworkMember == null || GameMain.NetworkMember.IsServer)
                {
                    if (target.Body.UserData is Structure structure && structure.Submarine != item.Submarine && structure.Submarine != null)
                    {
                        StickToTarget(structure.Submarine.PhysicsBody.FarseerBody, dir);
                    }
                    else
                    {
                        StickToTarget(target.Body, dir);
                    }   
                }
#if SERVER
                if (GameMain.NetworkMember != null && GameMain.NetworkMember.IsServer)
                {
                    item.CreateServerEvent(this);
                }
#endif
                item.body.LinearVelocity *= projectileNewSpeed;

                return Hitscan;                
            }
            else
            {
                item.body.LinearVelocity *= projectileNewSpeed;
            }

            var containedItems = item.OwnInventory?.AllItems;
            if (containedItems != null)
            {
                foreach (Item contained in containedItems)
                {
                    if (contained.body != null)
                    {
                        contained.SetTransform(item.SimPosition, contained.body.Rotation);
                    }
                }
            }

            if (RemoveOnHit)
            {
                removePending = true;
                item.HiddenInGame = true;
                item.body.FarseerBody.Enabled = false;
                Entity.Spawner?.AddToRemoveQueue(item);                
            }

            return true;
        }

        private void Deactivate()
        {
            item.body.FarseerBody.OnCollision -= OnProjectileCollision;
            if ((item.Prefab.DamagedByProjectiles || item.Prefab.DamagedByMeleeWeapons) && item.Condition > 0)
            {
                item.body.CollisionCategories = Physics.CollisionCharacter;
                item.body.CollidesWith = Physics.CollisionWall | Physics.CollisionLevel | Physics.CollisionPlatform | Physics.CollisionProjectile;
            }
            else
            {
                item.body.CollisionCategories = Physics.CollisionItem;
                item.body.CollidesWith = Physics.CollisionWall | Physics.CollisionLevel;
            }
            IgnoredBodies.Clear();
        }

        private void StickToTarget(Body targetBody, Vector2 axis)
        {
            if (stickJoint != null) { return; }

            stickJoint = new PrismaticJoint(targetBody, item.body.FarseerBody, item.body.SimPosition, axis, true)
            {
                MotorEnabled = true,
                MaxMotorForce = 30.0f,
                LimitEnabled = true,
                Breakpoint = 1000.0f
            };

            if (StickPermanently)
            {
                stickJoint.LowerLimit = stickJoint.UpperLimit = 0.0f;
                item.body.ResetDynamics();
            }
            else if (item.Sprite != null)
            {
                stickJoint.LowerLimit = ConvertUnits.ToSimUnits(item.Sprite.size.X * -0.3f * item.Scale);
                stickJoint.UpperLimit = ConvertUnits.ToSimUnits(item.Sprite.size.X * 0.3f * item.Scale);
            }

            persistentStickJointTimer = PersistentStickJointDuration;
            StickTarget = targetBody;
            GameMain.World.Add(stickJoint);

            IsActive = true;
        }

        private void Unstick()
        {
            StickTarget = null;
            if (stickJoint != null)
            {
                if (GameMain.World.JointList.Contains(stickJoint))
                {
                    GameMain.World.Remove(stickJoint);
                }
                stickJoint = null;
            }
            if (!item.body.FarseerBody.IsBullet) { IsActive = false; }
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();
            if (stickJoint != null)
            {
                try
                {
                    GameMain.World.Remove(stickJoint);
                }
                catch
                {
                    //the body that the projectile was stuck to has been removed
                }

                stickJoint = null;
            }

        }
        partial void LaunchProjSpecific(Vector2 startLocation, Vector2 endLocation);
    }
}
