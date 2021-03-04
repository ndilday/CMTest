﻿using OnlyWar.Scripts.Helpers.Battle;
using OnlyWar.Scripts.Helpers.Battle.Actions;
using OnlyWar.Scripts.Helpers.Battle.Resolutions;
using OnlyWar.Scripts.Models;
using OnlyWar.Scripts.Models.Equippables;
using OnlyWar.Scripts.Models.Planets;
using OnlyWar.Scripts.Models.Soldiers;
using OnlyWar.Scripts.Models.Squads;
using OnlyWar.Scripts.Views;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

namespace OnlyWar.Scripts.Controllers
{
    public class BattleController : MonoBehaviour
    {
        public UnityEvent OnBattleComplete;

        [SerializeField]
        private GameSettings GameSettings;
        [SerializeField]
        private BattleView BattleView;
        
        private readonly Dictionary<int, BattleSquad> _playerBattleSquads;
        private readonly Dictionary<int, BattleSquad> _opposingBattleSquads;
        private readonly Dictionary<int, BattleSquad> _soldierBattleSquadMap;
        private readonly List<BattleSoldier> _startingPlayerBattleSoldiers;
        private readonly Dictionary<int, BattleSoldier> _casualtyMap;

        private BattleSquad _selectedBattleSquad;
        private BattleSquad _hoveredBattleSquad;
        private BattleGrid _grid;
        private int _turnNumber;
        private int _startingEnemyCount;
        private readonly MoveResolver _moveResolver;
        private readonly WoundResolver _woundResolver;
        private Planet _planet;
        private Faction _opposingFaction;
        private BaseSkill _baseMeleeSkill;

        private const int MAP_WIDTH = 100;
        private const int MAP_HEIGHT = 450;
        private const bool VERBOSE = false;

        public BattleController()
        {
            _playerBattleSquads = new Dictionary<int, BattleSquad>();
            _opposingBattleSquads = new Dictionary<int, BattleSquad>();;
            _soldierBattleSquadMap = new Dictionary<int, BattleSquad>();
            _moveResolver = new MoveResolver();
            _moveResolver.OnRetreat.AddListener(MoveResolver_OnRetreat);
            _woundResolver = new WoundResolver(VERBOSE);
            _woundResolver.OnSoldierDeath.AddListener(WoundResolver_OnSoldierDeath);
            _woundResolver.OnSoldierFall.AddListener(WoundResolver_OnSoldierFall);
            _casualtyMap = new Dictionary<int, BattleSoldier>();
            _startingPlayerBattleSoldiers = new List<BattleSoldier>();
        }

        public void Start()
        {
            _baseMeleeSkill =
                GameSettings.Galaxy.BaseSkillMap.Values.First(bs => bs.Name == "Fist");
        }

        public void GalaxyController_OnBattleStarted(Planet planet)
        {
            _planet = planet;
            ResetBattleValues();

            foreach (KeyValuePair<int, List<Squad>> kvp in planet.FactionSquadListMap)
            {
                if (kvp.Key == GameSettings.Galaxy.PlayerFaction.Id)
                {
                    PopulateMapsFromSquadList(_playerBattleSquads, kvp.Value, true);
                }
                else
                {
                    _opposingFaction = GameSettings.Galaxy.Factions.First(f => f.Id == kvp.Key);
                    PopulateMapsFromSquadList(_opposingBattleSquads, kvp.Value, false);
                }
            }

            PopulateBattleView();
            BattleView.UpdateNextStepButton("Next Turn", true);
        }

        public void NextStepButton_OnClick()
        {
            if (_playerBattleSquads.Count() > 0 && _opposingBattleSquads.Count() > 0)
            {
                ProcessNextTurn();
            }
            else
            {
                ProcessEndOfBattle();
            }
        }

        public void BattleView_OnSoldierPointerEnter(int soldierId)
        {
            BattleSquad battleSquad = _soldierBattleSquadMap[soldierId];
            if (_hoveredBattleSquad != null 
                && _hoveredBattleSquad.Id != battleSquad.Id
                && battleSquad.Id != _selectedBattleSquad?.Id)
            {
                BattleView.HighlightSoldiers(_hoveredBattleSquad.Soldiers
                                                                .Select(s => s.Soldier.Id), 
                                             false, Color.clear);
            }

            if (battleSquad.Id != _selectedBattleSquad?.Id)
            {
                BattleView.HighlightSoldiers(battleSquad.Soldiers.Select(s => s.Soldier.Id),
                                             true, Color.cyan);
                _hoveredBattleSquad = battleSquad;
            }
            BattleSoldier soldier = battleSquad.Soldiers.First(s => s.Soldier.Id == soldierId);
            Tooltip.ShowTooltip(GetSoldierDetails(soldier));
        }

