using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace NextMetavers
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class SeatStation : UdonSharpBehaviour
    {
        public GameObject tablet;
        public PersonalTablet personalTablet;
        public Transform anchor;

        public override void OnStationEntered(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !player.isLocal) return;

            if (tablet != null) tablet.SetActive(true);
        }

        public override void OnStationExited(VRCPlayerApi player)
        {
            if (!Utilities.IsValid(player) || !player.isLocal) return;

            if (tablet != null) tablet.SetActive(false);
            if (personalTablet == null) return;

            personalTablet.anchor = anchor;
            personalTablet.HideIfAnchor();
        }
    }
}
