using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using WheelchairSonification.Runtime;

namespace WheelchairSonification.XR
{
    // Does not depend on an Android system keyboard. Edits are committed explicitly.
    public sealed class XrNumberPad
    {
        private readonly RectTransform panel;
        private readonly Text value;
        private InputField target;
        private string buffer;
        public GameObject Root => panel.gameObject;

        public XrNumberPad(DemoUi ui, Transform parent)
        {
            panel = ui.Card(parent, "数値入力", 480, 180, 480, 570);
            value = ui.Text(panel, "Editing value", "", 24, 55, 432, 95, 25);
            string[] keys = { "7", "8", "9", "4", "5", "6", "1", "2", "3", "0", ".", "," };
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                ui.Button(panel, key, 24 + i % 3 * 146, 164 + i / 3 * 62, 136, () => Press(key));
            }
            ui.Button(panel, "一文字削除", 24, 420, 210, () => Press("backspace"));
            ui.Button(panel, "全消去", 246, 420, 210, () => Press("clear"));
            ui.Button(panel, "取消", 24, 490, 210, Cancel);
            ui.Button(panel, "確定", 246, 490, 210, Commit, true);
            panel.gameObject.SetActive(false);
        }

        public void Bind(InputField field)
        {
            field.shouldHideMobileInput = true;
            field.shouldActivateOnSelect = false;
            // uGUI's OnPointerClick activates the native Android keyboard even with
            // shouldActivateOnSelect=false. Keep the value/label but let only our trigger edit it.
            field.DeactivateInputField();
            field.enabled = false;
            var trigger = field.gameObject.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            entry.callback.AddListener(_ => { if (field.interactable) Open(field); });
            trigger.triggers.Add(entry);
        }

        public void Open(InputField field)
        {
            target = field;
            buffer = field.text;
            value.text = buffer;
            panel.SetAsLastSibling();
            panel.gameObject.SetActive(true);
        }

        public void Press(string key)
        {
            if (target == null) return;
            if (key == "clear") buffer = "";
            else if (key == "backspace") buffer = buffer.Length == 0 ? "" : buffer.Substring(0, buffer.Length - 1);
            else if (key.Length == 1 && "0123456789.,".Contains(key) && buffer.Length < 300) buffer += key;
            value.text = buffer;
        }

        public void Commit()
        {
            if (target != null && target.interactable) target.text = buffer;
            Cancel();
        }

        public void Cancel() { target = null; panel.gameObject.SetActive(false); }
    }
}
