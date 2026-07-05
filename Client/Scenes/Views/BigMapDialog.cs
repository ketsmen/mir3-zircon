using Client.Controls;
using Client.Envir;
using Client.Models;
using Shared.Rendering;
using Client.UserModels;
using Library;
using Library.SystemModels;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using C = Library.Network.ClientPackets;

namespace Client.Scenes.Views
{
    public sealed class BigMapDialog : DXWindow
    {
        #region Properties

        #region SelectedInfo

        public MapInfo SelectedInfo
        {
            get { return _SelectedInfo; }
            set
            {
                if (_SelectedInfo == value) return;

                MapInfo oldValue = _SelectedInfo;
                _SelectedInfo = value;

                OnSelectedInfoChanged(oldValue, value);
            }
        }
        private MapInfo _SelectedInfo;
        public event EventHandler<EventArgs> SelectedInfoChanged;
        public void OnSelectedInfoChanged(MapInfo oValue, MapInfo nValue)
        {
            SelectedInfoChanged?.Invoke(this, EventArgs.Empty);

            ClearMapInfoObjects();
            ClearInstanceMapCache();

            if (SelectedInfo == null) return;

            TitleLabel.Text = SelectedInfo.PlayerDescription;
            Image.Index = SelectedInfo.MiniMap;

            var minWidth = 320;
            var minHeight = 240;

            var maxWidth = 800;
            var maxHeight = 600 - 80;

            SetClientSize(Image.Size, minWidth, minHeight, maxWidth, maxHeight);

            var imageLargerThanPanel = (Image.Size.Width > maxWidth || Image.Size.Height > maxHeight);

            Image.Movable = imageLargerThanPanel;
            Image.IgnoreMoveBounds = imageLargerThanPanel;

            var locationX = (Image.Size.Width - Panel.Size.Width) / 2;
            var locationY = (Image.Size.Height - Panel.Size.Height) / 2;

            Point defaultImageLocation = new Point(-locationX, -locationY);

            Image.Location = new Point(-locationX, -locationY);

            RecenterButton.Location = new Point(Size.Width - 30 - 80, Size.Height - 43);
            RecenterButton.Enabled = SelectedInfo != GameScene.Game.MapControl.MapInfo;

            Location = new Point((GameScene.Game.Size.Width - Size.Width) / 2, (GameScene.Game.Size.Height - Size.Height) / 2);

            Size size = GetMapSize(SelectedInfo);
            ScaleX = size.Width > 0 && Image.Size.Width > 0 ? Image.Size.Width / (float)size.Width : 1F;
            ScaleY = size.Height > 0 && Image.Size.Height > 0 ? Image.Size.Height / (float)size.Height : 1F;

            Image.Location = SelectedInfo == GameScene.Game.MapControl.MapInfo
                ? GetUserCentredImageLocation(defaultImageLocation)
                : defaultImageLocation;

            foreach (NPCInfo ob in Globals.NPCInfoList.Binding)
                Update(ob);

            foreach (MovementInfo ob in Globals.MovementInfoList.Binding)
                Update(ob);

            foreach (ClientObjectData ob in GameScene.Game.DataDictionary.Values)
                Update(ob);
        }

        private Point GetUserCentredImageLocation(Point fallbackLocation)
        {
            if (Image == null || Panel == null) return fallbackLocation;
            if (MapObject.User == null) return fallbackLocation;
            if (SelectedInfo != GameScene.Game.MapControl.MapInfo) return fallbackLocation;

            Point userLocation = MapObject.User.CurrentLocation;
            Size panelSize = Panel.Size;

            float userPixelX = ScaleX * userLocation.X;
            float userPixelY = ScaleY * userLocation.Y;

            int targetX = (int)Math.Round(panelSize.Width / 2f - userPixelX);
            int targetY = (int)Math.Round(panelSize.Height / 2f - userPixelY);

            int minX = Math.Min(0, panelSize.Width - Image.Size.Width);
            int maxX = Math.Max(0, panelSize.Width - Image.Size.Width);
            int minY = Math.Min(0, panelSize.Height - Image.Size.Height);
            int maxY = Math.Max(0, panelSize.Height - Image.Size.Height);

            targetX = Math.Max(minX, Math.Min(maxX, targetX));
            targetY = Math.Max(minY, Math.Min(maxY, targetY));

            return new Point(targetX, targetY);
        }

