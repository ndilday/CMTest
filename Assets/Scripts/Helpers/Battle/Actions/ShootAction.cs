﻿using System;
using System.Collections.Concurrent;

using OnlyWar.Scripts.Helpers.Battle.Resolutions;
using OnlyWar.Scripts.Models.Equippables;
using OnlyWar.Scripts.Models.Soldiers;
using UnityEngine;

namespace OnlyWar.Scripts.Helpers.Battle.Actions
{
    class ShootAction : IAction
    {
        private readonly BattleSoldier _soldier;
        private readonly RangedWeapon _weapon;
        private readonly BattleSoldier _target;
        private readonly float _range;
        private int _numberOfShots;
        private readonly bool _useBulk;
        private readonly ConcurrentBag<WoundResolution> _resultList;
        private readonly ConcurrentQueue<string> _log;

        public ShootAction(BattleSoldier shooter, RangedWeapon weapon, BattleSoldier target, float range, int numberOfShots, bool useBulk, ConcurrentBag<WoundResolution> resultList, ConcurrentQueue<string> log)
        {
            _soldier = shooter;
            _weapon = weapon;
            _target = target;
            _range = range;
            _numberOfShots = numberOfShots;
            _useBulk = useBulk;
            _resultList = resultList;
            _log = log;
        }

        public void Execute()
        {
            float modifier = CalculateToHitModifiers();
            float skill = _soldier.Soldier.GetTotalSkillValue(_weapon.Template.RelatedSkill); 
            float roll = 10.5f + (3.0f * (float)RNG.NextGaussianDouble());
            float total = skill + modifier - roll;
            _soldier.Aim = null;
            _log.Enqueue($"{_soldier.Soldier.Name} fires a {_weapon.Template.Name} at {_target.Soldier.Name}");
            if(total > 0)
            {
                _log.Enqueue("<color=red>" + _soldier.Soldier.Name + " hits " + _target.Soldier.Name + " " + Mathf.Min((int)(total/_weapon.Template.Recoil) + 1, _numberOfShots) + " times</color>");
                // there were hits, determine how many
                do
                {
                    HandleHit();
                    total -= _weapon.Template.Recoil;
                    _numberOfShots--;
                } while (total > 1 && _numberOfShots > 0);
            }
            _soldier.TurnsShooting++;
        }

        private float CalculateToHitModifiers()
        {
            float totalModifier = 0;
            if (_useBulk)
            {
                totalModifier -= _weapon.Template.Bulk;
            }
            if(_soldier.Aim?.Item1 == _target && _soldier.Aim?.Item2 == _weapon)
            {
                totalModifier += _soldier.Aim.Item3 + _weapon.Template.Accuracy + 1;
            }
            totalModifier += BattleModifiersUtil.CalculateRateOfFireModifier(_numberOfShots);
            totalModifier += BattleModifiersUtil.CalculateSizeModifier(_target.Soldier.Size);
            totalModifier += BattleModifiersUtil.CalculateRangeModifier(_range, _target.CurrentSpeed);

            return totalModifier;
        }
        
        private void HandleHit()
        {
            HitLocation hitLocation = DetermineHitLocation(_target);
            // make sure this body part hasn't already been shot off
            if(!hitLocation.IsSevered)
            {
                float damage = BattleModifiersUtil.CalculateDamageAtRange(_weapon, _range) * (3.5f + ((float)RNG.NextGaussianDouble() * 1.75f));
                float effectiveArmor = _target.Armor.Template.ArmorProvided * _weapon.Template.ArmorMultiplier;
                float penDamage = damage - effectiveArmor;
                if (penDamage > 0)
                {
                    float totalDamage = penDamage * _weapon.Template.WoundMultiplier;
                    _resultList.Add(new WoundResolution(_soldier, _weapon.Template, _target, totalDamage, hitLocation));
                }
            }
        }

        private HitLocation DetermineHitLocation(BattleSoldier soldier)
        {
            // we're using the "lottery ball" approach to randomness here, where each point of probability
            // for each available body party defines the size of the random linear distribution
            // TODO: factor in cover/body position
            // 
            int roll = RNG.GetIntBelowMax(0, soldier.Soldier.Body.TotalProbabilityMap[soldier.Stance]);
            foreach (HitLocation location in soldier.Soldier.Body.HitLocations)
            {
                int locationChance = location.Template.HitProbabilityMap[(int)soldier.Stance];
                if (roll < locationChance)
                {
                    return location;
                }
                else
                {
                    // this is basically an easy iterative way to figure out which body part on the "chart" the roll matches
                    roll -= locationChance;
                }
            }
            // this should never happen
            throw new InvalidOperationException("Could not determine a hit location");
        }
    }
}
