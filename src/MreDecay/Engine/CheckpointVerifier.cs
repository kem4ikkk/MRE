using System;
using System.Security.Cryptography;

namespace MreDecay.Engine
{
    // Comparison primitive only. The caller must obtain the anchor independently
    // of the restored world/state files and prove both snapshots are complete.
    public sealed class CheckpointAnchor
    {
        public long Generation;
        public byte[] WorldSha256, StateSha256;
    }
    public static class CheckpointVerifier
    {
        public static bool Matches(CheckpointAnchor trustedAnchor, long generation, byte[] worldSnapshot,
            byte[] stateSnapshot, bool hasUnresolvedTransaction)
        {
            if (trustedAnchor == null || trustedAnchor.Generation < 0 || generation != trustedAnchor.Generation ||
                hasUnresolvedTransaction || worldSnapshot == null || worldSnapshot.Length == 0 ||
                stateSnapshot == null || stateSnapshot.Length == 0 || trustedAnchor.WorldSha256 == null ||
                trustedAnchor.WorldSha256.Length != 32 || trustedAnchor.StateSha256 == null || trustedAnchor.StateSha256.Length != 32) return false;
            using (var sha = SHA256.Create()) {
                return Equal(sha.ComputeHash(worldSnapshot), trustedAnchor.WorldSha256) &&
                    Equal(sha.ComputeHash(stateSnapshot), trustedAnchor.StateSha256);
            }
        }
        private static bool Equal(byte[] a, byte[] b) {
            int difference = 0;
            for (int i=0; i<32; i++) difference |= a[i] ^ b[i];
            return difference == 0;
        }
    }
}
