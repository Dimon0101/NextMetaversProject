using UdonSharp;
using UnityEngine;

namespace NextMetavers
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class SeatTablet : UdonSharpBehaviour
    {
        public PersonalTablet personalTablet;
        public Transform anchor;
        public GameObject tablet;

        public override void Interact()
        {
            if (personalTablet == null) return;

            personalTablet.anchor = anchor;
            personalTablet.tablet = tablet;
            personalTablet.Toggle();
        }
    }
}
