using NotCounters.Util;
using UnityEngine;
using UnityEngine.EventSystems;

namespace NotCounters.Editor
{
    internal interface IEditHandleListener
    {
        void OnHandlePointerEnter(EditFrame frame);

        void OnHandlePointerExit(EditFrame frame);

        void OnHandlePointerDown(EditFrame frame, PointerEventData eventData);

        void OnHandlePointerUp(EditFrame frame, PointerEventData eventData);
    }

    internal sealed class EditHandle : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        private EditFrame? _frame;
        private IEditHandleListener? _listener;

        internal void Bind(EditFrame frame, IEditHandleListener listener)
        {
            _frame = frame;
            _listener = listener;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_frame != null && _listener != null)
            {
                Safe.Invoke("EditHandle.PointerEnter", () => _listener.OnHandlePointerEnter(_frame));
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_frame != null && _listener != null)
            {
                Safe.Invoke("EditHandle.PointerExit", () => _listener.OnHandlePointerExit(_frame));
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_frame != null && _listener != null && eventData.button == PointerEventData.InputButton.Left)
            {
                Safe.Invoke("EditHandle.PointerDown", () => _listener.OnHandlePointerDown(_frame, eventData));
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (_frame != null && _listener != null && eventData.button == PointerEventData.InputButton.Left)
            {
                Safe.Invoke("EditHandle.PointerUp", () => _listener.OnHandlePointerUp(_frame, eventData));
            }
        }
    }
}
