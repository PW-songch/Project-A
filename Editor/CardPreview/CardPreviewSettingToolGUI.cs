#if UNITY_EDITOR
using UnityEngine;

public class CardPreviewSettingToolGUI : MonoBehaviour
{
    public static System.Action ChangeViewCallback;

    private void OnGUI()
    {
        if (ChangeViewCallback != null)
        {
            GUILayout.BeginHorizontal();
            if (GUI.Button(new Rect(Screen.width - 110, 10, 100, 30), "Change View"))
                ChangeViewCallback.Invoke();
            GUILayout.EndHorizontal();
        }
    }
}
#endif