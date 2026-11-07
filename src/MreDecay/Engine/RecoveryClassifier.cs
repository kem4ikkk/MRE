using System;
using System.Collections.Generic;

namespace MreDecay.Engine
{
    public enum RecoveryClassification
    {
        Unknown,
        AllOld,
        AllNew,
        Mixed
    }

    // Stable data identity for a target. Coordinates are included so a reused
    // instance ID at a different location cannot be mistaken for the same object.
    public sealed class RecoveryIdentity
    {
        public byte Kind; // 1 structure, 2 barricade
        public uint InstanceId;
        public Guid AssetGuid;
        public ulong Owner;
        public ulong Group;
        public double X;
        public double Y;
        public double Z;
    }

    public sealed class RecoveryTarget
    {
        public RecoveryIdentity Identity;
        public ushort MaxHealth;
        public ushort OldHealth;
        public ushort NewHealth;
    }

    // Version 1 is the only supported manifest schema. Hashes are SHA-256 of
    // the raw plugin-state bytes at the two transaction endpoints.
    public sealed class PendingRecoveryManifest
    {
        public const int CurrentSchemaVersion = 1;
        public int SchemaVersion = CurrentSchemaVersion;
        public Guid TransactionId;
        public string WorldKey;
        public string BuildKey;
        public long OldGeneration;
        public long NewGeneration;
        public byte[] OldStateSha256;
        public byte[] NewStateSha256;
        public List<RecoveryTarget> Targets = new List<RecoveryTarget>();
    }

    public sealed class RecoveryObservation
    {
        public RecoveryIdentity Identity;
        public ushort MaxHealth;
        public ushort Health;
    }

    public sealed class RecoverySnapshot
    {
        public bool IsComplete;
        public string WorldKey;
        public string BuildKey;
        public long Generation;
        public byte[] StateSha256;
        public List<RecoveryObservation> Observations = new List<RecoveryObservation>();
    }

    // Classifies caller-supplied evidence only. IsComplete, generation, hashes,
    // and world/build keys are assertions from the snapshot producer; this
    // class does not prove their provenance, read persistence, or mutate state.
    public static class RecoveryClassifier
    {
        private const int MaxTargets = 100000;

        public static RecoveryClassification Classify(PendingRecoveryManifest manifest, RecoverySnapshot snapshot)
        {
            if (!ValidManifest(manifest) || !ValidSnapshot(snapshot)) return RecoveryClassification.Unknown;
            if (!String.Equals(manifest.WorldKey, snapshot.WorldKey, StringComparison.Ordinal) ||
                !String.Equals(manifest.BuildKey, snapshot.BuildKey, StringComparison.Ordinal))
                return RecoveryClassification.Unknown;

            bool oldEndpoint = snapshot.Generation == manifest.OldGeneration &&
                EqualHash(snapshot.StateSha256, manifest.OldStateSha256);
            bool newEndpoint = snapshot.Generation == manifest.NewGeneration &&
                EqualHash(snapshot.StateSha256, manifest.NewStateSha256);
            if (!oldEndpoint && !newEndpoint) return RecoveryClassification.Unknown;

            var targets = new Dictionary<string, RecoveryTarget>(manifest.Targets.Count, StringComparer.Ordinal);
            foreach (RecoveryTarget target in manifest.Targets)
                targets.Add(Key(target.Identity), target);

            var observed = new HashSet<string>(StringComparer.Ordinal);
            bool sawOldOnly = false;
            bool sawNewOnly = false;
            foreach (RecoveryObservation observation in snapshot.Observations)
            {
                string key = Key(observation.Identity);
                RecoveryTarget target;
                if (!observed.Add(key) || !targets.TryGetValue(key, out target) ||
                    !SameIdentity(target.Identity, observation.Identity) ||
                    target.MaxHealth != observation.MaxHealth ||
                    observation.Health > observation.MaxHealth)
                    return RecoveryClassification.Unknown;

                bool isOld = observation.Health == target.OldHealth;
                bool isNew = observation.Health == target.NewHealth;
                if (!isOld && !isNew) return RecoveryClassification.Unknown;
                if (isOld && !isNew) sawOldOnly = true;
                if (isNew && !isOld) sawNewOnly = true;
            }

            if (observed.Count != targets.Count) return RecoveryClassification.Unknown;
            if (sawOldOnly && sawNewOnly) return RecoveryClassification.Mixed;
            if (sawOldOnly) return oldEndpoint ? RecoveryClassification.AllOld : RecoveryClassification.Mixed;
            if (sawNewOnly) return newEndpoint ? RecoveryClassification.AllNew : RecoveryClassification.Mixed;

            // No health value distinguishes the endpoints. Only the exact
            // generation plus state hash can classify this snapshot.
            if (oldEndpoint) return RecoveryClassification.AllOld;
            if (newEndpoint) return RecoveryClassification.AllNew;
            return RecoveryClassification.Unknown;
        }

