namespace NotCounters.Editor
{
    internal sealed class EditSessionMarker
    {
        public EditSessionMarker(string environmentSerializedName)
        {
            EnvironmentSerializedName = environmentSerializedName;
        }

        public string EnvironmentSerializedName { get; }
    }
}
