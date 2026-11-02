using System;
using System.Collections.Generic;

namespace NotCounters.Core.Editing
{
    public sealed class UndoHistory<TState>
        where TState : class
    {
        public const int DefaultCapacity = 50;

        public const float CoalesceSeconds = 1f;

        private readonly Func<TState> _capture;
        private readonly int _capacity;
        private readonly LinkedList<TState> _undo = new LinkedList<TState>();
        private readonly Stack<TState> _redo = new Stack<TState>();

        private TState? _current;

        private TState? _stepStart;

        private string? _lastKey;
        private float _lastEditEnd;

        public UndoHistory(Func<TState> capture, int capacity = DefaultCapacity)
        {
            _capture = capture ?? throw new ArgumentNullException(nameof(capture));
            _capacity = Math.Max(1, capacity);
        }

        public bool IsStarted { get; private set; }

        public bool CanUndo => _undo.Count > 0;

        public bool CanRedo => _redo.Count > 0;

        public int UndoCount => _undo.Count;

        public int RedoCount => _redo.Count;

        public void Start()
        {
            Clear();
            _current = _capture();
            IsStarted = true;
        }

        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
            _current = null;
            _stepStart = null;
            _lastKey = null;
            IsStarted = false;
        }

        public bool Joins(string? coalesceKey, float now) =>
            coalesceKey != null
            && _undo.Count > 0
            && string.Equals(coalesceKey, _lastKey, StringComparison.Ordinal)
            && now - _lastEditEnd <= CoalesceSeconds;

        public bool BeforeEdit(string? coalesceKey, float now)
        {
            var joins = Joins(coalesceKey, now);
            _stepStart = joins ? null : Current();
            return joins;
        }

        public void AfterEdit(string? coalesceKey, float now)
        {
            if (_stepStart != null)
            {
                PushUndo(_stepStart);
                _redo.Clear();
                _stepStart = null;
            }

            _current = null;
            _lastKey = coalesceKey;
            _lastEditEnd = now;
        }

        public TState? Undo()
        {
            if (_undo.Count == 0)
            {
                return null;
            }

            var live = Current();
            var target = _undo.Last!.Value;
            _undo.RemoveLast();
            _redo.Push(live);
            Restoring();
            return target;
        }

        public TState? Redo()
        {
            if (_redo.Count == 0)
            {
                return null;
            }

            var live = Current();
            var target = _redo.Pop();
            PushUndo(live);
            Restoring();
            return target;
        }

        private void Restoring()
        {
            _current = null;
            _lastKey = null;
        }

        private TState Current() => _current ??= _capture();

        private void PushUndo(TState state)
        {
            _undo.AddLast(state);
            while (_undo.Count > _capacity)
            {
                _undo.RemoveFirst();
            }
        }
    }
}
