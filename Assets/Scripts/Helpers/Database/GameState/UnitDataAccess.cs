﻿using OnlyWar.Scripts.Models.Fleets;
using OnlyWar.Scripts.Models.Planets;
using OnlyWar.Scripts.Models.Squads;
using OnlyWar.Scripts.Models.Units;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace OnlyWar.Scripts.Helpers.Database.GameState
{
    public class UnitDataAccess
    {
        public Dictionary<int, List<Squad>> GetSquadsByUnitId(IDbConnection connection,
                                                               IReadOnlyDictionary<int, SquadTemplate> squadTemplateMap,
                                                               IReadOnlyDictionary<int, Ship> shipMap,
                                                               List<Planet> planetList)
        {
            Dictionary<int, List<Squad>> squadMap = new Dictionary<int, List<Squad>>();
            IDbCommand command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM Squad";
            var reader = command.ExecuteReader();
            while (reader.Read())
            {
                int id = reader.GetInt32(0);
                int squadTemplateId = reader.GetInt32(1);
                int parentUnitId = reader.GetInt32(2);
                string name = reader[3].ToString();
                bool isInReserve = reader.GetBoolean(6);

                SquadTemplate template = squadTemplateMap[squadTemplateId];

                Squad squad = new Squad(id, name, null, template, isInReserve);


                if (reader[4].GetType() != typeof(DBNull))
                {
                    Ship ship = shipMap[reader.GetInt32(4)];
                    squad.BoardedLocation = ship;
                }

                if (reader[5].GetType() != typeof(DBNull))
                {
                    Planet planet = planetList.First(p => p.Id == reader.GetInt32(5));
                    squad.Location = planet;
                }

                if (!squadMap.ContainsKey(parentUnitId))
                {
                    squadMap[parentUnitId] = new List<Squad>();
                }
                squadMap[parentUnitId].Add(squad);
            }
            return squadMap;
        }

        public List<Unit> GetUnits(IDbConnection connection,
                                   IReadOnlyDictionary<int, UnitTemplate> unitTemplateMap,
                                   IReadOnlyDictionary<int, List<Squad>> unitSquadMap)
        {
            List<Unit> unitList = new List<Unit>();
            Dictionary<int, Unit> unitMap = new Dictionary<int, Unit>();
            Dictionary<int, List<Unit>> parentUnitMap = new Dictionary<int, List<Unit>>();
            IDbCommand command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM Unit";
            var reader = command.ExecuteReader();
            while (reader.Read())
            {
                int id = reader.GetInt32(0);
                int unitTemplateId = reader.GetInt32(2);
                string name = reader[5].ToString();

                Squad hqSquad = null;
                int parentUnitId;

                List<Squad> squadList = null;
                if (unitSquadMap.ContainsKey(id))
                {
                    squadList = unitSquadMap[id];
                }

                if (reader[3].GetType() != typeof(DBNull))
                {
                    int hqSquadId = reader.GetInt32(3);
                    hqSquad = squadList.First(s => s.Id == hqSquadId);
                }

                Unit unit = new Unit(id, name, unitTemplateMap[unitTemplateId],
                                     hqSquad, squadList);
                if (hqSquad != null)
                {
                    hqSquad.ParentUnit = unit;
                }
                foreach (Squad squad in squadList)
                {
                    squad.ParentUnit = unit;
                }

                unitMap[id] = unit;
                unitList.Add(unit);

                if (reader[4].GetType() != typeof(DBNull))
                {
                    parentUnitId = reader.GetInt32(4);
                    if (!parentUnitMap.ContainsKey(parentUnitId))
                    {
                        parentUnitMap[parentUnitId] = new List<Unit>();
                    }
                    parentUnitMap[parentUnitId].Add(unit);
                }
            }

            foreach (KeyValuePair<int, List<Unit>> kvp in parentUnitMap)
            {
                unitMap[kvp.Key].ChildUnits = kvp.Value;
                foreach(Unit unit in kvp.Value)
                {
                    unit.ParentUnit = unitMap[kvp.Key];
                }
            }

            return unitList.Where(u => u.ParentUnit == null).ToList();
        }

        public void SaveUnit(IDbTransaction transaction, Unit unit)
        {
            string hq = unit.HQSquad == null ? "null" : unit.HQSquad.Id.ToString();
            string parent = unit.ParentUnit == null ? "null" : unit.ParentUnit.Id.ToString();
            string insert = $@"INSERT INTO Unit VALUES ({unit.Id}, {unit.UnitTemplate.Faction.Id}, 
                {unit.UnitTemplate.Id}, {hq}, {parent}, '{unit.Name}');";
            IDbCommand command = transaction.Connection.CreateCommand();
            command.CommandText = insert;
            command.ExecuteNonQuery();
        }

        public void SaveSquad(IDbTransaction transaction, Squad squad)
        {
            string safeName = squad.Name.Replace("\'", "\'\'");
            string ship = squad.BoardedLocation == null ? "null" : squad.BoardedLocation.Id.ToString();
            string planet = squad.Location == null ? "null" : squad.Location.Id.ToString();
            string insert = $@"INSERT INTO Squad VALUES ({squad.Id}, {squad.SquadTemplate.Id}, 
                {squad.ParentUnit.Id}, '{safeName}', {ship}, {planet}, {squad.IsInReserve});";
            IDbCommand command = transaction.Connection.CreateCommand();
            command.CommandText = insert;
            command.ExecuteNonQuery();
        }
    }
}
