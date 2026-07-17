#if SERHAT_FORGE_IOS_GAME_SERVICES
#if UNITY_IOS
using UnityEditor.iOS.Xcode;
using UnityEditor.Callbacks;
using UnityEditor;
#endif

public static class SignInWithApplePostprocessor
{
#if UNITY_IOS
    [PostProcessBuild(1)]
    public static void OnPostProcessBuild(BuildTarget target, string path)
    {
        if (target != BuildTarget.iOS)
            return;

        var projectPath = PBXProject.GetPBXProjectPath(path);
        var project = new PBXProject();
        project.ReadFromString(System.IO.File.ReadAllText(projectPath));
        var manager = new ProjectCapabilityManager(projectPath, "Entitlements.entitlements", null, project.GetUnityMainTargetGuid());
        manager.AddPushNotifications(true);
        manager.AddGameCenter();
        manager.WriteToFile();
    }
#endif
}
#endif
