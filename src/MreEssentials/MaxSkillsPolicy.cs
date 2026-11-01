using System;
using System.Collections.Generic;
using System.Linq;

namespace MreEssentials
{
    public sealed class SkillLevelState
    {
        public int Speciality { get; private set; }
        public int Index { get; private set; }
        public int Level { get; private set; }
        public int Maximum { get; private set; }

        public SkillLevelState(int speciality, int index, int level, int maximum)
        {
            Speciality = speciality;
            Index = index;
            Level = level;
            Maximum = maximum;
        }
    }

    public interface IMaxSkillsTarget
    {
        IEnumerable<SkillLevelState> ReadLevels();
        void UnlockAllAndReplicate();
    }

    public sealed class MaxSkillsResult
    {
        public bool Applied { get; private set; }
        public IList<SkillLevelState> Levels { get; private set; }
        public bool AllAtMaximum
        {
            get { return Levels.Count > 0 && Levels.All(s => s.Level == s.Maximum); }
        }

        public MaxSkillsResult(bool applied, IEnumerable<SkillLevelState> levels)
        {
            Applied = applied;
            Levels = new List<SkillLevelState>(levels).AsReadOnly();
        }
    }

    // Политика не выдумывает XP/максимумы: игровой адаптер использует штатную репликацию U3DS.
    public static class MaxSkillsPolicy
    {
        public static MaxSkillsResult Apply(IMaxSkillsTarget target)
        {
            if (target == null) throw new ArgumentNullException("target");
            var before = new List<SkillLevelState>(target.ReadLevels());
            bool apply = before.Count > 0 && before.Any(s => s.Level != s.Maximum);
            if (apply) target.UnlockAllAndReplicate();
            return new MaxSkillsResult(apply, target.ReadLevels());
        }
    }
}
