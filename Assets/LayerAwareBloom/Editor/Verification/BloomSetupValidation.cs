using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace LayerAwareBloom.Editor
{
    /// <summary>One entry point for the package's editor and rendering checks.</summary>
    public static class BloomSetupValidation
    {
        private const string MenuPath = "Tools/Layer Aware Bloom/Validate Setup";
        private const string SummaryPath = "Temp/BloomSetupValidation.txt";
        private static bool running;

        [MenuItem(MenuPath)]
        public static void Run()
        {
            if (!CanRun()) return;
            running = true;
            var checks = new (string name, Action run, string report)[]
            {
                ("Double bloom", BloomConflictVerification.Run, "Temp/BloomConflictVerification.txt"),
                ("Scene Volumes", BloomVolumeVerification.Run, "Temp/BloomVolumeVerification.txt"),
                ("Transparent sources", BloomTransparencyVerification.Run, "Temp/BloomTransparencyVerification.txt"),
                ("UI sources", BloomUIVerification.Run, "Temp/BloomUIVerification.txt"),
                ("Optional additions", BloomAdditionsVerification.Run, "Temp/BloomAdditionsVerification.txt")
            };
            var summary = new StringBuilder("[Layer Aware Bloom] Type: Validate setup\n");
            int failures = 0;
            try
            {
                Directory.CreateDirectory("Temp");
                for (int i = 0; i < checks.Length; i++)
                {
                    var check = checks[i];
                    EditorUtility.DisplayProgressBar("Validate Setup", check.name, (float)i / checks.Length);
                    try
                    {
                        // A previous successful report must never hide an early failure.
                        if (File.Exists(check.report)) File.Delete(check.report);
                        check.run();
                        string report = File.Exists(check.report) ? File.ReadAllText(check.report) : "";
                        summary.AppendLine($"\n[{check.name}]\n{report}");
                        string lastLine = report.Split('\n').LastOrDefault(line => !string.IsNullOrWhiteSpace(line))?.Trim();
                        if (lastLine == "PASS" || (lastLine?.StartsWith("PASS:", StringComparison.Ordinal) ?? false))
                            continue;

                        string problem = report.Split('\n').FirstOrDefault(line => line.StartsWith("FAIL", StringComparison.Ordinal))
                            ?? "Check did not complete. See its report and earlier Console errors.";
                        failures++;
                        Debug.LogError($"[Layer Aware Bloom] Type: {check.name}, error: {problem}\nReport: {check.report}");
                    }
                    catch (Exception error)
                    {
                        failures++;
                        summary.AppendLine($"[{check.name}] FAIL: {error}");
                        Debug.LogError($"[Layer Aware Bloom] Type: {check.name}, error: {error.Message}\n{error.StackTrace}");
                    }
                }
                string result = failures == 0
                    ? $"Validate Setup passed: all {checks.Length} validation groups passed."
                    : $"Validate Setup failed: {failures} of {checks.Length} validation groups failed. See the specific errors above.";
                summary.AppendLine(result);
                File.WriteAllText(SummaryPath, summary.ToString());
                if (failures == 0) Debug.Log($"[Layer Aware Bloom] Type: Validate setup, info: {result}\nReport: {SummaryPath}");
                else Debug.LogError($"[Layer Aware Bloom] Type: Validate setup, error: {result}\nReport: {SummaryPath}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                running = false;
            }
        }

        [MenuItem(MenuPath, true)]
        private static bool CanRun() => !running && !EditorApplication.isPlayingOrWillChangePlaymode
            && !EditorApplication.isCompiling && !EditorApplication.isUpdating;
    }
}
