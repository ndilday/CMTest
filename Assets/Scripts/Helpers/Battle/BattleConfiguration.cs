﻿using OnlyWar.Scripts.Helpers.Battle.Placers;
using OnlyWar.Scripts.Models;
using OnlyWar.Scripts.Models.Planets;
using OnlyWar.Scripts.Models.Squads;
using OnlyWar.Scripts.Models.Units;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Scripts.Helpers.Battle
{
    public class BattleConfiguration
    {
        public IReadOnlyList<BattleSquad> PlayerSquads;
        public IReadOnlyList<BattleSquad> OpposingSquads;
        public Planet Planet;
        public BattleGrid Grid;
    }

    public static class BattleConfigurationBuilder
    {
        public static BattleConfiguration BuildBattleConfiguration(Planet planet)
        {
            bool containsPlayerSquad = false;
            bool containsNonDefaultNonPlayerSquad = false;
            bool containsActivePlayerSquad = false;
            int marineCount = 0;
            // determine if the player and an OpFor are both on planet
            foreach (KeyValuePair<int, List<Squad>> kvp in planet.FactionSquadListMap)
            {
                Faction faction = kvp.Value[0].ParentUnit.UnitTemplate.Faction;
                if (!faction.IsDefaultFaction && !faction.IsPlayerFaction && kvp.Value.Any(s => !s.IsInReserve))
                {
                    containsNonDefaultNonPlayerSquad = true;
                }
                else if (faction.IsPlayerFaction)
                {
                    containsPlayerSquad = true;
                    containsActivePlayerSquad =
                        kvp.Value.Any(squad => !squad.IsInReserve);
                    marineCount = kvp.Value.Sum(s => s.Members.Count);
                }
            }
            if (!containsPlayerSquad)
            {
                return null;
            }
            if (containsActivePlayerSquad && containsNonDefaultNonPlayerSquad)
            {
                return ConstructAnnihilationConfiguration(planet);
            }
            // we have player squads, but no NPC squads;
            // need to determine if we should generate a force
            foreach (PlanetFaction planetFaction in planet.PlanetFactionMap.Values)
            {
                if (!planetFaction.Faction.IsDefaultFaction &&
                    !planetFaction.Faction.IsPlayerFaction)
                {
                    if (containsActivePlayerSquad)
                    {
                        // player is on the hunt
                        if (planetFaction.IsPublic)
                        {
                            // oppFor is in public, generate a force to face the player
                            Unit newArmy = GenerateNewArmy(planetFaction, planet);

                            return ConstructAnnihilationConfiguration(planet);
                        }
                        else
                        {
                            // TODO: chace that the player comes across the hidden enemy?
                            // increases over time with an active presence
                            // inverse to size of active presence
                            // formula: 0.0002 * pop / #marines
                            // should it be total marines or active marines?
                            double chanceToAmbush = 0.0002 *
                                                    planetFaction.Population /
                                                    marineCount;
                            if (RNG.GetLinearDouble() <= chanceToAmbush)
                            {
                                // set up an ambush force
                                Unit newArmy = GenerateNewArmy(planetFaction, planet);
                                return ConstructOpposingAmbushConfiguration(planet);
                            }
                        }
                    }
                    else
                    {
                        // TODO: change this logic later
                        return null;
                        // contains only reserve player squads
                        if (planetFaction.IsPublic)
                        {
                            // TODO: chance that the enemy comes across the player?
                        }
                        else
                        {
                            // TODO: chance that the two sides meet?
                        }
                    }
                }
            }
            return null;
        }

        private static Unit GenerateNewArmy(PlanetFaction planetFaction, Planet planet)
        {
            int factionId = planetFaction.Faction.Id;
            // if we got here, the assaulting force doesn't have an army generated
            // generate an army (and decrement it from the population
            Unit newArmy = TempArmyBuilder.GenerateArmyFromPlanetFaction(planetFaction);

            if (!planet.FactionSquadListMap.ContainsKey(factionId))
            {
                planet.FactionSquadListMap[factionId] = new List<Squad>();
            }

            // add unit to faction
            planetFaction.Faction.Units.Add(newArmy);
            
            // add unit to planet
            foreach(Squad squad in newArmy.GetAllSquads())
            {
                squad.IsInReserve = false;
                squad.Location = planet;
                planet.FactionSquadListMap[factionId].Add(squad);
            }

            // modify planetFaction based on new unit
            int headcount = newArmy.GetAllMembers().Count();
            float ratio = ((float)planetFaction.PDFMembers) /
                (planetFaction.Population + planetFaction.PDFMembers);
            int pdfHeadcount = (int)(headcount * ratio);
            headcount -= pdfHeadcount;
            planetFaction.PDFMembers -= pdfHeadcount;
            if(planetFaction.PDFMembers < 0)
            {
                headcount -= planetFaction.PDFMembers;
                planetFaction.PDFMembers = 0;
            }
            planetFaction.Population -= headcount;
            if(planetFaction.Population < 0)
            {
                planetFaction.Population = 0;
                // TODO: remove this planetFaction from the planet?
            }
            return newArmy;
        }
    
        private static BattleConfiguration ConstructAnnihilationConfiguration(Planet planet)
        {
            List<Squad> playerSquads = new List<Squad>();
            List<Squad> opposingSquads = new List<Squad>();
            foreach(List<Squad> squadList in planet.FactionSquadListMap.Values)
            {
                if(squadList[0].ParentUnit.UnitTemplate.Faction.IsPlayerFaction)
                {
                    foreach(Squad squad in squadList)
                    {
                        if(!squad.IsInReserve)
                        {
                            playerSquads.Add(squad);
                        }
                    }
                }
                else if(!squadList[0].ParentUnit.UnitTemplate.Faction.IsDefaultFaction)
                {
                    foreach(Squad squad in squadList)
                    {
                        if(!squad.IsInReserve)
                        {
                            opposingSquads.Add(squad);
                        }
                    }
                }
            }

            BattleConfiguration config = new BattleConfiguration();
            config.PlayerSquads = CreateBattleSquadList(playerSquads, true);
            config.OpposingSquads = CreateBattleSquadList(opposingSquads, false);
            config.Planet = planet;
            config.Grid = new BattleGrid(100, 500);
            AnnihilationPlacer placer = new AnnihilationPlacer(config.Grid);
            placer.PlaceSquads(config.PlayerSquads, config.OpposingSquads);
            return config;
        }

        private static BattleConfiguration ConstructOpposingAmbushConfiguration(Planet planet)
        {
            List<Squad> playerSquads = new List<Squad>();
            List<Squad> opposingSquads = new List<Squad>();
            foreach (List<Squad> squadList in planet.FactionSquadListMap.Values)
            {
                if (squadList[0].ParentUnit.UnitTemplate.Faction.IsPlayerFaction)
                {
                    foreach (Squad squad in squadList)
                    {
                        if (!squad.IsInReserve)
                        {
                            playerSquads.Add(squad);
                        }
                    }
                }
                else if (!squadList[0].ParentUnit.UnitTemplate.Faction.IsDefaultFaction)
                {
                    foreach (Squad squad in squadList)
                    {
                        if (!squad.IsInReserve)
                        {
                            opposingSquads.Add(squad);
                        }
                    }
                }
            }

            BattleConfiguration config = new BattleConfiguration();
            config.PlayerSquads = CreateBattleSquadList(playerSquads, true);
            config.OpposingSquads = CreateBattleSquadList(opposingSquads, false);
            config.Planet = planet;
            config.Grid = new BattleGrid(200, 200);
            AmbushPlacer placer = new AmbushPlacer(config.Grid);
            placer.PlaceSquads(config.PlayerSquads, config.OpposingSquads);
            return config;
        }

        private static List<BattleSquad> CreateBattleSquadList(IReadOnlyList<Squad> squads,
                                                               bool isPlayerSquad)
        {
            List<BattleSquad> battleSquadList = new List<BattleSquad>();
            foreach (Squad squad in squads)
            {
                BattleSquad bs = new BattleSquad(isPlayerSquad, squad);

                battleSquadList.Add(bs);
            }
            return battleSquadList;
        }
    }
}
