﻿using System.Collections.Generic;
using Iam.Scripts.Models.Units;

namespace Iam.Scripts.Models.Soldiers
{
    public abstract class Soldier
    {
        public int Id;
        public abstract string JobRole { get; }
        public Unit AssignedUnit;
        public List<Equippable> Equipment;
        public List<Weapon> Weapons;
        public Armor Armor;

        public float Melee;
        public float Ranged;
        public float Strength;
        public float Dexterity;
        public float Perception;
        public float Intelligence;
        public float Ego;
        public float Presence;
        public float Constitution;

        public float AttackSpeed;
        public float Size;
        // 1mph is approximately 0.5 yards/sec
        // military walk is about 3-4mph, so say 2 yd/s
        // military double-time is about 6mph, or 3 yd/s
        // military sprint with equipment is about 8-9 yd/s
        // in fiction, Impys and Marines move at the same speed, 6"
        // with 2s turns, a double time move of 6yd would match the 6" speed of double time
        public float MoveSpeed;

        public Dictionary<string, Skill> Skills;
        public List<string> SoldierHistory;
        public Body Body { get; private set; }

        public Soldier()
        {
            Equipment = new List<Equippable>();
            Weapons = new List<Weapon>();
            SoldierHistory = new List<string>();
            Skills = new Dictionary<string, Skill>();
        }

        public void InitializeBody(BodyTemplate bodyTemplate)
        {
            Body = new Body(bodyTemplate);
        }

        public abstract bool CanFireWeapon(Weapon weapon);
    }
}
