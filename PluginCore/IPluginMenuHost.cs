using System;
using System.Drawing;

namespace PluginCore
{
    public interface IPluginMenuHost
    {
        IPluginMenuGroup AddGroup(IPluginStart start, string title);
    }

    public interface IPluginMenuGroup
    {
        void AddButton(string caption, Image largeIcon, EventHandler clickHandler);
    }
}