        public void ToggleOpen(bool open)
        {
            if (open)
            {
                if (!TryShowMap(GameScene.Game.MapControl.MapInfo))
                {
                    return;
                }

                Opacity = 1F;
                Visible = true;
            }
            else
            {
                Visible = false;
            }
        }

        public bool TryShowMap(MapInfo map, bool sendMessage = true)
        {
            if (map == null || map.MiniMap == 0)
            {
                if (sendMessage)
                    GameScene.Game.ReceiveChat("没有可用地图。", MessageType.System);

                return false;
            }

            return true;
        }

        public void SetClientSize(Size clientSize, int minWidth, int minHeight, int maxWidth, int maxHeight)
        {
            SetClientSize(new Size(Math.Min(Math.Max(clientSize.Width, minWidth), maxWidth), Math.Min(Math.Max(clientSize.Height, minHeight), maxHeight)));
        }

        private Size GetMapSize(MapInfo map)
        {
            if (map == null) return Size.Empty;

            string fileName = map.FileName;
            if (string.IsNullOrWhiteSpace(fileName)) return GetFallbackMapSize(map);

            try
            {
                string path = Path.Combine(Config.MapPath ?? string.Empty, fileName + ".map");
                if (!File.Exists(path)) return GetFallbackMapSize(map);

                using (FileStream stream = File.OpenRead(path))
                using (BinaryReader reader = new BinaryReader(stream))
                {
                    if (stream.Length < 26) return GetFallbackMapSize(map);

                    stream.Seek(22, SeekOrigin.Begin);
                    int width = reader.ReadInt16();
                    int height = reader.ReadInt16();

                    if (width <= 0 || height <= 0) return GetFallbackMapSize(map);

                    return new Size(width, height);
                }
            }
            catch
            {
                return GetFallbackMapSize(map);
            }
        }

        private Size GetFallbackMapSize(MapInfo map)
        {
            MapControl currentMap = GameScene.Game?.MapControl;
            if (currentMap != null &&
                map == currentMap.MapInfo &&
                currentMap.Width > 0 &&
                currentMap.Height > 0)
                return new Size(currentMap.Width, currentMap.Height);

            return Size.Empty;
        }

        #endregion

        public Rectangle Area;
        public DXImageControl Image;
        public DXControl Panel;

        public DXButton RecenterButton;

        public static float ScaleX, ScaleY;

        public Dictionary<object, DXControl> MapInfoObjects = new Dictionary<object, DXControl>();
        private readonly Dictionary<long, DXControl> LocatorObjectControls = new Dictionary<long, DXControl>();
        private readonly Dictionary<long, DXControl> LocatorNpcControls = new Dictionary<long, DXControl>();
        private readonly HashSet<MapInfo> InstanceMapCache = new HashSet<MapInfo>();
        private InstanceInfo CachedInstanceInfo;
        private int CachedInstanceMapCount = -1;

        public override void OnClientAreaChanged(Rectangle oValue, Rectangle nValue)
        {
            base.OnClientAreaChanged(oValue, nValue);

            if (Panel == null) return;

            Panel.Location = ClientArea.Location;
            Panel.Size = ClientArea.Size;
        }

        public override void OnIsVisibleChanged(bool oValue, bool nValue)
        {
            base.OnIsVisibleChanged(oValue, nValue);

            SelectedInfo = IsVisible ? GameScene.Game.MapControl.MapInfo : null;

            BringToFront();
        }

        public override void OnOpacityChanged(float oValue, float nValue)
        {
            base.OnOpacityChanged(oValue, nValue);

            foreach (DXControl control in Controls)
                control.Opacity = Opacity;

            foreach (DXControl control in MapInfoObjects.Values)
                control.Opacity = Opacity;

            if (Image != null)
            {
                Image.Opacity = Opacity;
                Image.ImageOpacity = Opacity;
            }
        }

        public override WindowType Type => WindowType.None;
        public override bool CustomSize => false;
        public override bool AutomaticVisibility => false;

        #endregion

        public BigMapDialog()
        {
            BackColour = Color.Black;
            DropShadow = true;
            HasFooter = true;

            AllowResize = false;

            Panel = new DXControl
            {
                Parent = this,
                Location = Area.Location,
                Size = Area.Size
            };

            Image = new DXImageControl
            {
                Parent = Panel,
                LibraryFile = LibraryFile.MiniMap,
                Movable = true,
                IgnoreMoveBounds = true,
                Clip = true
            };
            Image.MouseClick += Image_MouseClick;

            RecenterButton = new DXButton
            {
                ButtonType = ButtonType.Default,
                Label = { Text = "回到当前" },
                Parent = this,
                Size = new Size(80, DefaultHeight)
            };
            RecenterButton.MouseClick += RecenterButton_MouseClick;
        }

