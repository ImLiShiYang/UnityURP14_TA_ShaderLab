using UnityEngine;

public class CharacterYRotate : MonoBehaviour
{
    [Header("旋转设置")]
    [Tooltip("每秒绕 Y 轴旋转多少度")]
    public float rotateSpeed = 30f;

    [Tooltip("开始游戏时是否自动旋转")]
    public bool autoRotateOnStart = false;

    [Tooltip("是否使用世界坐标 Y 轴旋转。开启后永远绕世界 Y 轴转。")]
    public bool useWorldY = true;

    [Header("控制按钮")]
    [Tooltip("是否在 Game 视图左上角显示控制按钮")]
    public bool showButton = true;

    [Tooltip("按钮宽度")]
    public float buttonWidth = 120f;

    [Tooltip("按钮高度")]
    public float buttonHeight = 40f;

    private bool isRotating;

    private void Start()
    {
        isRotating = autoRotateOnStart;
    }

    private void Update()
    {
        if (!isRotating)
            return;

        float angle = rotateSpeed * Time.deltaTime;

        if (useWorldY)
        {
            transform.Rotate(Vector3.up, angle, Space.World);
        }
        else
        {
            transform.Rotate(Vector3.up, angle, Space.Self);
        }
    }

    public void ToggleRotate()
    {
        isRotating = !isRotating;
    }

    public void StartRotate()
    {
        isRotating = true;
    }

    public void PauseRotate()
    {
        isRotating = false;
    }

    private void OnGUI()
    {
        if (!showButton)
            return;

        string buttonText = isRotating ? "暂停旋转" : "开始旋转";

        if (GUI.Button(new Rect(20, 20, buttonWidth, buttonHeight), buttonText))
        {
            ToggleRotate();
        }
    }
}