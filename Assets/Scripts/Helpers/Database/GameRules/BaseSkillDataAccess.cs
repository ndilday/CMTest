﻿using OnlyWar.Scripts.Models.Soldiers;
using System.Collections.Generic;
using System.Data;

namespace OnlyWar.Scripts.Helpers.Database.GameRules
{
    public class BaseSkillDataAccess
    {
        public Dictionary<int, BaseSkill> GetBaseSkills(IDbConnection connection)
        {
            Dictionary<int, BaseSkill> baseSkillMap = new Dictionary<int, BaseSkill>();
            IDbCommand command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM BaseSkill";
            var reader = command.ExecuteReader();
            while (reader.Read())
            {
                int id = reader.GetInt32(0);
                string name = reader[1].ToString();
                SkillCategory category = (SkillCategory)reader.GetInt32(2);
                var attribute = (Attribute)reader.GetInt32(3);
                float difficulty = (float)reader[4];
                BaseSkill baseSkill = new BaseSkill(id, category, name, attribute, difficulty);

                baseSkillMap[id] = baseSkill;
            }
            return baseSkillMap;
        }
    }
}