        private void RecenterButton_MouseClick(object sender, MouseEventArgs e)
        {
            GameScene.Game.BigMapBox.SelectedInfo = GameScene.Game.MapControl.MapInfo;
        }

        private void Image_MouseClick(object sender, MouseEventArgs e)
        {
            if (!TryGetMapPoint(e.Location, out Point target)) return;

            if ((e.Button & MouseButtons.Right) == MouseButtons.Right)
            {
                CEnvir.Enqueue(new C.TeleportRing { Location = target, Index = SelectedInfo.Index });
            }

            if ((e.Button & MouseButtons.Left) == MouseButtons.Left)
            {
                if (GameScene.Game.Observer || MapObject.User == null) return;

                if (SelectedInfo != GameScene.Game.MapControl.MapInfo)
                {
                    GameScene.Game.ReceiveChat("只能在当前地图上启动自动寻路。", MessageType.Hint);
                    return;
                }

                if (!GameScene.Game.MapControl.ValidCell(target))
                {
                    GameScene.Game.SetAutoPath(false, false);
                    GameScene.Game.ReceiveChat("目标位置不可到达。", MessageType.Hint);
                    return;
                }

                if (GameScene.Game.PathFinder == null || !GameScene.Game.PathFinder.Matches(GameScene.Game.MapControl))
                    GameScene.Game.PathFinder = new PathFinder(GameScene.Game.MapControl);

                List<Node> path = GameScene.Game.PathFinder.FindPath(MapObject.User.CurrentLocation, target);

                if (path == null || path.Count == 0)
                {
                    GameScene.Game.SetAutoPath(false, false);
                    GameScene.Game.ReceiveChat("没有路径可以前进。", MessageType.Hint);
                    return;
                }

                GameScene.Game.CurrentPath = path;
                GameScene.Game.CurrentPathIndex = 0;
                GameScene.Game.AutoPath = true;
            }
        }

        private bool TryGetMapPoint(Point imageLocation, out Point target)
        {
            target = Point.Empty;
            if (SelectedInfo == null || Image == null || ScaleX <= 0 || ScaleY <= 0) return false;
            if (!Image.DisplayArea.Contains(imageLocation)) return false;

            int x = (int)((imageLocation.X - Image.DisplayArea.X) / ScaleX);
            int y = (int)((imageLocation.Y - Image.DisplayArea.Y) / ScaleY);

            Size mapSize = GetMapSize(SelectedInfo);
            if (mapSize.Width <= 0 || mapSize.Height <= 0) return false;
            if (x < 0 || y < 0 || x >= mapSize.Width || y >= mapSize.Height) return false;

            target = new Point(x, y);
            return true;
        }

        #region Methods
        public override void Draw()
        {
            if (!IsVisible || Size.Width == 0 || Size.Height == 0) return;

            OnBeforeDraw();
            DrawControl();
            DrawWindow();
            OnBeforeChildrenDraw();
            DrawChildControls();
            DrawPath();
            TitleLabel.Draw();
            DrawBorder();
            OnAfterDraw();
        }

        private void DrawPath()
        {
            if (!GameScene.Game.AutoPath || GameScene.Game.CurrentPath == null || SelectedInfo != GameScene.Game.MapControl.MapInfo) return;

            List<LinePoint> points = new List<LinePoint>();

            for (int i = Math.Max(0, GameScene.Game.CurrentPathIndex - 1); i < GameScene.Game.CurrentPath.Count; i++)
            {
                Node node = GameScene.Game.CurrentPath[i];
                float x = Image.DisplayArea.X + (node.Location.X + 0.5F) * ScaleX;
                float y = Image.DisplayArea.Y + (node.Location.Y + 0.5F) * ScaleY;

                if (!Panel.DisplayArea.Contains((int)x, (int)y)) continue;

                points.Add(new LinePoint(x, y));
            }

            if (points.Count < 2) return;

            float oldWidth = RenderingPipelineManager.GetLineWidth();

            try
            {
                RenderingPipelineManager.SetLineWidth(2F);
                RenderingPipelineManager.DrawLine(points, Color.Red);
            }
            finally
            {
                RenderingPipelineManager.SetLineWidth(oldWidth);
            }
        }