        private static bool ValidManifest(PendingRecoveryManifest manifest)
        {
            if (manifest == null || manifest.SchemaVersion != PendingRecoveryManifest.CurrentSchemaVersion ||
                manifest.TransactionId == Guid.Empty || String.IsNullOrWhiteSpace(manifest.WorldKey) ||
                String.IsNullOrWhiteSpace(manifest.BuildKey) || manifest.OldGeneration < 0 ||
                manifest.OldGeneration == long.MaxValue || manifest.NewGeneration != manifest.OldGeneration + 1 ||
                !ValidHash(manifest.OldStateSha256) || !ValidHash(manifest.NewStateSha256) ||
                manifest.Targets == null || manifest.Targets.Count == 0 || manifest.Targets.Count > MaxTargets)
                return false;

            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (RecoveryTarget target in manifest.Targets)
            {
                if (target == null || !ValidIdentity(target.Identity) || target.MaxHealth == 0 ||
                    target.OldHealth > target.MaxHealth || target.NewHealth > target.MaxHealth ||
                    !keys.Add(Key(target.Identity))) return false;
            }
            return true;
        }

        private static bool ValidSnapshot(RecoverySnapshot snapshot)
        {
            if (snapshot == null || !snapshot.IsComplete || String.IsNullOrWhiteSpace(snapshot.WorldKey) ||
                String.IsNullOrWhiteSpace(snapshot.BuildKey) || snapshot.Generation < 0 ||
                !ValidHash(snapshot.StateSha256) || snapshot.Observations == null ||
                snapshot.Observations.Count == 0 || snapshot.Observations.Count > MaxTargets) return false;

            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (RecoveryObservation observation in snapshot.Observations)
            {
                if (observation == null || !ValidIdentity(observation.Identity) || observation.MaxHealth == 0 ||
                    observation.Health > observation.MaxHealth || !keys.Add(Key(observation.Identity))) return false;
            }
            return true;
        }

        private static bool ValidIdentity(RecoveryIdentity identity)
        {
            return identity != null && (identity.Kind == 1 || identity.Kind == 2) &&
                identity.InstanceId != 0 && identity.AssetGuid != Guid.Empty &&
                Finite(identity.X) && Finite(identity.Y) && Finite(identity.Z);
        }

        private static string Key(RecoveryIdentity identity)
        {
            return identity.Kind.ToString() + ":" + identity.InstanceId.ToString();
        }

        private static bool SameIdentity(RecoveryIdentity a, RecoveryIdentity b)
        {
            return a.Kind == b.Kind && a.InstanceId == b.InstanceId && a.AssetGuid == b.AssetGuid &&
                a.Owner == b.Owner && a.Group == b.Group && a.X == b.X && a.Y == b.Y && a.Z == b.Z;
        }

        private static bool ValidHash(byte[] hash) { return hash != null && hash.Length == 32; }

        private static bool EqualHash(byte[] a, byte[] b)
        {
            if (!ValidHash(a) || !ValidHash(b)) return false;
            int difference = 0;
            for (int i = 0; i < 32; i++) difference |= a[i] ^ b[i];
            return difference == 0;
        }

        private static bool Finite(double value)
        {
            return !Double.IsNaN(value) && !Double.IsInfinity(value);
        }
    }
}
