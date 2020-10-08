﻿using Mono.Data.Sqlite;
using OnlyWar.Scripts.Models;
using OnlyWar.Scripts.Models.Soldiers;
using OnlyWar.Scripts.Models.Squads;
using System.Collections.Generic;
using System.Data;
using UnityEngine;

namespace OnlyWar.Scripts.Helpers.Database
{
    class PlayerSoldierDataAccess
    {
        public Dictionary<int, PlayerSoldier> GetData(string fileName, Dictionary<int, Soldier> soldierMap)
        {
            string connection = $"URI=file:{Application.streamingAssetsPath}/Saves/{fileName}";
            IDbConnection dbCon = new SqliteConnection(connection);
            dbCon.Open();

            var factionCasualtyMap = GetFactionCasualtiesBySoldierId(dbCon);
            var weaponCasualtyMap = GetWeaponCasualtiesBySoldierId(dbCon);
            var historyMap = GetHistoryBySoldierId(dbCon);
            var playerSoldiers = GetPlayerSoldiers(dbCon, soldierMap, factionCasualtyMap, 
                                                   weaponCasualtyMap, historyMap);

            foreach(PlayerSoldier playerSoldier in playerSoldiers.Values)
            {
                // replace the soldier in the squad with the playerSoldier
                Squad squad = playerSoldier.AssignedSquad;
                playerSoldier.RemoveFromSquad();
                playerSoldier.AssignToSquad(squad);
            }

            dbCon.Close();
            return playerSoldiers;
        }

        private Dictionary<int, Dictionary<int, ushort>> GetFactionCasualtiesBySoldierId(IDbConnection connection)
        {
            Dictionary<int, Dictionary<int, ushort>> soldierFactionCasualtyMap = 
                new Dictionary<int, Dictionary<int, ushort>>();
            IDbCommand command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM PlayerSoldierFactionCasualtyCount";
            var reader = command.ExecuteReader();
            while (reader.Read())
            {
                int soldierId = reader.GetInt32(1);
                int factionId = reader.GetInt32(2);
                ushort count = (ushort)reader.GetInt32(3);

                if (!soldierFactionCasualtyMap.ContainsKey(soldierId))
                {
                    soldierFactionCasualtyMap[soldierId] = new Dictionary<int, ushort>();
                }
                soldierFactionCasualtyMap[soldierId][factionId] = count;

            }
            return soldierFactionCasualtyMap;
        }

        private Dictionary<int, Dictionary<int, ushort>> GetWeaponCasualtiesBySoldierId(IDbConnection connection)
        {
            Dictionary<int, Dictionary<int, ushort>> soldierWeaponCasualtyMap =
                new Dictionary<int, Dictionary<int, ushort>>();
            IDbCommand command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM PlayerSoldierWeaponCasualtyCount";
            var reader = command.ExecuteReader();
            while (reader.Read())
            {
                int soldierId = reader.GetInt32(1);
                int weaponTemplateId = reader.GetInt32(2);
                ushort count = (ushort)reader.GetInt32(3);

                if (!soldierWeaponCasualtyMap.ContainsKey(soldierId))
                {
                    soldierWeaponCasualtyMap[soldierId] = new Dictionary<int, ushort>();
                }
                soldierWeaponCasualtyMap[soldierId][weaponTemplateId] = count;

            }
            return soldierWeaponCasualtyMap;
        }
    
        private Dictionary<int, List<string>> GetHistoryBySoldierId(IDbConnection connection)
        {
            Dictionary<int, List<string>> soldierEntryListMap = new Dictionary<int, List<string>>();
            IDbCommand command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM PlayerSoldierHistory";
            var reader = command.ExecuteReader();
            while (reader.Read())
            {
                int soldierId = reader.GetInt32(1);
                string entry = reader[2].ToString();

                if (!soldierEntryListMap.ContainsKey(soldierId))
                {
                    soldierEntryListMap[soldierId] = new List<string>();
                }
                soldierEntryListMap[soldierId].Add(entry);

            }
            return soldierEntryListMap;
        }
    
        private Dictionary<int, PlayerSoldier> GetPlayerSoldiers(IDbConnection connection,
                                                                 Dictionary<int, Soldier> baseSoldierMap,
                                                                 Dictionary<int, Dictionary<int, ushort>> factionCasualtyMap,
                                                                 Dictionary<int, Dictionary<int, ushort>> weaponCasualtyMap,
                                                                 Dictionary<int, List<string>> historyMap)
        {
            Dictionary<int, PlayerSoldier> playerSoldierMap = new Dictionary<int, PlayerSoldier>();
            IDbCommand command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM PlayerSoldier";
            var reader = command.ExecuteReader();
            while (reader.Read())
            {
                int soldierId = reader.GetInt32(0);
                float melee = (float)reader[1];
                float ranged = (float)reader[2];
                float leadership = (float)reader[3];
                float medical = (float)reader[4];
                float tech = (float)reader[5];
                float piety = (float)reader[6];
                float ancient = (float)reader[7];
                int implantMillenium = reader.GetInt32(8);
                int implantYear = reader.GetInt32(9);
                int implantWeek = reader.GetInt32(10);

                Date implantDate = new Date(implantMillenium, implantYear, implantWeek);

                PlayerSoldier playerSoldier = new PlayerSoldier(baseSoldierMap[soldierId], melee, ranged,
                                                                leadership, medical, tech, piety, ancient,
                                                                implantDate, historyMap[soldierId],
                                                                weaponCasualtyMap[soldierId],
                                                                factionCasualtyMap[soldierId]);

                playerSoldierMap[soldierId] = playerSoldier;

            }
            return playerSoldierMap;
        }
    }
}
