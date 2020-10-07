﻿using System.Collections.Generic;
using UnityEngine;

using Iam.Scripts.Models.Factions;
using System.Linq;

namespace Iam.Scripts.Models.Fleets
{
    public class Fleet
    {
        private static int _nextFleetId = 0;
        public int Id { get; set; }
        public Faction Faction { get; }
        public Vector2 Position { get; set; }
        public Planet Destination { get; set; }
        public Planet Planet { get; set; }
        public List<Ship> Ships { get; }

        public Fleet(Faction faction, int templateId) : this(faction)
        {
            int i = Id * 1000;
            BoatTemplate boatTemplate = faction.BoatTemplates.First().Value;
            foreach(ShipTemplate shipTemplate in faction.FleetTemplates[templateId].Ships)
            {
                Ships.Add(new Ship(i, $"{shipTemplate.ClassName}-{i}", shipTemplate, boatTemplate));
                i++;
            }
        }

        public Fleet(Faction faction)
        {
            Id = _nextFleetId++;
            Faction = faction;
            Ships = new List<Ship>();
        }
    }
}