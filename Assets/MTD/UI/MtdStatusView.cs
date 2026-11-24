using UnityEngine;

namespace MTD
{
    // Sits in a layout prefab and shows the display's status. Optional: a layout without one shows no status.
    public abstract class MtdStatusView : MonoBehaviour
    {
        public abstract void Show(MtdStatus status);
    }

    // What the display should tell someone standing in front of it when it is not simply showing tables.
    //
    // Both texts are plain text and can hold file paths and words that came from a broker. Show them with rich text
    // and escape-sequence parsing turned off (TMP: richText and parseCtrlCharacters), or "C:\tcs" is drawn as a tab.
    public sealed class MtdStatus
    {
        public string Headline = ""; // One or two short lines for any MTD UI to show. Empty when everything is working.
        public string Details = ""; // Several lines for diagnosis. The debug UI shows them; a styled UI may not.

        public bool SameAs(MtdStatus other)
        {
            return other != null && Headline == other.Headline && Details == other.Details;
        }
    }
}
