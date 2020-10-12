﻿using OnlyWar.Scripts.Helpers;
using OnlyWar.Scripts.Models;
using OnlyWar.Scripts.Models.Fleets;
using OnlyWar.Scripts.Models.Soldiers;
using OnlyWar.Scripts.Models.Squads;
using OnlyWar.Scripts.Models.Units;
using OnlyWar.Scripts.Helpers.Database.GameState;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OnlyWar.Scripts.Controllers
{
    public class MainMenuController : MonoBehaviour
    {
        private const int GENERATE_GALAXY_SEED = 0;

        [SerializeField]
        private GameSettings GameSettings;
        // Start is called before the first frame update

        public void NewGameButton_OnClick()
        {
            GenerateNewGame();
            SceneManager.LoadScene("GalaxyView");
        }

        public void LoadGameButton_OnClick()
        {
            // TODO: open file screen
            // load that file
            GameSettings.Galaxy = new Galaxy(GameSettings.GalaxySize);
            var shipTemplateMap = GameSettings.Galaxy.Factions.SelectMany(f => f.ShipTemplates.Values)
                                                              .ToDictionary(s => s.Id);
            var unitTemplateMap = GameSettings.Galaxy.Factions.SelectMany(f => f.UnitTemplates.Values)
                                                              .ToDictionary(u => u.Id);
            var squadTemplateMap = GameSettings.Galaxy.Factions.SelectMany(f => f.SquadTemplates.Values)
                                                              .ToDictionary(s => s.Id);
            var hitLocations = GameSettings.Galaxy.BodyHitLocationTemplateMap.Values.SelectMany(hl => hl)
                                                                                    .Distinct()
                                                                                    .ToDictionary(hl => hl.Id);
            var soldierTypeMap = GameSettings.Galaxy.Factions.SelectMany(f => f.SoldierTypes.Values)
                                                             .ToDictionary(st => st.Id);
            var gameData = 
                GameStateDataAccess.Instance.GetData("default.s3db", 
                                                     GameSettings.Galaxy.Factions.ToDictionary(f => f.Id), 
                                                     shipTemplateMap, unitTemplateMap, squadTemplateMap,
                                                     hitLocations, GameSettings.Galaxy.BaseSkillMap,
                                                     soldierTypeMap);

            GameSettings.Galaxy.GenerateGalaxy(gameData.Planets, gameData.Fleets);
            var factionUnits = gameData.Units.GroupBy(u => u.UnitTemplate.Faction)
                                             .ToDictionary(g => g.Key, g => g.ToList());
            foreach(Faction faction in GameSettings.Galaxy.Factions)
            {
                if(factionUnits.ContainsKey(faction))
                {
                    List<Unit> units = factionUnits[faction];
                    faction.Units.AddRange(units);
                    foreach(Unit unit in units)
                    {
                        foreach(Squad squad in unit.GetAllSquads())
                        {
                            if(squad.Location != null)
                            {
                                if(!squad.Location.FactionSquadListMap.ContainsKey(faction.Id))
                                {
                                    squad.Location.FactionSquadListMap[faction.Id] = 
                                        new List<Squad>();
                                }
                                squad.Location.FactionSquadListMap[faction.Id].Add(squad);
                            }
                        }
                    }
                }
            }
            var chapterUnit = factionUnits[GameSettings.Galaxy.PlayerFaction].First(u => u.ParentUnit == null);
            var soldiers = chapterUnit.GetAllMembers().Select(s => (PlayerSoldier)s);
            GameSettings.Chapter = new Chapter(chapterUnit, soldiers);
            GameSettings.Chapter.PopulateSquadMap();
            SceneManager.LoadScene("GalaxyView");
        }

        public void QuitGameButton_OnClick()
        {
            Application.Quit();
        }

        private void GenerateNewGame()
        {
            GameSettings.Galaxy = new Galaxy(GameSettings.GalaxySize);
            GameSettings.Galaxy.GenerateGalaxy(GENERATE_GALAXY_SEED);
            // generate chapter
            CreateChapter();
            PlaceStartingForces();
        }

        private void CreateChapter()
        {
            Date basicTrainingEndDate = new Date(GameSettings.Date.Millenium, GameSettings.Date.Year - 3, 52);
            Date trainingStartDate = new Date(GameSettings.Date.Millenium, GameSettings.Date.Year - 4, 1);
            var soldierTemplate = GameSettings.Galaxy.PlayerFaction.SoldierTemplates[0];
            var soldiers = 
                SoldierFactory.Instance.GenerateNewSoldiers(1000, soldierTemplate)
                .Select(s => new PlayerSoldier(s, $"{TempNameGenerator.GetName()} {TempNameGenerator.GetName()}"))
                .ToList();

            string foo = "";
            SoldierTrainingHelper trainingHelper =
                new SoldierTrainingHelper(GameSettings.Galaxy.BaseSkillMap.Values);
            foreach (PlayerSoldier soldier in soldiers)
            {
                soldier.AddEntryToHistory(trainingStartDate + ": accepted into training");
                if (soldier.PsychicPower > 0)
                {
                    soldier.AddEntryToHistory(trainingStartDate + ": psychic ability detected, acolyte training initiated");
                    // add psychic specific training here
                }
                trainingHelper.EvaluateSoldier(soldier, basicTrainingEndDate);
                soldier.ProgenoidImplantDate = new Date(GameSettings.Date.Millenium, GameSettings.Date.Year - 2, RNG.GetIntBelowMax(1, 53));

                foo += $"{(int)soldier.MeleeRating}, {(int)soldier.RangedRating}, {(int)soldier.LeadershipRating}, {(int)soldier.AncientRating}, {(int)soldier.MedicalRating}, {(int)soldier.TechRating}, {(int)soldier.PietyRating}\n";
            }


            System.IO.File.WriteAllText($"{Application.streamingAssetsPath}/ratings.csv", foo);

            GameSettings.Chapter =
                NewChapterBuilder.CreateChapter(soldiers,
                                                GameSettings.Galaxy.PlayerFaction,
                                                new Date(GameSettings.Date.Millenium,
                                                    (GameSettings.Date.Year), 1).ToString());
            // post-MOS evaluations
            foreach(PlayerSoldier soldier in soldiers)
            {
                trainingHelper.EvaluateSoldier(soldier, GameSettings.Date);
            }
            GameSettings.Galaxy.PlayerFaction.Units.Add(GameSettings.Chapter.OrderOfBattle);

            // TODO: replace this with a random assignment of starting planet
            // and then have the galaxy map screen default to zooming in
            // on the Marine starting planet
            var emptyPlanets = GameSettings.Galaxy.Planets.Where(p => p.ControllingFaction == null);
            int max = emptyPlanets.Count();
            int chapterPlanetIndex = RNG.GetIntBelowMax(0, max);
            Planet chapterPlanet = emptyPlanets.ElementAt(chapterPlanetIndex);
            chapterPlanet.ControllingFaction = GameSettings.Galaxy.PlayerFaction;
        }

        private void PlaceStartingForces()
        {
            // For now, put the chapter on their home planet
            foreach (Planet planet in GameSettings.Galaxy.Planets)
            {
                if (planet.ControllingFaction == GameSettings.Galaxy.PlayerFaction)
                {
                    planet.FactionSquadListMap = new Dictionary<int, List<Squad>>
                    {
                        [GameSettings.Galaxy.PlayerFaction.Id] =
                            GameSettings.Chapter.SquadMap.Values.ToList()
                    };
                    SetChapterSquadsLocation(planet);
                    foreach(Fleet fleet in GameSettings.Chapter.Fleets)
                    {
                        fleet.Planet = planet;
                        fleet.Position = planet.Position;
                        GameSettings.Galaxy.AddNewFleet(fleet);
                    }
                }
                else if (planet.ControllingFaction != null)
                {
                    int potentialArmies = planet.ControllingFaction
                                                .UnitTemplates
                                                .Values
                                                .Where(ut => ut.IsTopLevelUnit)
                                                .Count();
                    Unit newArmy = TempTyranidArmyGenerator.GenerateTyranidArmy(
                        RNG.GetIntBelowMax(0, potentialArmies),
                        planet.ControllingFaction);
                    planet.ControllingFaction.Units.Add(newArmy);
                    planet.FactionSquadListMap = new Dictionary<int, List<Squad>>
                    {
                        // TODO: generalize this
                        [planet.ControllingFaction.Id] = newArmy.GetAllSquads().ToList()
                    };
                }
            }
        }

        private void SetChapterSquadsLocation(Planet planet)
        {
            if (GameSettings.Chapter.OrderOfBattle.HQSquad != null)
            {
                GameSettings.Chapter.OrderOfBattle.HQSquad.Location = planet;
            }
            foreach (Squad squad in GameSettings.Chapter.OrderOfBattle.Squads)
            {
                squad.Location = planet;
            }
            foreach (Unit unit in GameSettings.Chapter.OrderOfBattle.ChildUnits)
            {
                if (unit.HQSquad != null)
                {
                    unit.HQSquad.Location = planet;
                }
                foreach (Squad squad in unit.Squads)
                {
                    squad.Location = planet;
                }
            }
        }
    }
}