using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

public static class BuildScript
{
    public static void BuildWebGL()
    {
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = "BuildOutput/WebGL",
            target = BuildTarget.WebGL,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }
}
