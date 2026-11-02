using System;
using NotCounters.Contracts;
using NotCounters.Util;
using Zenject;

namespace NotCounters.Editor
{
    internal sealed class EditSelection : IInitializable, IDisposable
    {
        private readonly IEditableElementRegistry? _registry;
        private EditableElementKind _selectedKind;
        private string? _selectedId;

        private bool _selectedIsInScene;

        public EditSelection([InjectOptional] IEditableElementRegistry? registry)
        {
            _registry = registry;
        }

        public event Action? Changed;

        public string? SelectedId => _selectedId;

        public EditableElementKind SelectedKind => _selectedKind;

        public IEditableElement? Selected => _selectedId == null ? null : Find(_selectedKind, _selectedId);

        public void Initialize()
        {
            if (_registry != null)
            {
                _registry.ElementsChanged += HandleElementsChanged;
            }
        }

        public void Dispose()
        {
            if (_registry != null)
            {
                _registry.ElementsChanged -= HandleElementsChanged;
            }
        }

        public void Select(IEditableElement? element)
        {
            if (element == null)
            {
                Select(EditableElementKind.Vanilla, null);
            }
            else
            {
                Select(element.Kind, element.Id);
            }
        }

        public void Select(EditableElementKind kind, string? id)
        {
            if (string.Equals(id, _selectedId, StringComparison.Ordinal) && (id == null || kind == _selectedKind))
            {
                return;
            }

            _selectedId = id;
            _selectedKind = kind;
            _selectedIsInScene = Selected != null;
            RaiseChanged();
        }

        public bool IsSelected(EditableElementKind kind, string id) =>
            _selectedId != null && kind == _selectedKind && string.Equals(id, _selectedId, StringComparison.Ordinal);

        public void EnsureSelection()
        {
            if (_selectedId != null || _registry == null)
            {
                return;
            }

            foreach (var element in _registry.Elements)
            {
                if (element != null && element.Root != null)
                {
                    Select(element);
                    return;
                }
            }
        }

        private IEditableElement? Find(EditableElementKind kind, string id)
        {
            if (_registry == null)
            {
                return null;
            }

            var elements = _registry.Elements;
            for (var i = 0; i < elements.Count; i++)
            {
                var element = elements[i];
                if (element != null && element.Kind == kind && string.Equals(element.Id, id, StringComparison.Ordinal) && element.Root != null)
                {
                    return element;
                }
            }

            return null;
        }

        private void HandleElementsChanged()
        {
            var wasInScene = _selectedIsInScene;
            _selectedIsInScene = Selected != null;
            if (wasInScene && !_selectedIsInScene && _selectedKind == EditableElementKind.Vanilla)
            {
                _selectedId = null;
            }

            RaiseChanged();
        }

        private void RaiseChanged()
        {
            var handlers = Changed;
            if (handlers == null)
            {
                return;
            }

            foreach (var handler in handlers.GetInvocationList())
            {
                Safe.Invoke("EditSelection.Changed", (Action)handler);
            }
        }
    }
}
