using UnityEngine;
using UnityEngine.UI;

namespace MTD
{
    public sealed class DiamondRelay : MonoBehaviour
    {
        public RawImage seam;
        public int slots;
        Material ink;
        double start;
        static readonly int elapsed = Shader.PropertyToID("_Elapsed");

        public void Play()
        {
            if (ink == null)
            {
                ink = new Material(seam.material);
                ink.SetFloat("_Slots", slots);
                seam.material = ink;
            }
            start = Time.unscaledTimeAsDouble;
            Sample(0);
            enabled = true;
        }

        void Update() => Sample((float)(Time.unscaledTimeAsDouble - start));

        internal void Sample(float seconds)
        {
            if (seconds >= 2.24f)
            {
                seam.enabled = false;
                enabled = false;
                return;
            }
            ink.SetFloat(elapsed, seconds);
            seam.enabled = true;
        }

        void OnDisable()
        {
            if (seam != null) seam.enabled = false;
        }

        void OnDestroy()
        {
            if (ink == null) return;
            if (Application.isPlaying) Destroy(ink);
            else DestroyImmediate(ink);
        }
    }
}
