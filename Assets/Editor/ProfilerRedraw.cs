#if UNITY_EDITOR
using System.Reflection;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class ProfilerRedraw
{
    private static ProfilerWindow profilerWindow;
    private static MethodInfo repaintImmediatelyMethod;

    static ProfilerRedraw()
    {
        repaintImmediatelyMethod = typeof(EditorWindow).GetMethod(
            "RepaintImmediately",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        EditorApplication.update += ForceUpdateEveryFrame;
    }

    private static void ForceUpdateEveryFrame()
    {
        if (!EditorApplication.isPlaying)
            return;

        if (profilerWindow == null)
        {
            var windows = Resources.FindObjectsOfTypeAll<ProfilerWindow>();
            if (windows.Length > 0)
                profilerWindow = windows[0];
        }

        if (profilerWindow != null)
        {
            profilerWindow.SelectAndStayOnLatestFrame();
            if (repaintImmediatelyMethod != null)
                repaintImmediatelyMethod.Invoke(profilerWindow, null);
            else
                profilerWindow.Repaint();
        }
    }
}
#endif