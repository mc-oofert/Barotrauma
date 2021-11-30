﻿using System;
using System.Collections.Generic;
using System.Xml.Linq;
using System.Linq;
using Barotrauma.Extensions;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;

namespace Barotrauma.Items.Components
{
    partial class GeneticMaterial : ItemComponent, IServerSerializable
    {
        private readonly string materialName;

        private Character targetCharacter;
        private AfflictionPrefab selectedEffect, selectedTaintedEffect;

        [Serialize("", true)]
        public string Effect
        {
            get;
            set;
        }

        [Serialize("geneticmaterialdebuff", true)]
        public string TaintedEffect
        {
            get;
            set;
        }

        private bool tainted;
        [Serialize(false, true)]
        public bool Tainted
        {
            get { return tainted; }
            private set
            {
                if (!value) { return; }
                tainted = true;
                item.AllowDeconstruct = false;
                if (!string.IsNullOrEmpty(TaintedEffect))
                {
                    selectedTaintedEffect = AfflictionPrefab.Prefabs.Where(a =>
                        a.Identifier.Equals(TaintedEffect, StringComparison.OrdinalIgnoreCase) ||
                        a.AfflictionType.Equals(TaintedEffect, StringComparison.OrdinalIgnoreCase)).GetRandom();
                }
            }
        }

        //only for saving the selected tainted effect
        [Serialize("", true)]
        public string SelectedTaintedEffect
        {
            get { return selectedTaintedEffect?.Identifier ?? string.Empty; }
            private set
            {
                if (string.IsNullOrEmpty(value)) { return; }
                selectedTaintedEffect = AfflictionPrefab.Prefabs.Find(a => a.Identifier == value);
            }
        }

        public GeneticMaterial(Item item, XElement element)
            : base(item, element)
        {
            string nameId = element.GetAttributeString("nameidentifier", "");
            if (!string.IsNullOrEmpty(nameId))
            {
                materialName = TextManager.Get(nameId);
            }
            if (!string.IsNullOrEmpty(Effect))
            {
                selectedEffect = AfflictionPrefab.Prefabs.Where(a =>
                    a.Identifier.Equals(Effect, StringComparison.OrdinalIgnoreCase) ||
                    a.AfflictionType.Equals(Effect, StringComparison.OrdinalIgnoreCase)).GetRandom();
            }
        }

        [Serialize(3.0f, false)]
        public float ConditionIncreaseOnCombineMin  { get; set; }

        [Serialize(8.0f, false)]
        public float ConditionIncreaseOnCombineMax { get; set; }

        public bool CanBeCombinedWith(GeneticMaterial otherGeneticMaterial)
        {
            return !tainted && otherGeneticMaterial != null && !otherGeneticMaterial.tainted && item.AllowDeconstruct && otherGeneticMaterial.item.AllowDeconstruct;
        }

        public override void Equip(Character character)
        {
            if (character == null) { return; }
            IsActive = true;

            if (targetCharacter != null) { return; }

            if (tainted)
            {
                if (selectedTaintedEffect != null)
                {
                    float selectedTaintedEffectStrength = item.ConditionPercentage / 100.0f * selectedTaintedEffect.MaxStrength;
                    character.CharacterHealth.ApplyAffliction(null, selectedTaintedEffect.Instantiate(selectedTaintedEffectStrength));
                    var existingAffliction = character.CharacterHealth.GetAllAfflictions().FirstOrDefault(a => a.Prefab == selectedTaintedEffect);
                    if (existingAffliction != null)
                    {
                        existingAffliction.Strength = selectedTaintedEffectStrength;
                    }
                    targetCharacter = character;
#if SERVER
                    item.CreateServerEvent(this);
#endif      
                }
            }
            if (selectedEffect != null)
            {
                ApplyStatusEffects(ActionType.OnWearing, 1.0f);
                float selectedEffectStrength = item.ConditionPercentage / 100.0f * selectedEffect.MaxStrength;
                character.CharacterHealth.ApplyAffliction(null, selectedEffect.Instantiate(selectedEffectStrength));
                var existingAffliction = character.CharacterHealth.GetAllAfflictions().FirstOrDefault(a => a.Prefab == selectedEffect);
                if (existingAffliction != null)
                {
                    existingAffliction.Strength = selectedEffectStrength;
                }
                targetCharacter = character;
#if SERVER
                item.CreateServerEvent(this);
#endif      
            }
            foreach (Item containedItem in item.ContainedItems)
            {
                containedItem.GetComponent<GeneticMaterial>()?.Equip(character);
            }
        }

