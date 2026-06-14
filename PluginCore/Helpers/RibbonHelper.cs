using System;
using System.Drawing;

namespace PluginCore.Helpers
{
    public static class RibbonHelper
    {
        public static IPluginMenuGroup AddGroup(IPluginStart start, IPluginMenuHost menuHost, string title)
        {
            return menuHost.AddGroup(start, title);
        }

        public static void AddButton(IPluginStart start, IPluginMenuGroup group, string caption, Image largeIcon, EventHandler clickHandler)
        {
            group.AddButton(caption, largeIcon, clickHandler);
        }
    }
}
