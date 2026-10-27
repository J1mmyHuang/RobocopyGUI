using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace RobocopyGui.Core
{
    public static class HashVerifier
    {
        public static readonly string[] Algorithms = { "SHA-256", "SHA-512", "SHA-1", "MD5" };

        public static async Task<List<HashRecord>> VerifyAllAsync(IReadOnlyList<FilePlan> plan, string algorithm, int blockSize, WorkflowProgress progress, Action<HashRecord> onFailure, CancellationToken token)
        {
            long targetBytes = 0; foreach (var item in plan) { token.ThrowIfCancellationRequested(); if (File.Exists(item.Target)) targetBytes += new FileInfo(item.Target).Length; } progress.BeginHashing(plan.Sum(p => p.Size) + targetBytes);
            var records = new List<HashRecord>();
            await Task.Run(delegate
            {
                foreach (var item in plan)
                {
                    token.ThrowIfCancellationRequested(); var record = new HashRecord { RelativePath = item.RelativePath, Result = "通过", Detail = "哈希一致" };
                    try
                    {
                        record.SourceHash = HashFile(item.Source, algorithm, blockSize, progress, token); if (!File.Exists(item.Target)) { record.Result = "缺失"; record.Detail = "目标文件不存在"; } else { record.TargetHash = HashFile(item.Target, algorithm, blockSize, progress, token); if (record.SourceHash != record.TargetHash) { record.Result = "失败"; record.Detail = "哈希不一致"; } }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { record.Result = "错误"; record.Detail = ex.Message; }
                    records.Add(record); if (record.Result == "通过") progress.CountPassed(); else { progress.CountFailed(); onFailure(record); }
                }
            }, token);
            return records;
        }

        public static string HashFile(string path, string algorithm, int blockSize, WorkflowProgress progress, CancellationToken token)
        {
            HashAlgorithm hash; if (algorithm == "MD5") hash = MD5.Create(); else if (algorithm == "SHA-1") hash = SHA1.Create(); else if (algorithm == "SHA-512") hash = SHA512.Create(); else hash = SHA256.Create();
            using (hash) using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, blockSize, FileOptions.SequentialScan)) { byte[] buffer = new byte[blockSize]; int read; while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) { token.ThrowIfCancellationRequested(); hash.TransformBlock(buffer, 0, read, buffer, 0); progress.AddHashed(read); } hash.TransformFinalBlock(new byte[0], 0, 0); return BitConverter.ToString(hash.Hash).Replace("-", ""); }
        }
    }
}
