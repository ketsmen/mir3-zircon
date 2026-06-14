namespace PluginCore
{
    public abstract class AbstractPlugin : IPluginType
    {
        public IPluginStart Start { get; set; }

        public abstract void SetupMenu(IPluginMenuHost menuHost);
    }
}