        public void Update(NPCInfo ob)
        {
            if (SelectedInfo == null) return;

            Size size = GetMapSize(SelectedInfo);
            if (size.Width <= 0 || size.Height <= 0) return;

            bool onSelectedMap = ob.Region?.Map == SelectedInfo;
            if (MapInfoObjects.TryGetValue(ob, out DXControl control))
            {
                if (onSelectedMap && (CurrentQuest)control.Tag == ob.CurrentQuest) return;

                Remove(ob);
            }

            if (!onSelectedMap) return;

            control = GameScene.Game.GetNPCControl(ob);
            control.Parent = Image;
            control.Opacity = Opacity;
            RegisterMapInfoObject(ob, control);

            if (ob.Region.PointList == null)
                ob.Region.CreatePoints(size.Width);

            int minX = size.Width, maxX = 0, minY = size.Height, maxY = 0;

            foreach (Point point in ob.Region.PointList)
            {
                if (point.X < minX)
                    minX = point.X;
                if (point.X > maxX)
                    maxX = point.X;

                if (point.Y < minY)
                    minY = point.Y;
                if (point.Y > maxY)
                    maxY = point.Y;
            }

            int x = (minX + maxX) / 2;
            int y = (minY + maxY) / 2;

            control.Location = new Point((int)(ScaleX * x) - control.Size.Width / 2, (int)(ScaleY * y) - control.Size.Height / 2);
        }

        public void Update(MovementInfo ob)
        {
            if (ob.SourceRegion == null || ob.SourceRegion.Map != SelectedInfo ||
                ob.DestinationRegion?.Map == null || ob.Icon == MapIcon.None)
            {
                Remove(ob);
                return;
            }

            if (GameScene.Game.MapControl.InstanceInfo != null)
            {
                InstanceInfo instance = GameScene.Game.MapControl.InstanceInfo;

                if (!InstanceContainsMap(instance, ob.SourceRegion.Map) && ob.NeedInstance == null)
                {
                    Remove(ob);
                    return;
                }

                if (!InstanceContainsMap(instance, ob.DestinationRegion?.Map) && ob.NeedInstance == null)
                {
                    Remove(ob);
                    return;
                }
            }

            Size size = GetMapSize(SelectedInfo);
            if (size.Width <= 0 || size.Height <= 0) return;

            if (ob.SourceRegion.PointList == null)
                ob.SourceRegion.CreatePoints(size.Width);

            int minX = size.Width, maxX = 0, minY = size.Height, maxY = 0;

            foreach (Point point in ob.SourceRegion.PointList)
            {
                if (point.X < minX)
                    minX = point.X;
                if (point.X > maxX)
                    maxX = point.X;

                if (point.Y < minY)
                    minY = point.Y;
                if (point.Y > maxY)
                    maxY = point.Y;
            }

            int x = (minX + maxX) / 2;
            int y = (minY + maxY) / 2;

            DXImageControl control = null;

            if (MapInfoObjects.TryGetValue(ob, out DXControl existing))
            {
                control = existing as DXImageControl;

                if (control == null)
                {
                    existing.Dispose();
                    UnregisterMapInfoObject(ob);
                }
            }

            if (control == null)
            {
                control = new DXImageControl
                {
                    LibraryFile = LibraryFile.MiniMapIcon,
                    Parent = Image
                };
                control.OpacityChanged += (o, e) => control.ImageOpacity = control.Opacity;
                control.MouseClick += (o, e) =>
                {
                    if (!TryShowMap(ob.DestinationRegion.Map))
                    {
                        return;
                    }

                    SelectedInfo = ob.DestinationRegion.Map;
                };

                RegisterMapInfoObject(ob, control);
            }

            control.Opacity = Opacity;
            control.ImageOpacity = Opacity;
            control.Hint = ob.DestinationRegion.Map.PlayerDescription;
            GameScene.Game.UpdateMapIcon(control, ob.Icon);
            control.Location = new Point((int)(ScaleX * x) - control.Size.Width / 2, (int)(ScaleY * y) - control.Size.Height / 2);
        }

