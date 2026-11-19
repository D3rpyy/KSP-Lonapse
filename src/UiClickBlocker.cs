// An invisible uGUI panel under an IMGUI window. Input locks only stop KSP's own input (parts, camera, editor), while
// the stock UI (part menus, staging, navball, toolbar, editor tabs) listens to Unity's EventSystem and ignores them.
// The panel sits on an overlay canvas above that UI and swallows the clicks. IMGUI does not use the EventSystem, so
// the window on top keeps working.
using UnityEngine;
using UnityEngine.UI;

namespace Lonapse
{
    internal sealed class UiClickBlocker
    {
        // overlay canvases win the EventSystem raycast from KSP's camera canvases, the high order keeps us above other overlays
        private const int SortingOrder = 32000;

        private readonly string name;
        private GameObject root;
        private RectTransform panel;

        public UiClickBlocker(string name) { this.name = name; }

        /// <summary>Covers the window rect, given in IMGUI coordinates (origin top left).</summary>
        public void Cover(Rect rect)
        {
            if (root == null) Create(); // also after a scene change destroyed it
            panel.anchoredPosition = new Vector2(rect.x, Screen.height - rect.yMax);
            panel.sizeDelta = new Vector2(rect.width, rect.height);
            if (!root.activeSelf) root.SetActive(true);
        }

        public void Hide()
        {
            if (root != null && root.activeSelf) root.SetActive(false);
        }

        public void Destroy()
        {
            if (root != null) Object.Destroy(root);
            root = null;
            panel = null;
        }

        private void Create()
        {
            root = new GameObject(name);
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            root.AddComponent<GraphicRaycaster>();

            GameObject child = new GameObject(name + "_panel", typeof(RectTransform));
            child.transform.SetParent(root.transform, false);
            panel = (RectTransform)child.transform;
            panel.anchorMin = panel.anchorMax = panel.pivot = Vector2.zero;
            Image image = child.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0f); // fully transparent, it still catches raycasts
            image.raycastTarget = true;
        }
    }
}