        public override void Update(float deltaTime, Camera cam)
        {
            base.Update(deltaTime, cam);
            if (targetCharacter != null)
            {
                var rootContainer = item.GetRootContainer();
                if (!targetCharacter.HasEquippedItem(item) && 
                    (rootContainer == null || !targetCharacter.HasEquippedItem(rootContainer) || !targetCharacter.Inventory.IsInLimbSlot(rootContainer, InvSlotType.HealthInterface)))
                {
                    item.ApplyStatusEffects(ActionType.OnSevered, 1.0f, targetCharacter);
                    targetCharacter.CharacterHealth.ReduceAffliction(null, selectedEffect.Identifier, selectedEffect.MaxStrength);
                    if (tainted)
                    {
                        targetCharacter.CharacterHealth.ReduceAffliction(null, selectedTaintedEffect.Identifier, selectedTaintedEffect.MaxStrength);
                    }
                    targetCharacter = null;
                    IsActive = false;
                }
            }
        }

        public bool Combine(GeneticMaterial otherGeneticMaterial, Character user)
        {
            if (!CanBeCombinedWith(otherGeneticMaterial)) { return false; }

            float conditionIncrease = Rand.Range(ConditionIncreaseOnCombineMin, ConditionIncreaseOnCombineMax);
            conditionIncrease += user.GetStatValue(StatTypes.GeneticMaterialRefineBonus);
            if (item.Prefab == otherGeneticMaterial.item.Prefab)
            {
                item.Condition = Math.Max(item.Condition, otherGeneticMaterial.item.Condition) + conditionIncrease;
                float taintedProbability = GetTaintedProbabilityOnRefine(user);
                if (taintedProbability >= Rand.Range(0.0f, 1.0f))
                {
                    MakeTainted();
                }
                return true;
            }
            else
            {
                item.Condition = otherGeneticMaterial.Item.Condition =
                    (item.Condition + otherGeneticMaterial.Item.Condition) / 2.0f + conditionIncrease;
                item.OwnInventory?.TryPutItem(otherGeneticMaterial.Item, user: null);
                item.AllowDeconstruct = false;
                otherGeneticMaterial.Item.AllowDeconstruct = false;
                if (GetTaintedProbabilityOnCombine(user) >= Rand.Range(0.0f, 1.0f))
                {
                    MakeTainted();
                }
                return false;
            }
        }

        private float GetTaintedProbabilityOnRefine(Character user)
        {
            if (user == null) { return 1.0f; }
            float probability = MathHelper.Lerp(0.0f, 0.99f, item.Condition / 100.0f);
            probability *= MathHelper.Lerp(1.0f, 0.25f, DegreeOfSuccess(user));
            return MathHelper.Clamp(probability, 0.0f, 1.0f);
        }

        private float GetTaintedProbabilityOnCombine(Character user)
        {
            if (user == null) { return 1.0f; }
            float probability = 1.0f - user.GetStatValue(StatTypes.GeneticMaterialTaintedProbabilityReductionOnCombine);
            return MathHelper.Clamp(probability, 0.0f, 1.0f);
        }

        private void MakeTainted()
        {
            if (GameMain.NetworkMember?.IsClient ?? false) { return; }
            Tainted = true;
#if SERVER
            item.CreateServerEvent(this);
#endif            
        }

        public static string TryCreateName(ItemPrefab prefab, XElement element)
        {
            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().Equals(nameof(GeneticMaterial), StringComparison.OrdinalIgnoreCase))
                {
                    string nameId = subElement.GetAttributeString("nameidentifier", "");
                    if (!string.IsNullOrEmpty(nameId))
                    {
                        return prefab.Name.Replace("[type]", TextManager.Get(nameId, returnNull: true) ?? nameId);
                    }
                }
            }
            return prefab.Name;
        }
    }
}