        public void Update(ClientObjectData ob)
        {
            if (SelectedInfo == null) return;

            Size mapSize = GetMapSize(SelectedInfo);
            if (mapSize.Width <= 0 || mapSize.Height <= 0) return;

            if (!MapInfoObjects.TryGetValue(ob, out DXControl existing))
            {
                if (ob.MapIndex != SelectedInfo.Index) return;
                if (ob.ItemInfo != null && ob.ItemInfo.Rarity == Rarity.Common) return;
                if (ob.MonsterInfo != null && (ob.Dead || ob.MonsterInfo.Image == MonsterImage.None)) return;

                DXControl created = CreateMapInfoObject();
                RegisterMapInfoObject(ob, created);
                existing = created;
            }
            else if (ob.MapIndex != SelectedInfo.Index || (ob.MonsterInfo != null && ob.Dead) || (ob.ItemInfo != null && ob.ItemInfo.Rarity == Rarity.Common))
            {
                Remove(ob);
                return;
            }

            DXControl control = existing as DXControl;
            if (control == null)
            {
                existing.Dispose();

                DXControl created = CreateMapInfoObject();
                RegisterMapInfoObject(ob, created);
                control = created;
            }

            Size size = new Size(3, 3);
            Color colour = Color.White;
            string name = ob.Name;
            bool isGroupMember = IsGroupMember(ob.ObjectID);
            if (control is DXMapInfoControl mapInfoControl)
                mapInfoControl.Hollow = false;

            control.Visible = true;

            if (ob.MonsterInfo != null)
            {
                name = $"{ob.MonsterInfo.MonsterName}";

                if (ob.MonsterInfo.AI < 0)
                {
                    colour = Color.Red;
                }
                else
                {
                    colour = Color.Red;

                    if (GameScene.Game.HasQuest(ob.MonsterInfo, GameScene.Game.MapControl.MapInfo))
                        colour = Color.Orange;
                }

                if (ob.MonsterInfo.Flag == MonsterFlag.CastleObjective || ob.MonsterInfo.Flag == MonsterFlag.CastleDefense)
                {
                    control.Visible = false;
                }

                if (ob.MonsterInfo.IsBoss)
                {
                    size = new Size(5, 5);

                    if (control.Controls.Count == 0) // This is disgusting but its cheap
                    {
                        new DXMapInfoControl
                        {
                            Parent = control,
                            Location = new Point(1, 1),
                            BackColour = colour,
                            Size = new Size(3, 3)
                        };
                    }
                    else
                        control.Controls[0].BackColour = colour;

                    colour = Color.White;

                }

                if (!string.IsNullOrEmpty(ob.PetOwner))
                {
                    name += $" ({ob.PetOwner})";

                    if (ob.PetOwner == GameScene.Game.User.Name)
                    {
                        colour = Color.Orange;
                        size = new Size(4, 4);
                    }
                    else
                    {
                        colour = Color.Red;
                    }
                }
            }
            else if (ob.ItemInfo != null)
            {
                colour = Color.DarkBlue;
            }
            else
            {
                if (MapObject.User.ObjectID == ob.ObjectID)
                {
                    size = new Size(5, 5);
                    control.Border = false;
                    control.BorderSize = 1f;
                    control.BorderColour = Color.Transparent;
                    colour = Color.Lime;
                    if (control is DXMapInfoControl playerControl)
                        playerControl.Hollow = true;

                    if (SelectedInfo == GameScene.Game.MapControl.MapInfo)
                    {
                        RecenterButton.Enabled = false;
                    }

                    var overlay = DXMapInfoControl.GetOverlay(control);

                    control.ProcessAction = () =>
                    {
                        if (overlay?.IsBorderAnimationActive == false)
                        {
                            bool isVisibleSecond = CEnvir.Now.Millisecond < 500;

                            control.Border = false;
                            control.BackColour = isVisibleSecond ? Color.Lime : Color.Transparent;
                            if (control is DXMapInfoControl playerControl)
                                playerControl.Hollow = true;
                        }
                        else
                        {
                            control.Border = false;
                            control.BackColour = Color.Lime;
                            if (control is DXMapInfoControl playerControl)
                                playerControl.Hollow = true;
                        }
                    };
                }
                else if (isGroupMember)
                {
                    colour = Color.Lime;
                    size = new Size(4, 4);
                }
                else if (GameScene.Game.Observer)
                {
                    control.Visible = false;
                }
                else if (GameScene.Game.Partner != null && GameScene.Game.Partner.ObjectID == ob.ObjectID)
                {
                    colour = Color.DeepPink;
                }
                else if (IsGuildMember(ob.ObjectID))
                {
                    colour = Color.DeepSkyBlue;
                }
            }

            control.Hint = name;
            control.BackColour = colour;
            control.Size = size;
            control.Location = new Point((int)(ScaleX * ob.Location.X) - size.Width / 2, (int)(ScaleY * ob.Location.Y) - size.Height / 2);
        }