        public void BattleView_OnSoldierPointerExit()
        {
            if(_hoveredBattleSquad != null && _hoveredBattleSquad.Id != _selectedBattleSquad?.Id)
            {
                BattleView.HighlightSoldiers(_hoveredBattleSquad.Soldiers
                                                                .Select(s => s.Soldier.Id), 
                                             false, Color.clear);
                Tooltip.HideTooltip();
            }
        }

        public void BattleView_OnSoldierPointerClick(int soldierId)
        {
            IEnumerable<int> soldierIds;
            if(_selectedBattleSquad != null)
            {
                soldierIds = _selectedBattleSquad.Soldiers.Select(s => s.Soldier.Id);
                BattleView.HighlightSoldiers(soldierIds, false, Color.clear);
            }
            _selectedBattleSquad = _soldierBattleSquadMap[soldierId];
            soldierIds = _selectedBattleSquad.Soldiers.Select(s => s.Soldier.Id);
            BattleView.HighlightSoldiers(soldierIds, true, Color.yellow);
            if(_selectedBattleSquad.IsPlayerSquad)
            {
                BattleView.OverwritePlayerWoundTrack(GetSquadDetails(_selectedBattleSquad));
            }
            else
            {
                BattleView.OverwritePlayerWoundTrack(GetSquadSummary(_selectedBattleSquad));
            }
        }

        private void WoundResolver_OnSoldierDeath(BattleSoldier casualty, BattleSoldier inflicter, WeaponTemplate weapon)
        {
            _casualtyMap[casualty.Soldier.Id] = casualty;
            if(casualty.BattleSquad.IsPlayerSquad)
            {
                // add death note to soldier history, though we currently just delete it 
                // we'll probably want it later
                GameSettings.Chapter.PlayerSoldierMap[casualty.Soldier.Id]
                    .AddEntryToHistory($"Killed in battle with the {_opposingFaction.Name} by a {weapon.Name}");
            }
            else
            {
                // give the inflicter credit for downing this enemy
                // WARNING: this will lead to multi-counting in some cases
                // I may later try to divide credit, but having multiple soldiers 
                // claim credit feels pseudo-realistic for now
                CreditSoldierForKill(inflicter, weapon);
            }
        }

        private void WoundResolver_OnSoldierFall(BattleSoldier fallenSoldier, BattleSoldier inflicter, WeaponTemplate weapon)
        {
            _casualtyMap[fallenSoldier.Soldier.Id] = fallenSoldier;
            if(!fallenSoldier.BattleSquad.IsPlayerSquad)
            {
                // give the inflicter credit for downing this enemy
                // WARNING: this will lead to multi-counting in some cases
                // I may later try to divide credit, but having multiple soldiers 
                // claim credit feels pseudo-realistic for now
                CreditSoldierForKill(inflicter, weapon);
            }
        }

        private void MoveResolver_OnRetreat(BattleSoldier soldier)
        {
            Log(false, "<b>" + soldier.Soldier.Name + " has retreated from the battlefield</b>");
            _casualtyMap[soldier.Soldier.Id] = soldier;
        }

