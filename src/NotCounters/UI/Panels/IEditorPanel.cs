using System;
using BeatSaberMarkupLanguage;
using UnityEngine;

namespace NotCounters.UI.Panels
{
    internal interface IEditorPanel
    {
        string Name { get; }

        string ViewResource { get; }

        void PrepareView();

        void Attach(BSMLParser parser, Action<GameObject> prepareRows);

        void OnShown();

        void LateTick();

        void Detach();
    }
}
