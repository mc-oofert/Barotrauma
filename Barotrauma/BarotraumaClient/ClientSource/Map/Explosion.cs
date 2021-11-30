﻿using Barotrauma.Lights;
using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace Barotrauma
{
    partial class Explosion
    {
        partial void ExplodeProjSpecific(Vector2 worldPosition, Hull hull)
        {
            if (GameMain.Client?.MidRoundSyncing ?? false) { return; }

            if (shockwave)
            {
                GameMain.ParticleManager.CreateParticle("shockwave", worldPosition,
                    Vector2.Zero, 0.0f, hull);
            }

            hull ??= Hull.FindHull(worldPosition, useWorldCoordinates: true);
            bool underwater = hull == null || worldPosition.Y < hull.WorldSurface;

            if (underwater && underwaterBubble)
            {
                var underwaterExplosion = GameMain.ParticleManager.CreateParticle("underwaterexplosion", worldPosition, Vector2.Zero, 0.0f, hull);
                if (underwaterExplosion != null)
                {
                    underwaterExplosion.Size *= MathHelper.Clamp(Attack.Range / 150.0f, 0.5f, 10.0f);
                    underwaterExplosion.StartDelay = 0.0f;
                }
            }

            for (int i = 0; i < Attack.Range * 0.1f; i++)
            {
                if (!underwater)
                {
                    float particleSpeed = Rand.Range(0.0f, 1.0f);
                    particleSpeed = particleSpeed * particleSpeed * Attack.Range;

                    if (flames)
                    {
                        float particleScale = MathHelper.Clamp(Attack.Range * 0.0025f, 0.5f, 2.0f);
                        var flameParticle = GameMain.ParticleManager.CreateParticle("explosionfire",
                            ClampParticlePos(worldPosition + Rand.Vector((float)System.Math.Sqrt(Rand.Range(0.0f, Attack.Range))), hull),
                            Rand.Vector(Rand.Range(0.0f, particleSpeed)), 0.0f, hull);
                        if (flameParticle != null) flameParticle.Size *= particleScale;
                    }
                    if (smoke)
                    {
                        GameMain.ParticleManager.CreateParticle(Rand.Range(0.0f, 1.0f) < 0.5f ? "explosionsmoke" : "smoke",
                            ClampParticlePos(worldPosition + Rand.Vector((float)System.Math.Sqrt(Rand.Range(0.0f, Attack.Range))), hull),
                            Rand.Vector(Rand.Range(0.0f, particleSpeed)), 0.0f, hull);
                    }
                }
                else if (underwaterBubble)
                {
                    Vector2 bubblePos = Rand.Vector(Rand.Range(0.0f, Attack.Range * 0.5f));

                    GameMain.ParticleManager.CreateParticle("risingbubbles", worldPosition + bubblePos,
                        Vector2.Zero, 0.0f, hull);

                    if (i < Attack.Range * 0.02f)
                    {
                        var underwaterExplosion = GameMain.ParticleManager.CreateParticle("underwaterexplosion", worldPosition + bubblePos,
                            Vector2.Zero, 0.0f, hull);
                        if (underwaterExplosion != null)
                        {
                            underwaterExplosion.Size *= MathHelper.Clamp(Attack.Range / 300.0f, 0.5f, 2.0f) * Rand.Range(0.8f, 1.2f);
                        }
                    }
                    
                }

                if (sparks)
                {
                    GameMain.ParticleManager.CreateParticle("spark", worldPosition,
                        Rand.Vector(Rand.Range(1200.0f, 2400.0f)), 0.0f, hull);
                }
            }

            if (flash)
            {
                float displayRange = flashRange ?? Attack.Range;
                if (displayRange < 0.1f) { return; }
                var light = new LightSource(worldPosition, displayRange, flashColor, null);
                CoroutineManager.StartCoroutine(DimLight(light));
            }
        }

        private Vector2 ClampParticlePos(Vector2 particlePos, Hull hull)
        {
            if (hull == null) return particlePos;

            return new Vector2(
                MathHelper.Clamp(particlePos.X, hull.WorldRect.X, hull.WorldRect.Right),
                MathHelper.Clamp(particlePos.Y, hull.WorldRect.Y - hull.WorldRect.Height, hull.WorldRect.Y));
        }

        private IEnumerable<object> DimLight(LightSource light)
        {
            float currBrightness = 1.0f;
            while (light.Color.A > 0.0f && flashDuration > 0.0f)
            {
                light.Color = new Color(light.Color.R, light.Color.G, light.Color.B, (byte)(currBrightness * 255));
                currBrightness -= 1.0f / flashDuration * CoroutineManager.DeltaTime;

                yield return CoroutineStatus.Running;
            }

            light.Remove();

            yield return CoroutineStatus.Success;
        }

        static partial void PlayTinnitusProjSpecific(float volume) => SoundPlayer.PlaySound("tinnitus", volume: volume);
    }
}