        private void ProcessNextTurn()
        {
            _turnNumber++;
            BattleView.UpdateNextStepButton("Next Turn", false);
            BattleView.ClearBattleLog();
            _grid.ClearReservations();
            _casualtyMap.Clear();
            Log(false, "Turn " + _turnNumber.ToString());
            // this is a three step process: plan, execute, and apply

            ConcurrentBag<IAction> moveSegmentActions = new ConcurrentBag<IAction>();
            ConcurrentBag<IAction> shootSegmentActions = new ConcurrentBag<IAction>();
            ConcurrentBag<IAction> meleeSegmentActions = new ConcurrentBag<IAction>();
            ConcurrentQueue<string> log = new ConcurrentQueue<string>();
            Plan(shootSegmentActions, moveSegmentActions, meleeSegmentActions, log);
            while (!log.IsEmpty)
            {
                log.TryDequeue(out string line);
                Log(false, line);
            }

            HandleShootingAndMoving(shootSegmentActions, moveSegmentActions);
            while (!log.IsEmpty)
            {
                log.TryDequeue(out string line);
                Log(false, line);
            }

            HandleMelee(meleeSegmentActions);
            while (!log.IsEmpty)
            {
                log.TryDequeue(out string line);
                Log(false, line);
            }

            ProcessWounds();
            CleanupAtEndOfTurn();

            if (_selectedBattleSquad?.IsPlayerSquad == true)
            {
                BattleView.OverwritePlayerWoundTrack(GetSquadDetails(_selectedBattleSquad));
            }
            else if(_selectedBattleSquad != null)
            {
                BattleView.OverwritePlayerWoundTrack(GetSquadSummary(_selectedBattleSquad));
            }

            if (_playerBattleSquads.Count() == 0 || _opposingBattleSquads.Count() == 0)
            {
                Log(false, "One side destroyed, battle over");
                BattleView.UpdateNextStepButton("End Battle", true);
            }
            else
            {
                BattleView.UpdateNextStepButton("Next Turn", true);
            }
        }

        private void ProcessEndOfBattle()
        {
            if(_opposingBattleSquads == null || _opposingBattleSquads.Count == 0)
            {
                // The marines finish off any xenos still moving
                _planet.FactionSquadListMap.Remove(_opposingFaction.Id);
            }
            else
            {
                // kill all marines?
            }
            // we'll be nice to the Marines despite losing the battle... for now
            Debug.Log("Battle completed");
            ProcessSoldierHistoryForBattle();
            ApplySoldierExperienceForBattle();
            List<PlayerSoldier> dead = RemoveSoldiersKilledInBattle();
            LogBattleToChapterHistory(dead);
            BattleView.gameObject.SetActive(false);
            OnBattleComplete.Invoke();
        }

        private void PopulateBattleView()
        {
            BattleSquadPlacer placer = new BattleSquadPlacer(_grid);
            placer.PlaceSquads(_playerBattleSquads.Values);
            PopulateBattleViewSquads(_playerBattleSquads.Values);
            placer.PlaceSquads(_opposingBattleSquads.Values);
            PopulateBattleViewSquads(_opposingBattleSquads.Values);
        }

        private void Plan(ConcurrentBag<IAction> shootSegmentActions, 
                          ConcurrentBag<IAction> moveSegmentActions, 
                          ConcurrentBag<IAction> meleeSegmentActions,
                          ConcurrentQueue<string> log)
        {
            // PLAN
            // use the thread pool to handle the BattleSquadPlanner classes;
            // these look at the current game state to figure out the actions each soldier should take
            // the planners populate the actionBag with what they want to do
            MeleeWeapon defaultWeapon = new MeleeWeapon(
                GameSettings.Galaxy.MeleeWeaponTemplates.Values
                    .First(mwt => mwt.Name == "Fist"));
            //Parallel.ForEach(_playerSquads.Values, (squad) =>
            foreach(BattleSquad squad in _playerBattleSquads.Values)
            {
                BattleSquadPlanner planner = new BattleSquadPlanner(_grid, _soldierBattleSquadMap, 
                                                                    shootSegmentActions, moveSegmentActions, 
                                                                    meleeSegmentActions, 
                                                                    _woundResolver.WoundQueue, 
                                                                    _moveResolver.MoveQueue, 
                                                                    log, defaultWeapon);
                planner.PrepareActions(squad);
            };
            //Parallel.ForEach(_opposingSquads.Values, (squad) =>
            foreach(BattleSquad squad in _opposingBattleSquads.Values)
            {
                BattleSquadPlanner planner = new BattleSquadPlanner(_grid, _soldierBattleSquadMap, 
                                                                    shootSegmentActions, moveSegmentActions, 
                                                                    meleeSegmentActions, 
                                                                    _woundResolver.WoundQueue, 
                                                                    _moveResolver.MoveQueue, 
                                                                    log, defaultWeapon);
                planner.PrepareActions(squad);
            };
        }

