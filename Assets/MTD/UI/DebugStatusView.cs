using TMPro;
using UnityEngine;

namespace MTD
{
    // The debug UI's status display: the headline across the middle when there is one, and the details in a strip
    // along the bottom all the time.
    public sealed class DebugStatusView : MtdStatusView
    {
        public GameObject headlinePanel;
        public TMP_Text headline;
        public TMP_Text details;

        public override void Show(MtdStatus status)
        {
            if (headlinePanel != null) headlinePanel.SetActive(!string.IsNullOrEmpty(status.Headline));
            if (headline != null) headline.text = status.Headline;
            if (details != null) details.text = status.Details;
        }
    }
}
