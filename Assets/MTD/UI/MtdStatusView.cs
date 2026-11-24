using UnityEngine;

namespace MTD
{
    // Sits in a layout prefab and shows the display's status. Optional: a layout without one shows no status.
    public abstract class MtdStatusView : MonoBehaviour
    {
        public abstract void Show(MtdStatus status);
    }

    // What the display should tell someone standing in front of it when it is not simply showing tables.
    public sealed class MtdStatus
    {
        public string Headline = ""; // One line for any MTD UI to show. Empty when everything is working.
        public string Details = ""; // Several lines for diagnosis. The debug UI shows them; a styled UI may not.

        public bool SameAs(MtdStatus other)
        {
            return other != null && Headline == other.Headline && Details == other.Details;
        }
    }
}