        private void HandleShootingAndMoving(ConcurrentBag<IAction> shootActions,
                                    ConcurrentBag<IAction> moveActions)
        {
            // EXECUTE
            // once the squads have all finished planning actions, we process the execution logic. 
            // These use the command pattern to allow the controller to execute each without 
            // having any knowledge of what the internal implementation is
            // this also allows us to separate the concerns of the planner and the executor
            // we take the results/side effects of each execution that impact the outside world 
            // and put those results into queues
            // (movement and wounding are the only things that fit this category, today, 
            // but there will be others in the future)
            //Parallel.ForEach(actionBag, (action) => action.Execute());
            foreach(IAction action in shootActions)
            {
                action.Execute();
            }
            foreach (IAction action in moveActions)
            {
                action.Execute();
            }
            _moveResolver.Resolve();
        }

        private void HandleMelee(ConcurrentBag<IAction> meleeActions)
        {
            foreach (IAction action in meleeActions)
            {
                action.Execute();
            }
        }

        private void ProcessWounds()
        {
            _woundResolver.Resolve();
            Log(false, _woundResolver.ResolutionLog);
        }

        private void CleanupAtEndOfTurn()
        {
            // handle casualties
            foreach (BattleSoldier soldier in _casualtyMap.Values)
            {
                RemoveSoldier(soldier, _soldierBattleSquadMap[soldier.Soldier.Id]);
            }

            // redraw map
            RedrawSquadPositions();
            
            // update who's in melee
            foreach(BattleSquad squad in _playerBattleSquads.Values)
            {
                UpdateSquadMeleeStatus(squad);
            }
            foreach (BattleSquad squad in _opposingBattleSquads.Values)
            {
                UpdateSquadMeleeStatus(squad);
            }
        }

        private void UpdateSquadMeleeStatus(BattleSquad squad)
        {
            bool atLeastOneSoldierInMelee = false;
            foreach (BattleSoldier soldier in squad.Soldiers)
            {
                soldier.IsInMelee = _grid.IsAdjacentToEnemy(soldier.Soldier.Id);
                if (soldier.IsInMelee) atLeastOneSoldierInMelee = true;
            }
            squad.IsInMelee = atLeastOneSoldierInMelee;
        }

        private void RedrawSquadPositions()
        {
            foreach(BattleSquad squad in _playerBattleSquads.Values)
            {
                foreach(BattleSoldier soldier in squad.Soldiers)
                {
                    Tuple<int, int> position = _grid.GetSoldierPosition(soldier.Soldier.Id);
                    BattleView.MoveSoldier(soldier.Soldier.Id,
                                           new Vector2(position.Item1, position.Item2));
                }
            }
            foreach(BattleSquad squad in _opposingBattleSquads.Values)
            {
                foreach (BattleSoldier soldier in squad.Soldiers)
                {
                    Tuple<int, int> position = _grid.GetSoldierPosition(soldier.Soldier.Id);
                    BattleView.MoveSoldier(soldier.Soldier.Id,
                                           new Vector2(position.Item1, position.Item2));
                }
            }
        }

        private void ResetBattleValues()
        {
            BattleView.gameObject.SetActive(true);
            BattleView.Clear();
            BattleView.SetMapSize(new Vector2(MAP_WIDTH, MAP_HEIGHT));
            _turnNumber = 0;

            _playerBattleSquads.Clear();
            _soldierBattleSquadMap.Clear();
            _opposingBattleSquads.Clear();
            _startingPlayerBattleSoldiers.Clear();
            _startingEnemyCount = 0;

            _grid = new BattleGrid(MAP_WIDTH, MAP_HEIGHT);
        }

        private void PopulateMapsFromSquadList(Dictionary<int, BattleSquad> map, List<Squad> squads, bool isPlayerSquad)
        {
            var activeSquads = squads.Where(s => !s.IsInReserve);
            foreach (Squad squad in activeSquads)
            {
                BattleSquad bs = new BattleSquad(isPlayerSquad, squad);

                map[bs.Id] = bs;
                foreach(BattleSoldier soldier in bs.Soldiers)
                {
                    _soldierBattleSquadMap[soldier.Soldier.Id] = bs;
                }
                if(isPlayerSquad)
                {
                    // making a separate list 
                    _startingPlayerBattleSoldiers.AddRange(bs.Soldiers);
                }
                else
                {
                    _startingEnemyCount += bs.Soldiers.Count;
                }
            }
        }

