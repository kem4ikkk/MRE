using System.Collections.Generic;
using Rocket.API;

namespace MreTips
{
    public sealed class MreTipsConfiguration : IRocketPluginConfiguration
    {
        public bool Enabled;
        public int IntervalSeconds;
        public List<string> Messages;

        public void LoadDefaults()
        {
            Enabled = true;
            IntervalSeconds = 300;
            Messages = new List<string>
            {
                "Подсказка: /kits покажет доступные наборы; /kit start выдаёт стартовый набор.",
                "Подсказка: /tpa имя отправляет запрос на телепортацию к игроку.",
                "Подсказка: /tpa a принимает запрос, /tpa d отклоняет его.",
                "Подсказка: /home запускает перемещение к своей кровати; /home cancel отменяет его.",
                "Подсказка: ударьте постройку пустой рукой, чтобы увидеть её состояние.",
                "Подсказка: ночью можно начать голосование за день командой /vote day; голос — /vote yes или /cv."
            };
        }
    }
}
