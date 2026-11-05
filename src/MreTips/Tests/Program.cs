using System;
using MreTips;
class Program
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Main()
    {
        var s = new TipSchedule(300, new[] { "один", "два" }, 0);
        Check(s.Poll(299, true) == null, "ранняя отправка");
        Check(s.Poll(300, true) == "один", "первая подсказка");
        Check(s.Poll(1900, true) == "два", "пропущенный интервал");
        Check(s.Poll(1900, true) == null && s.Poll(2199, true) == null, "накопленные сообщения");
        Check(s.Poll(2200, false) == null && s.Poll(2500, true) == "один", "пустой сервер и порядок");
        Check(new TipSchedule(60, new[] { " ", "текст" }, 0).Poll(60, true) == "текст", "пустые строки");
        Check(new TipSchedule(60, new string[0], 0).Poll(60, true) == null, "пустой список");
        bool invalid = false; try { new TipSchedule(59, new[] { "x" }, 0); } catch (ArgumentOutOfRangeException) { invalid = true; }
        Check(invalid, "неверный интервал");
        invalid = false; try { new TipSchedule(60, new[] { "a\nb" }, 0); } catch (ArgumentException) { invalid = true; }
        Check(invalid, "многострочная подсказка");
        Console.WriteLine("Проверки расписания MreTips пройдены.");
    }
}