        private void PopulateBattleViewSquads(IEnumerable<BattleSquad> squads)
        {
            foreach (BattleSquad squad in squads)
            {
                Color color = squad.IsPlayerSquad ? Color.blue : Color.black;
                foreach (BattleSoldier soldier in squad.Soldiers)
                {
                    Tuple<int, int> position = _grid.GetSoldierPosition(soldier.Soldier.Id);
                    BattleView.AddSoldier(soldier.Soldier.Id, 
                                          new Vector2(position.Item1, position.Item2), color);
                }
            }
        }

        private string GetSquadDetails(BattleSquad squad)
        {
            string report = "\n" + squad.Name + "\n" + squad.Soldiers.Count.ToString() + " soldiers standing\n\n";
            foreach(BattleSoldier soldier in squad.Soldiers)
            {
                report += GetSoldierDetails(soldier);
            }
            return report;
        }

        private static string GetSoldierDetails(BattleSoldier soldier)
        {
            string report = soldier.Soldier.Name + "\n";
            foreach (RangedWeapon weapon in soldier.RangedWeapons)
            {
                report += weapon.Template.Name + "\n";
            }
            report += soldier.Armor.Template.Name + "\n";
            foreach (HitLocation hl in soldier.Soldier.Body.HitLocations)
            {
                if (hl.Wounds.WoundTotal != 0)
                {
                    report += hl.ToString() + "\n";
                }
            }
            report += "\n";
            return report;
        }

        private string GetSquadSummary(BattleSquad squad)
        {
            return "\n" + squad.Name + "\n" + squad.Soldiers.Count.ToString() + " soldiers standing\n\n";
        }

        private void Log(bool isMessageVerbose, string text)
        {
            if (VERBOSE || !isMessageVerbose)
            {
                BattleView.LogToBattleLog(text);
            }
        }

        private void RemoveSoldier(BattleSoldier soldier, BattleSquad squad)
        {
            squad.RemoveSoldier(soldier);
            _grid.RemoveSoldier(soldier.Soldier.Id);
            BattleView.RemoveSoldier(soldier.Soldier.Id);
            _soldierBattleSquadMap.Remove(soldier.Soldier.Id);
            if(squad.Soldiers.Count == 0)
            {
                RemoveSquad(squad);
            }
        }

        private void RemoveSquad(BattleSquad squad)
        {
            Log(false, "<b>" + squad.Name + " wiped out</b>");
            
            if(squad.IsPlayerSquad)
            {
                _playerBattleSquads.Remove(squad.Id);
            }
            else
            {
                _opposingBattleSquads.Remove(squad.Id);
            }

            if(_selectedBattleSquad == squad)
            {
                _selectedBattleSquad = null;
            }
        }

        private void ProcessSoldierHistoryForBattle()
        {
            foreach (BattleSoldier soldier in _startingPlayerBattleSoldiers)
            {
                string historyEntry = GameSettings.Date.ToString() 
                    + ": Fought in a skirmish on " + _planet.Name + ".";
                if(soldier.EnemiesTakenDown > 0)
                {
                    historyEntry += $" Felled {soldier.EnemiesTakenDown} {_opposingFaction.Name}.";
                }
                if(soldier.WoundsTaken > 0)
                {
                    bool badWound = false;
                    bool sever = false;
                    foreach(HitLocation hl in soldier.Soldier.Body.HitLocations)
                    {
                        if(hl.Template.IsVital && hl.IsCrippled)
                        {
                            badWound = true;
                        }
                        if(hl.IsSevered)
                        {
                            sever = true;
                            historyEntry += $" Lost his {hl.Template.Name} in the fighting.";
                        }
                    }
                    if(badWound && !sever)
                    {
                        historyEntry += $"Was greviously wounded.";
                    }
                }
                GameSettings.Chapter.PlayerSoldierMap[soldier.Soldier.Id].AddEntryToHistory(historyEntry);
            }

        }

