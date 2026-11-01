using System.Collections.Generic;
using SDG.Unturned;

namespace MreEssentials
{
    internal sealed class PlayerMaxSkillsTarget : IMaxSkillsTarget
    {
        private readonly PlayerSkills playerSkills;

        public PlayerMaxSkillsTarget(PlayerSkills skills)
        {
            playerSkills = skills;
        }

        public IEnumerable<SkillLevelState> ReadLevels()
        {
            Skill[][] levels = playerSkills == null ? null : playerSkills.skills;
            if (levels == null) yield break;
            for (int speciality = 0; speciality < levels.Length; speciality++)
            {
                if (levels[speciality] == null) continue;
                for (int index = 0; index < levels[speciality].Length; index++)
                {
                    Skill skill = levels[speciality][index];
                    if (skill != null)
                        yield return new SkillLevelState(speciality, index, skill.level, skill.max);
                }
            }
        }

        public void UnlockAllAndReplicate()
        {
            // Этот публичный метод выставляет native max и надежно рассылает новые уровни клиентам.
            playerSkills.ServerUnlockAllSkills();
        }
    }
}