        public void PlayLocatorAnim(long id)
        {
            if (LocatorObjectControls.TryGetValue(id, out DXControl objectControl))
            {
                DXMapInfoControl.GetOverlay(objectControl)?.PlayBorderAnimation(Color.Lime);
            }
            else if (LocatorNpcControls.TryGetValue(id, out DXControl npcControl))
            {
                DXMapInfoControl.GetOverlay(npcControl)?.PlayBorderAnimation(Color.Yellow);
            }
        }

        private DXControl CreateMapInfoObject()
        {
            return new DXMapInfoControl
            {
                Parent = Image,
                Opacity = Opacity,
            };
        }

        public void Remove(object ob)
        {
            DXControl control;

            if (!MapInfoObjects.TryGetValue(ob, out control)) return;

            control.Dispose();
            UnregisterMapInfoObject(ob);
        }

        private void RegisterMapInfoObject(object ob, DXControl control)
        {
            MapInfoObjects[ob] = control;

            if (ob is ClientObjectData data)
                LocatorObjectControls[data.ObjectID] = control;
            else if (ob is NPCInfo npc)
                LocatorNpcControls[npc.Index] = control;
        }

        private void UnregisterMapInfoObject(object ob)
        {
            MapInfoObjects.Remove(ob);

            if (ob is ClientObjectData data)
                LocatorObjectControls.Remove(data.ObjectID);
            else if (ob is NPCInfo npc)
                LocatorNpcControls.Remove(npc.Index);
        }

        private void ClearMapInfoObjects()
        {
            foreach (DXControl control in MapInfoObjects.Values)
                control.Dispose();

            MapInfoObjects.Clear();
            LocatorObjectControls.Clear();
            LocatorNpcControls.Clear();
        }

        private bool InstanceContainsMap(InstanceInfo instance, MapInfo map)
        {
            if (instance?.Maps == null || map == null) return false;

            if (CachedInstanceInfo != instance || CachedInstanceMapCount != instance.Maps.Count)
            {
                InstanceMapCache.Clear();
                CachedInstanceInfo = instance;
                CachedInstanceMapCount = instance.Maps.Count;

                for (int i = 0; i < instance.Maps.Count; i++)
                {
                    MapInfo instanceMap = instance.Maps[i].Map;
                    if (instanceMap != null)
                        InstanceMapCache.Add(instanceMap);
                }
            }

            return InstanceMapCache.Contains(map);
        }

        private void ClearInstanceMapCache()
        {
            CachedInstanceInfo = null;
            CachedInstanceMapCount = -1;
            InstanceMapCache.Clear();
        }

        private static bool IsGroupMember(uint objectID)
        {
            var members = GameScene.Game?.GroupBox?.Members;
            if (members == null) return false;

            for (int i = 0; i < members.Count; i++)
                if (members[i].ObjectID == objectID)
                    return true;

            return false;
        }

        private static bool IsGuildMember(uint objectID)
        {
            var members = GameScene.Game?.GuildBox?.GuildInfo?.Members;
            if (members == null) return false;

            for (int i = 0; i < members.Count; i++)
                if (members[i].ObjectID == objectID)
                    return true;

            return false;
        }

        #endregion

        #region IDisposable

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                _SelectedInfo = null;
                SelectedInfoChanged = null;

                Area = Rectangle.Empty;
                ScaleX = 0;
                ScaleY = 0;

                ClearMapInfoObjects();
                ClearInstanceMapCache();
                MapInfoObjects = null;

                if (Image != null)
                {
                    if (!Image.IsDisposed)
                        Image.Dispose();

                    Image = null;
                }

                if (Panel != null)
                {
                    if (!Panel.IsDisposed)
                        Panel.Dispose();

                    Panel = null;
                }

                if (RecenterButton != null)
                {
                    if (!RecenterButton.IsDisposed)
                        RecenterButton.Dispose();

                    RecenterButton = null;
                }
            }
        }

        #endregion
    }
}