        private void ApplySoldierExperienceForBattle()
        {
            // each wound is .005 CON, for now
            // each turn shooting is .0005 for both DEX and the gun skill
            // each turn aiming is .0005 for the gun skill
            // each turn swinging is .0005 for ST and the melee skill
            foreach(BattleSquad squad in _playerBattleSquads.Values)
            {
                foreach(BattleSoldier soldier in squad.Soldiers)
                {
                    if (soldier.RangedWeapons.Count > 0)
                    {
                        if (soldier.TurnsAiming > 0)
                        {
                            soldier.Soldier.AddSkillPoints(soldier.RangedWeapons[0].Template.RelatedSkill, 
                                                           soldier.TurnsAiming * 0.0005f);
                        }
                        if (soldier.TurnsShooting > 0)
                        {
                            soldier.Soldier.AddSkillPoints(soldier.RangedWeapons[0].Template.RelatedSkill,
                                                           soldier.TurnsShooting * 0.0005f);
                            soldier.Soldier.AddAttributePoints(Models.Soldiers.Attribute.Dexterity,
                                                               soldier.TurnsShooting * 0.0005f);
                        }
                    }
                    if (soldier.WoundsTaken > 0)
                    {
                        soldier.Soldier.AddAttributePoints(Models.Soldiers.Attribute.Constitution,
                                                               soldier.WoundsTaken * 0.0005f);
                    }
                    if (soldier.TurnsSwinging > 0)
                    {
                        if (soldier.MeleeWeapons.Count > 0)
                        {
                            soldier.Soldier.AddSkillPoints(soldier.MeleeWeapons[0].Template.RelatedSkill,
                                                               soldier.TurnsSwinging * 0.0005f);
                        }
                        else
                        {
                            soldier.Soldier.AddSkillPoints(_baseMeleeSkill,
                                                           soldier.TurnsSwinging * 0.0005f);
                        }
                        soldier.Soldier.AddAttributePoints(Models.Soldiers.Attribute.Strength,
                                                               soldier.TurnsSwinging * 0.0005f);
                    }
                }
            }
        }

        private List<PlayerSoldier> RemoveSoldiersKilledInBattle()
        {
            List<PlayerSoldier> dead = new List<PlayerSoldier>();
            foreach(BattleSoldier soldier in _startingPlayerBattleSoldiers)
            {
                foreach(HitLocation hl in soldier.Soldier.Body.HitLocations)
                {
                    if(hl.Template.IsVital && hl.IsSevered)
                    {
                        // if a vital part is severed, they're dead
                        PlayerSoldier playerSoldier = 
                            GameSettings.Chapter.PlayerSoldierMap[soldier.Soldier.Id];
                        dead.Add(playerSoldier);
                        Squad squad = playerSoldier.AssignedSquad;
                        playerSoldier.AssignedSquad = null;
                        squad.RemoveSquadMember(playerSoldier);
                        GameSettings.Chapter.PlayerSoldierMap.Remove(soldier.Soldier.Id);
                        break;
                    }
                }
            }
            return dead;
        }

        private void LogBattleToChapterHistory(List<PlayerSoldier> killedInBattle)
        {
            var subEvents = GetBattleLog(killedInBattle);
            GameSettings.Chapter.AddToBattleHistory(GameSettings.Date,
                                                    $"A skirmish on {_planet.Name}",
                                                    subEvents);
        }

        private List<string> GetBattleLog(List<PlayerSoldier> killedInBattle)
        {
            List<string> battleEvents = new List<string>();
            int marineCount = _startingPlayerBattleSoldiers.Count;
            battleEvents.Add(marineCount.ToString() + " stood against " + _startingEnemyCount.ToString() + " enemies");
            foreach(PlayerSoldier soldier in killedInBattle)
            {
                string geneseedStatus = GetGeneseedStatusDescription(soldier);
                battleEvents.Add(
                    $"{soldier.Template.Name} {soldier.Name} died in the service of the emperor. Geneseed: {geneseedStatus}.");
            }
            return battleEvents;
        }

        private string GetGeneseedStatusDescription(PlayerSoldier soldier)
        {
            if(soldier.Body.HitLocations.First(hl => hl.Template.Name == "Face").IsSevered)
            {
                return "Destroyed";
            }
            else if(soldier.Body.HitLocations.First(hl => hl.Template.Name == "Torso").IsSevered)
            {
                return "Destroyed";
            }
            else if(GameSettings.Date.GetWeeksDifference(soldier.ProgenoidImplantDate) < 260)
            {
                return "Immature, Unrecoverable";
            }
            else
            {
                GameSettings.Chapter.GeneseedStockpile++;
                return "Recovered";
            }
        }
    
        private void CreditSoldierForKill(BattleSoldier inflicter, WeaponTemplate weapon)
        {
            GameSettings.Chapter.PlayerSoldierMap[inflicter.Soldier.Id]
                .AddKill(_opposingFaction.Id, weapon.Id);
            inflicter.EnemiesTakenDown++;
        }
    }
}