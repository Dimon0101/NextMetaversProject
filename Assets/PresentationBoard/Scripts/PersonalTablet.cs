using UdonSharp;
using UnityEngine;

namespace NextMetavers
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class PersonalTablet : UdonSharpBehaviour
    {
        public PresentationBoard board;
        public GameObject panel;
        public Transform anchor;
        public GameObject tablet;

        private Transform _openedAt;
        private GameObject _hiddenTablet;

        public void Toggle()
        {
            if (anchor == null) return;

            if (panel.activeSelf && _openedAt == anchor)
            {
                Hide();
                return;
            }

            ShowTablet();
            transform.SetPositionAndRotation(anchor.position, anchor.rotation);
            _openedAt = anchor;
            _hiddenTablet = tablet;
            if (tablet != null) tablet.SetActive(false);
            panel.SetActive(true);
            if (board != null) board.RefreshPanels();
        }

        public void Hide()
        {
            panel.SetActive(false);
            _openedAt = null;
            ShowTablet();
        }

        public void HideIfAnchor()
        {
            if (_openedAt != null && _openedAt == anchor)
            {
                panel.SetActive(false);
                _openedAt = null;
                _hiddenTablet = null;
            }
        }

        private void ShowTablet()
        {
            if (_hiddenTablet != null) _hiddenTablet.SetActive(true);
            _hiddenTablet = null;
        }
    }
}
