using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace ScenarioSystem.Actions
{
    /// <summary>
    /// Command that updates the text of a UI Text or TextMesh component.
    /// </summary>
    [Serializable]
    public class UpdateTextComponentCommand : BaseCommand
    {
        public enum TextComponentType
        {
            UnityText,
            TextMesh,
            TMProText,
            TMProTextMeshPro
        }

        [SerializeField]
        private TextComponentType _textType = TextComponentType.UnityText;

        [SerializeField]
        private ProxyObjectSaver<Component> _textComponent = new ProxyObjectSaver<Component>();

        [SerializeField]
        [TextArea(2, 5)]
        private string _newText = "";

        [SerializeField]
        private bool _appendText = false;

        public TextComponentType TextType
        {
            get => _textType;
            set => _textType = value;
        }

        public Component TextComponent
        {
            get => _textComponent.Value;
            set => _textComponent.Value = value;
        }

        public string NewText
        {
            get => _newText;
            set => _newText = value;
        }

        public bool AppendText
        {
            get => _appendText;
            set => _appendText = value;
        }

        public UpdateTextComponentCommand()
        {
        }

        public UpdateTextComponentCommand(Component textComponent, string text, TextComponentType type = TextComponentType.UnityText)
        {
            _textComponent = new ProxyObjectSaver<Component>(true) { Value = textComponent };
            _newText = text;
            _textType = type;
        }

        public override Task Execute()
        {
            if (_textComponent.Value == null)
            {
                Debug.LogWarning("[UpdateTextComponentCommand] Text component is null.");
                return Task.CompletedTask;
            }

            string textToSet = _newText;

            switch (_textType)
            {
                case TextComponentType.UnityText:
                    if (_textComponent.Value is Text uiText)
                    {
                        if (_appendText)
                            textToSet = uiText.text + _newText;
                        uiText.text = textToSet;
                    }
                    break;

                case TextComponentType.TextMesh:
                    if (_textComponent.Value is TextMesh textMesh)
                    {
                        if (_appendText)
                            textToSet = textMesh.text + _newText;
                        textMesh.text = textToSet;
                    }
                    break;

                case TextComponentType.TMProText:
                case TextComponentType.TMProTextMeshPro:
                    // Use reflection to support TMPro without hard dependency
                    SetTMProText(_textComponent.Value, textToSet);
                    break;
            }

            return Task.CompletedTask;
        }

        private void SetTMProText(Component component, string text)
        {
            var type = component.GetType();
            var textProperty = type.GetProperty("text");

            if (textProperty != null)
            {
                if (_appendText)
                {
                    var currentText = textProperty.GetValue(component) as string ?? "";
                    text = currentText + text;
                }
                textProperty.SetValue(component, text);
            }
            else
            {
                Debug.LogWarning($"[UpdateTextComponentCommand] Could not find 'text' property on {type.Name}");
            }
        }

        public override void OnSave(IUnityObjectSaver saver)
        {
            _textComponent.Save(saver);
        }

        public override void OnRestore(IUnityObjectSaver saver)
        {
            _textComponent.Restore(saver);
        }

        public override void ReleaseResources(IUnityObjectSaver saver)
        {
            _textComponent.ReleaseResources(saver);
        }
    }
}
