using System;
using System.Collections;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MTD
{
    public sealed class ColumnView : MonoBehaviour
    {
        public const int HistoryLength = 11;
        public const float RowPitch = 130f;
        public const float RoulettePitch = 125f;

        public Image headerTint, bodyTint;
        public Image[] historyFields;
        public bool softenHistoryTint;
        public TMP_Text tableLabel, reflection, minimum, maximum, currentState;
        public Image leftLogo, rightLogo;
        public Sprite powerReel, xtra;
        public RectTransform current, history;
        public CanvasGroup surface;
        public RawImage headerLight, chipBurst, backplate;
        public RectTransform[] currentParts, historyRows;
        public RectTransform rouletteTail;
        internal ColumnsRouletteWidget Roulette { private get; set; }

        Vector2[] rest;
        float started;
        Action drawCurrent, copyTail;
        Action<int> drawRow;
        int rowsChanged;
        bool heroChanged;
        bool Sliding => rouletteTail == null;

        public void Tint(Color colour)
        {
            colour.a = 1;
            headerTint.color = bodyTint.color = colour;
            Color metal = softenHistoryTint ? new Color32(146,207,220,255) : new Color32(227,169,105,255);
            backplate.color = Color.Lerp(metal, colour, .18f);
            Color field = Color.Lerp(colour, Color.white, softenHistoryTint ? .36f : .48f);
            field.a = softenHistoryTint ? .095f : .15f;
            foreach (Image background in historyFields) background.color = field;
        }

        public void Header(TableSnapshot table, RouletteVariant variant = RouletteVariant.Standard)
        {
            string name = string.IsNullOrWhiteSpace(table.TableName) ? "TABLE" : table.TableName;
            tableLabel.text = reflection.text = name + " " + table.TableNumber.ToString("D2", CultureInfo.InvariantCulture);
            minimum.text = table.MinBetText;
            maximum.text = table.MaxBetText;
            Sprite logo = variant == RouletteVariant.PowerReel ? powerReel : variant == RouletteVariant.Xtra ? xtra : null;
            leftLogo.sprite = rightLogo.sprite = logo;
            leftLogo.enabled = rightLogo.enabled = logo != null;
            float inset = logo != null ? 132 : 26;
            SetLabelInsets(tableLabel.rectTransform, inset);
            SetLabelInsets(reflection.rectTransform, inset);

            // Integer UV offsets select the effect and its phase without instantiating a material per slot.
            int style = variant == RouletteVariant.PowerReel ? 1 : variant == RouletteVariant.Xtra ? 2 : 0;
            var layout = GetComponentInParent<MtdLayout>();
            int slot = layout != null ? Array.IndexOf(layout.slots, transform.parent as RectTransform) : 0;
            headerLight.uvRect = new Rect(2 * style, (slot % 2) * 2, 1, 1);
            backplate.uvRect = new Rect(0, 2 * Mathf.Max(0, slot), 1, 1);
        }

        static void SetLabelInsets(RectTransform label, float inset)
        {
            label.offsetMin = new Vector2(inset, label.offsetMin.y);
            label.offsetMax = new Vector2(-inset, label.offsetMax.y);
        }

        public void CurrentState(bool readable, bool unavailable)
        {
            currentState.gameObject.SetActive(!readable);
            if (!readable) currentState.text = unavailable ? "Result unavailable" : "Awaiting first result";
        }

        public void Bind(Action hero, Action<int> row)
        {
            if (rest == null)
                rest = Array.ConvertAll(historyRows, rect => rect.anchoredPosition);
            ResetMotion();
            surface.alpha = 1;
            DrawAll(hero, row);
        }

        void DrawAll(Action hero, Action<int> row)
        {
            hero();
            for (int i = 0; i < historyRows.Length; i++) row(i);
        }

        public void Refresh(TableChange change, bool readable, Action hero, Action<int> row, Action tail = null)
        {
            if (change == TableChange.NewResult && readable)
            {
                if (drawCurrent != null && (rowsChanged > 0 || heroChanged))
                {
                    DrawAll(drawCurrent, drawRow);
                    ResetMotion();
                }
                if (drawCurrent == null) started = Time.unscaledTime;
                drawCurrent = hero;
                drawRow = row;
                copyTail = tail;
            }
            else if (readable && drawCurrent != null && (change == TableChange.Updated || change == TableChange.Heartbeat))
            {
                // The result list is identical; pending rows can consume the latest metadata.
                drawCurrent = hero;
                drawRow = row;
            }
            else
            {
                ResetMotion();
                DrawAll(hero, row);
            }
        }

        void Update()
        {
            if (drawCurrent != null) Sample(Time.unscaledTime - started);
        }

        // The two films use a 30 fps timeline. Sampling elapsed time keeps delayed frames
        // and simultaneous updates on the same final pose as real-time playback.
        internal void Sample(float t)
        {
            if (drawCurrent == null) return;
            float reveal = Sliding ? 1.76f : .94f;
            float stagger = Sliding ? 4f / 30f : 0;
            if (!heroChanged && t >= .30f + stagger * (currentParts.Length - 1))
            {
                drawCurrent();
                heroChanged = true;
            }
            for (int i = 0; i < currentParts.Length; i++)
            {
                float local = t - i * stagger;
                float scale = local < reveal ? 1 - Ease(local / .267f) : Arrive((local - reveal) / .40f);
                currentParts[i].localScale = Vector3.one * scale;
            }

            if (Sliding) SlideDiceRows(t);
            else
            {
                ScrollSpins(t);
                Roulette?.SampleTransfer(t, rowsChanged > 0);
            }

            if (t >= (Sliding ? 3.55f : 2.5f)) ResetMotion();
        }

        void SlideDiceRows(float t)
        {
            // The right edge is the local RectMask2D, also when Sic Bo is a middle slot.
            float distance = history.rect.width;
            for (int i = 0; i < historyRows.Length; i++)
            {
                float local = t - .65f - i * (5f / 30f);
                if (local >= .36f && rowsChanged == i)
                {
                    drawRow(i);
                    rowsChanged++;
                }
                float offset = local < .36f ? Ease(local / .36f) : 1 - Ease((local - .57f) / .46f);
                historyRows[i].anchoredPosition = rest[i] + Vector2.right * (distance * offset);
            }
        }

        void ScrollSpins(float t)
        {
            const float start = .44f;
            if (t < start) return;
            if (rowsChanged == 0)
            {
                copyTail();
                for (int i = 0; i < historyRows.Length; i++) drawRow(i);
                rowsChanged = historyRows.Length;
            }
            for (int i = 0; i < historyRows.Length; i++)
            {
                float p = SpinTravel(t - start - i / 30f);
                historyRows[i].anchoredPosition = rest[i] + Vector2.up * (RoulettePitch * (1 - p));
            }
            float tail = t - start - historyRows.Length / 30f;
            rouletteTail.anchoredPosition = rest[rest.Length - 1] + Vector2.down * (RoulettePitch * SpinTravel(tail));
            if (tail >= .60f) rouletteTail.gameObject.SetActive(false);
            float burst = (t - .94f) / 1.45f;
            chipBurst.enabled = burst >= 0 && burst < 1 && currentParts[1].gameObject.activeSelf;
            if (chipBurst.enabled) chipBurst.uvRect = new Rect(6 + 2 * Mathf.Min(43, (int)(burst * 44)), 0, 1, 1);
        }

        static float Ease(float p) => Mathf.SmoothStep(0, 1, Mathf.Clamp01(p));

        // Bound the peak speed so two balls in the same lane stay visibly separated
        // during the one-frame ripple, including the outgoing eleventh ball.
        static float SpinTravel(float elapsed)
        {
            if (elapsed >= .50f) return Mathf.Lerp(1.02f, 1, Ease((elapsed - .50f) / .10f));
            float t = Mathf.Clamp01(elapsed / .50f);
            const float ramp = .14f;
            float motion = t < ramp ? t*t/(2*ramp) : t > 1-ramp
                ? 1-ramp - (1-t)*(1-t)/(2*ramp) : t-ramp*.5f;
            return motion * (1.02f / (1-ramp));
        }

        static float Arrive(float p)
        {
            // The new winner and each scrolling ball pass their resting size or position once.
            if (p <= .82f) return 1.027f * Ease(p / .82f);
            return Mathf.Lerp(1.027f, 1, Ease((p - .82f) / .18f));
        }

        public IEnumerator Fade(bool entering)
        {
            float from = entering ? 0 : surface.alpha;
            float to = entering ? 1 : 0;
            float start = Time.unscaledTime;
            float t;
            do
            {
                t = Mathf.Clamp01((Time.unscaledTime - start) / .18f);
                surface.alpha = Mathf.Lerp(from, to, Ease(t));
                if (t < 1) yield return null;
            } while (t < 1);
        }

        void ResetMotion()
        {
            Roulette?.EndTransfer();
            drawCurrent = null;
            drawRow = null;
            copyTail = null;
            rowsChanged = 0;
            heroChanged = false;
            if (rest == null) return;
            foreach (var part in currentParts) part.localScale = Vector3.one;
            for (int i = 0; i < rest.Length; i++) historyRows[i].anchoredPosition = rest[i];
            if (rouletteTail != null) rouletteTail.gameObject.SetActive(false);
            if (chipBurst != null) chipBurst.enabled = false;
        }

        void OnDisable() => ResetMotion();
    }
}
