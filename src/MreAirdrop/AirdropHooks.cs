using System;
using System.Collections.Generic;
using System.Linq;
using SDG.Unturned;
using UnityEngine;
using Logger = Rocket.Core.Logging.Logger;

namespace MreAirdrop
{
    internal static class AirdropHooks
    {
        private sealed class SpawnScope { internal MreAirdropPlugin Plugin; internal HashSet<Carepackage> Before; internal ushort[] Loot; }
        private sealed class CollisionScope { internal CollisionScope Previous; internal Carepackage Package; internal SpawnAsset Cargo; internal ushort[] Loot; internal int Index; }
        [ThreadStatic] private static CollisionScope current;

        private static void BeforeSpawn(Vector3 position, SpawnAsset cargoSpawnTable, out object __state)
        {
            __state = null;
            var p = MreAirdropPlugin.Instance;
            if (p == null) return;
            var candidates = p.Planes.Where(x => x.Key.ServerHasDeployedCarepackage &&
                x.Value.Cargo == cargoSpawnTable && (position - x.Key.ServerDropPosition).sqrMagnitude <= 0.01f).ToArray();
            if (candidates.Length == 0) return;
            if (candidates.Length != 1)
            {
                // Не назначать случайный комплект при неоднозначности, например
                // после исключения внутри стороннего патча.
                foreach (var candidate in candidates) p.Planes.Remove(candidate.Key);
                Logger.LogWarning("[MRE] Несколько самолётов соответствуют сбросу; чужие грузы не изменены.");
                return;
            }
            var chosen = candidates[0];
            // Забираем сопоставление ДО вложенных вызовов: один самолёт не может
            // назначить тот же комплект двум экземплярам груза.
            p.Planes.Remove(chosen.Key);
            __state = new SpawnScope { Plugin = p, Loot = chosen.Value.Loot, Before = new HashSet<Carepackage>(UnityEngine.Object.FindObjectsOfType<Carepackage>()) };
        }

        private static void AfterSpawn(object __state)
        {
            var scope = __state as SpawnScope;
            if (scope == null) return;
            var added = UnityEngine.Object.FindObjectsOfType<Carepackage>().Where(x => !scope.Before.Contains(x)).ToArray();
            if (added.Length == 1) scope.Plugin.Packages.Add(added[0], scope.Loot);
            else Logger.LogWarning("[MRE] Новый груз не определён однозначно; чужие грузы не изменены.");
        }

        private static void BeforeCollision(Carepackage __instance, Collision collision, bool ___isExploded, out object __state)
        {
            // Даже чужой вложенный callback временно скрывает контекст внешнего
            // груза, чтобы не получить его предметы через ту же таблицу.
            var scope = new CollisionScope { Previous = current };
            __state = scope; current = null;
            var p = MreAirdropPlugin.Instance; ushort[] loot;
            if (p == null || !Provider.isServer || ___isExploded || collision.collider.isTrigger || !Level.isLoaded ||
                DamageTool.getVehicle(collision.transform) != null || !p.Packages.TryGetValue(__instance, out loot)) return;
            scope.Package = __instance; scope.Cargo = __instance.cargoSpawnTable; scope.Loot = loot;
            current = scope;
        }

        private static Exception AfterCollision(Exception __exception, object __state)
        {
            var scope = __state as CollisionScope;
            if (scope == null) return __exception;
            current = scope.Previous;
            var p = MreAirdropPlugin.Instance;
            if (p != null && scope.Package != null) p.Packages.Remove(scope.Package);
            if (__exception != null) Logger.LogException(__exception, "[MRE] Ошибка приземления: повторная выдача не выполняется.");
            return __exception;
        }

        private static bool BeforeResolve(SpawnAsset __0, EAssetType __1, ref ushort __result)
        {
            var scope = current;
            if (scope == null || __1 != EAssetType.ITEM || __0 != scope.Cargo) return true;
            __result = scope.Index < scope.Loot.Length ? scope.Loot[scope.Index++] : (ushort)0;
            return false;
        }
    }
}
