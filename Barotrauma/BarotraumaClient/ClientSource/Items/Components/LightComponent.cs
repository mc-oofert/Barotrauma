﻿using Barotrauma.Extensions;
using Barotrauma.Lights;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;

namespace Barotrauma.Items.Components
{
    partial class LightComponent : Powered, IServerSerializable, IDrawableComponent
    {
        private bool? lastReceivedState;

        private CoroutineHandle resetPredictionCoroutine;
        private float resetPredictionTimer;

        public Vector2 DrawSize
        {
            get { return new Vector2(Light.Range * 2, Light.Range * 2); }
        }

        public LightSource Light { get; }

        public override void OnScaleChanged()
        {
            Light.SpriteScale = Vector2.One * item.Scale;
            Light.Position = ParentBody != null ? ParentBody.Position : item.Position;
        }

        partial void SetLightSourceState(bool enabled, float brightness)
        {
            if (Light == null) { return; }
            Light.Enabled = enabled;
            if (enabled)
            {
                Light.Color = LightColor.Multiply(brightness);
            }
        }

        public void Draw(SpriteBatch spriteBatch, bool editing = false, float itemDepth = -1)
        {
            if (Light.LightSprite != null && (item.body == null || item.body.Enabled) && lightBrightness > 0.0f && IsOn && Light.Enabled)
            {
                Vector2 origin = Light.LightSprite.Origin;
                if ((Light.LightSpriteEffect & SpriteEffects.FlipHorizontally) == SpriteEffects.FlipHorizontally) { origin.X = Light.LightSprite.SourceRect.Width - origin.X; }
                if ((Light.LightSpriteEffect & SpriteEffects.FlipVertically) == SpriteEffects.FlipVertically) { origin.Y = Light.LightSprite.SourceRect.Height - origin.Y; }
                Light.LightSprite.Draw(spriteBatch, new Vector2(item.DrawPosition.X, -item.DrawPosition.Y), lightColor * lightBrightness, origin, -Light.Rotation, item.Scale, Light.LightSpriteEffect, itemDepth - 0.0001f);
            }
        }

        public override void FlipX(bool relativeToSub)
        {
            if (Light?.LightSprite != null && item.Prefab.CanSpriteFlipX && item.body == null)
            {
                Light.LightSpriteEffect = Light.LightSpriteEffect == SpriteEffects.None ?
                    SpriteEffects.FlipHorizontally : SpriteEffects.None;                
            }
        }

        partial void OnStateChanged()
        {
            if (GameMain.Client == null || !lastReceivedState.HasValue) { return; }
            //reset to last known server state after the state hasn't changed in 1.0 seconds client-side
            resetPredictionTimer = 1.0f;
            if (resetPredictionCoroutine == null || !CoroutineManager.IsCoroutineRunning(resetPredictionCoroutine))
            {
                resetPredictionCoroutine = CoroutineManager.StartCoroutine(ResetPredictionAfterDelay());
            }
        }

        /// <summary>
        /// Reset client-side prediction of the light's state to the last known state sent by the server after resetPredictionTimer runs out
        /// </summary>
        private IEnumerable<object> ResetPredictionAfterDelay()
        {
            while (resetPredictionTimer > 0.0f)
            {
                resetPredictionTimer -= CoroutineManager.DeltaTime;
                yield return CoroutineStatus.Running;
            }
            if (lastReceivedState.HasValue) { IsActive = lastReceivedState.Value; }
            resetPredictionCoroutine = null;
            yield return CoroutineStatus.Success;
        }

        public void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
        {
            IsActive = msg.ReadBoolean();
            lastReceivedState = IsActive;
        }

        protected override void RemoveComponentSpecific()
        {
            base.RemoveComponentSpecific();
            Light.Remove();
        }
    }
}
