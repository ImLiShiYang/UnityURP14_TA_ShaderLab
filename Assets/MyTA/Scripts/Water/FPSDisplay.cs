using UnityEngine;

namespace DouduckLib.DevelopTool {
    public class FPSDisplay : MonoBehaviour {
        public enum ScreenCorner {
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight
        }

        [Header ("Display")]
        public ScreenCorner corner = ScreenCorner.TopLeft;
        public Vector2 screenOffset = new Vector2 (16f, 16f);
        [Range (10, 32)]
        public int fontSize = 18;
        public Color textColor = new Color (1f, 1f, 1f, 0.92f);
        public Color shadowColor = new Color (0f, 0f, 0f, 0.75f);

        [Header ("Refresh")]
        [Range (0.1f, 2f)]
        public float refreshInterval = 0.5f;
        [Range (0.01f, 0.25f)]
        public float smoothing = 0.08f;

        private float m_deltaTime = 1f / 60f;
        private float m_timer;
        private string m_text = "FPS 60";
        private GUIContent m_content;
        private GUIStyle m_style;
        private Rect m_rect;

        private void Awake () {
            m_content = new GUIContent (m_text);
            BuildStyle ();
            UpdateLayout ();
        }

        private void Update () {
            float frameDelta = Mathf.Max (0.0001f, Time.unscaledDeltaTime);
            m_deltaTime += (frameDelta - m_deltaTime) * smoothing;
            m_timer += frameDelta;

            if (m_timer >= refreshInterval) {
                m_timer = 0f;

                float fps = 1f / Mathf.Max (0.0001f, m_deltaTime);
                float ms = m_deltaTime * 1000f;
                m_text = string.Format ("FPS {0:0}  |  {1:0.0} ms", fps, ms);
                m_content.text = m_text;
                UpdateLayout ();
            }
        }

        private void OnGUI () {
            if (m_style == null || m_style.fontSize != fontSize) {
                BuildStyle ();
                UpdateLayout ();
            }

            if (Event.current.type != EventType.Repaint) {
                return;
            }

            int oldDepth = GUI.depth;
            GUI.depth = -10000;

            Color oldColor = m_style.normal.textColor;

            m_style.normal.textColor = shadowColor;
            GUI.Label (new Rect (m_rect.x + 1f, m_rect.y + 1f, m_rect.width, m_rect.height), m_content, m_style);

            m_style.normal.textColor = textColor;
            GUI.Label (m_rect, m_content, m_style);

            m_style.normal.textColor = oldColor;
            GUI.depth = oldDepth;
        }

        private void BuildStyle () {
            m_style = new GUIStyle ();
            m_style.alignment = TextAnchor.UpperLeft;
            m_style.fontSize = fontSize;
            m_style.fontStyle = FontStyle.Bold;
            m_style.clipping = TextClipping.Overflow;
        }

        private void UpdateLayout () {
            Vector2 size = m_style.CalcSize (m_content);
            float width = size.x + 4f;
            float height = Mathf.Max (size.y, fontSize + 4f);
            float x = screenOffset.x;
            float y = screenOffset.y;

            if (corner == ScreenCorner.TopRight || corner == ScreenCorner.BottomRight) {
                x = Screen.width - width - screenOffset.x;
            }

            if (corner == ScreenCorner.BottomLeft || corner == ScreenCorner.BottomRight) {
                y = Screen.height - height - screenOffset.y;
            }

            m_rect = new Rect (x, y, width, height);
        }
    }
}
