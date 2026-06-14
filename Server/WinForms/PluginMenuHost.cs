using PluginCore;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Server.WinForms
{
    public sealed class PluginMenuHost : IPluginMenuHost
    {
        private readonly ToolStripMenuItem _pluginMenu;

        public PluginMenuHost(ToolStripMenuItem pluginMenu)
        {
            _pluginMenu = pluginMenu;
        }

        public IPluginMenuGroup AddGroup(IPluginStart start, string title)
        {
            ToolStripMenuItem group = new ToolStripMenuItem(title)
            {
                Name = $"{start.Name}Group"
            };

            _pluginMenu.DropDownItems.Add(group);
            return new PluginMenuGroup(group);
        }

        private sealed class PluginMenuGroup : IPluginMenuGroup
        {
            private readonly ToolStripMenuItem _group;

            public PluginMenuGroup(ToolStripMenuItem group)
            {
                _group = group;
            }

            public void AddButton(string caption, Image largeIcon, EventHandler clickHandler)
            {
                ToolStripMenuItem item = new ToolStripMenuItem(caption)
                {
                    Image = largeIcon
                };

                item.Click += clickHandler;
                _group.DropDownItems.Add(item);
            }
        }
    }
}
