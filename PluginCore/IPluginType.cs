namespace PluginCore
{
    public interface IPluginType
    {
        IPluginStart Start { get; set; }

        /// <summary>
        /// Initialise any menu items to appear under the plugin tab.
        /// </summary>
        /// <param name="menuHost"></param>
        void SetupMenu(IPluginMenuHost menuHost);
    }
}
