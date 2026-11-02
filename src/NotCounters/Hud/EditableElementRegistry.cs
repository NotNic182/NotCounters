using System;
using System.Collections.Generic;
using NotCounters.Contracts;
using NotCounters.Util;

namespace NotCounters.Hud
{
    internal sealed class EditableElementRegistry : IEditableElementRegistry
    {
        private readonly List<IEditableElement> _elements = new List<IEditableElement>();

        public event Action? ElementsChanged;

        public IReadOnlyList<IEditableElement> Elements => _elements;

        public void Register(IEditableElement element)
        {
            if (element == null || _elements.Contains(element))
            {
                return;
            }

            _elements.Add(element);
            RaiseChanged();
        }

        public void Unregister(IEditableElement element)
        {
            if (element != null && _elements.Remove(element))
            {
                RaiseChanged();
            }
        }

        private void RaiseChanged()
        {
            var handler = ElementsChanged;
            if (handler != null)
            {
                Safe.Invoke("hud:registry-changed", handler);
            }
        }
    }
}
