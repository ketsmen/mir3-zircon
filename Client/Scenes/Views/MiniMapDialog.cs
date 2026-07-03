using Client.Controls;
using Client.Envir;
using Client.Models;
using Client.UserModels;
using Library;
using Library.SystemModels;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace Client.Scenes.Views
{
    public sealed class MiniMapDialog : DXWindow
    {
        #region Properties

        public Rectangle Area;
        private DXImageControl Image;
        public DXControl Panel;

        public DXImageControl TimeOfDayImage;
        private int _timeOfDayHintMinute = -1;
        private string _timeOfDayHintTemplate = string.Empty;
        private string _timeOfDayHint = string.Empty;

        public Dictionary<object, DXControl> MapInfoObjects = new Dictionary<object, DXControl>();

        public static float ScaleX, ScaleY;

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
        public override void OnClientAreaChanged(Rectangle oValue, Rectangle nValue)
        {
            base.OnClientAreaChanged(oValue, nValue);

            Area = ClientArea;
            Area.Inflate(6, 6);

            if (Panel == null) return;

            Panel.Location = Area.Location;
            Panel.Size = Area.Size;

            if (Size.Height < HeaderSize)
            {
                Size = new Size(Size.Width, HeaderSize);
            }

            UpdateMapPosition();
        }

        public override WindowType Type => WindowType.MiniMapBox;
        public override bool CustomSize => true;
        public override bool AutomaticVisibility => true;

        #endregion

        public MiniMapDialog()
        {
            BackColour = Color.Black;
            HasFooter = false;

            AllowResize = true;
            CloseButton.Visible = false;

            Size = new Size(200, 200);

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
            };

            TimeOfDayImage = new DXImageControl
            {
                Parent = this,
                LibraryFile = LibraryFile.GameInter,
                Index = 0,
                HintPosition = HintPosition.Fluid
            };

            GameScene.Game.MapControl.MapInfoChanged += MapControl_MapInfoChanged;
            Image.Moving += Image_Moving;
        }

        #region Methods

        private void Image_Moving(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            ClipMap();
        }

        private int originalMiniMapHeight = 0;

        private void MapControl_MapInfoChanged(object sender, EventArgs e)
        {
            foreach (DXControl temp in MapInfoObjects.Values)
                temp.Dispose();

            MapInfoObjects.Clear();

            GameScene.Game.SetAutoPath(false, false);
            GameScene.Game.CurrentPath = null;
            GameScene.Game.CurrentPathIndex = 0;
            GameScene.Game.PathFinder = HasUsableMap() ? new PathFinder(GameScene.Game.MapControl) : null;

            if (GameScene.Game.MapControl.MapInfo == null)
            {
                TitleLabel.Text = string.Empty;
                Image.Index = 0;
                ScaleX = 1F;
                ScaleY = 1F;
                return;
            }

            TitleLabel.Text = GameScene.Game.MapControl.MapInfo.PlayerDescription;
            Image.Index = GameScene.Game.MapControl.MapInfo.MiniMap;

            if (Image.Index <= 0 && Size.Height >= originalMiniMapHeight)
            {
                originalMiniMapHeight = Size.Height;
                Size = new Size(Size.Width, 32);
                AllowResize = false;
            }
            else if (originalMiniMapHeight > 0)
            {
                Size = new Size(Size.Width, originalMiniMapHeight);
                AllowResize = true;
            }

            if (!HasUsableMap())
            {
                ScaleX = 1F;
                ScaleY = 1F;
                return;
            }

            ScaleX = Image.Size.Width / (float)GameScene.Game.MapControl.Width;
            ScaleY = Image.Size.Height / (float)GameScene.Game.MapControl.Height;

            foreach (NPCInfo ob in Globals.NPCInfoList.Binding)
                Update(ob);

            foreach (MovementInfo ob in Globals.MovementInfoList.Binding)
                Update(ob);

            foreach (ClientObjectData ob in GameScene.Game.DataDictionary.Values)
                Update(ob);
        }

        public void Update(NPCInfo ob)
        {
            if (!HasUsableMap()) return;

            bool onCurrentMap = ob.Region?.Map == GameScene.Game.MapControl.MapInfo;
            if (MapInfoObjects.TryGetValue(ob, out DXControl control))
            {
                if (onCurrentMap && (CurrentQuest)control.Tag == ob.CurrentQuest) return;

                control.Dispose();
                MapInfoObjects.Remove(ob);
            }

            if (!onCurrentMap) return;

            control = GameScene.Game.GetNPCControl(ob);
            control.Parent = Image;
            control.Opacity = Opacity;
            MapInfoObjects[ob] = control;

            if (ob.Region.PointList == null)
                ob.Region.CreatePoints(GameScene.Game.MapControl.Width);

            int minX = GameScene.Game.MapControl.Width, maxX = 0, minY = GameScene.Game.MapControl.Height, maxY = 0;

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
            if (!HasUsableMap()) return;

            if (ob.SourceRegion == null || ob.SourceRegion.Map != GameScene.Game.MapControl.MapInfo ||
                ob.DestinationRegion?.Map == null || ob.Icon == MapIcon.None)
            {
                Remove(ob);
                return;
            }

            if (GameScene.Game.MapControl.InstanceInfo != null)
            {
                if (!InstanceContainsMap(GameScene.Game.MapControl.InstanceInfo, ob.SourceRegion.Map) && ob.NeedInstance == null)
                {
                    Remove(ob);
                    return;
                }

                if (!InstanceContainsMap(GameScene.Game.MapControl.InstanceInfo, ob.DestinationRegion?.Map) && ob.NeedInstance == null)
                {
                    Remove(ob);
                    return;
                }
            }

            if (ob.SourceRegion.PointList == null)
                ob.SourceRegion.CreatePoints(GameScene.Game.MapControl.Width);

            int minX = GameScene.Game.MapControl.Width, maxX = 0, minY = GameScene.Game.MapControl.Height, maxY = 0;

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
                    MapInfoObjects.Remove(ob);
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
                MapInfoObjects[ob] = control;
            }

            control.Opacity = Opacity;
            control.ImageOpacity = Opacity;
            control.Hint = ob.DestinationRegion.Map.PlayerDescription;
            GameScene.Game.UpdateMapIcon(control, ob.Icon);

            //control.MouseClick += (o, e) => SelectedInfo = ob.DestinationRegion.Map;
            control.Location = new Point((int)(ScaleX * x) - control.Size.Width / 2, (int)(ScaleY * y) - control.Size.Height / 2);
        }

        public void Update(ClientObjectData ob, bool playLocatorAnim = false)
        {
            if (!HasUsableMap()) return;

            if (!MapInfoObjects.TryGetValue(ob, out DXControl control))
            {
                if (ob.MapIndex != GameScene.Game.MapControl.MapInfo.Index) return;
                if (ob.ItemInfo != null && ob.ItemInfo.Rarity == Rarity.Common) return;
                if (ob.MonsterInfo != null && (ob.Dead || ob.MonsterInfo.Image == MonsterImage.None)) return;

                DXMapInfoControl created = CreateMapInfoObject();
                MapInfoObjects[ob] = created;
                control = created;

            }
            else if (ob.MapIndex != GameScene.Game.MapControl.MapInfo.Index || (ob.MonsterInfo != null && ob.Dead) || (ob.ItemInfo != null && ob.ItemInfo.Rarity == Rarity.Common))
            {
                control.Dispose();
                MapInfoObjects.Remove(ob);
                return;
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

                    if (control.ProcessAction == null)
                    {
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

            if (MapObject.User.ObjectID != ob.ObjectID) return;

            Image.Location = new Point(-control.Location.X + Area.Width / 2, -control.Location.Y + Area.Height / 2);

            ClipMap();

            if (playLocatorAnim)
            {
                GameScene.Game.MiniMapBox.PlayLocatorAnim(ob.ObjectID);
            }
        }

        public void UpdateMapPosition()
        {
            if (!HasUsableMap()) return;
            if (MapObject.User == null) return;

            ClientObjectData data;

            if (!GameScene.Game.DataDictionary.TryGetValue(MapObject.User.ObjectID, out data)) return;

            DXControl control;
            if (!MapInfoObjects.TryGetValue(data, out control)) return;

            Image.Location = new Point(-control.Location.X + Area.Width / 2, -control.Location.Y + Area.Height / 2);

            ClipMap();
        }

        private void ClipMap()
        {
            if (Image == null || Panel == null) return;

            int x = Image.Location.X;

            if (x + Image.Size.Width < Panel.Size.Width)
                x = Panel.Size.Width - Image.Size.Width;

            if (x > 0)
                x = 0;

            int y = Image.Location.Y;

            if (y + Image.Size.Height < Panel.Size.Height)
                y = Panel.Size.Height - Image.Size.Height;

            if (y > 0)
                y = 0;

            if (Image.Size.Width < Panel.Size.Width)
            {
                x = -((Image.Size.Width - Panel.Size.Width) / 2);
            }

            if (Image.Size.Height < Panel.Size.Height)
            {
                y = -((Image.Size.Height - Panel.Size.Height) / 2);
            }

            Image.Location = new Point(x, y);
        }

        public void PlayLocatorAnim(uint objectID)
        {
            DXControl control = null;

            foreach (KeyValuePair<object, DXControl> pair in MapInfoObjects)
            {
                ClientObjectData info = pair.Key as ClientObjectData;
                if (info == null || info.ObjectID != objectID) continue;

                control = pair.Value;
                break;
            }

            if (control == null) return;

            DXMapInfoControl.GetOverlay(control)?.PlayBorderAnimation(Color.Lime);
        }

        private DXMapInfoControl CreateMapInfoObject()
        {
            return new DXMapInfoControl
            {
                Parent = Image,
                Opacity = Opacity,
            };
        }

        private static bool HasUsableMap()
        {
            MapControl map = GameScene.Game?.MapControl;
            return map?.MapInfo != null &&
                   map.Cells != null &&
                   map.Width > 0 &&
                   map.Height > 0 &&
                   map.Cells.GetLength(0) >= map.Width &&
                   map.Cells.GetLength(1) >= map.Height;
        }

        private static bool InstanceContainsMap(InstanceInfo instance, MapInfo map)
        {
            if (instance?.Maps == null || map == null) return false;

            for (int i = 0; i < instance.Maps.Count; i++)
                if (instance.Maps[i].Map == map)
                    return true;

            return false;
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

        public void Remove(object ob)
        {
            DXControl control;

            if (!MapInfoObjects.TryGetValue(ob, out control)) return;

            control.Dispose();
            MapInfoObjects.Remove(ob);
        }

        public override void Draw()
        {
            if (!IsVisible || Size.Width == 0 || Size.Height == 0) return;

            OnBeforeDraw();
            DrawControl();
            OnBeforeChildrenDraw();
            DrawChildControls();
            DrawWindow();
            TitleLabel.Draw();
            DrawTimeOfDay();
            DrawBorder();
            OnAfterDraw();
        }

        private void DrawTimeOfDay()
        {
            int index = GetTimeOfDayImageIndex(GameScene.Game.TimeOfDay);

            if (TimeOfDayImage.Index != index)
                TimeOfDayImage.Index = index;

            Point location = new Point(3, Size.Height - 29);
            if (TimeOfDayImage.Location != location)
                TimeOfDayImage.Location = location;

            TimeOfDayImage.Hint = GetTimeOfDayHint();
            TimeOfDayImage.Draw();
        }

        private static int GetTimeOfDayImageIndex(TimeOfDay timeOfDay)
        {
            switch (timeOfDay)
            {
                case TimeOfDay.Dawn:
                    return 215;
                case TimeOfDay.Day:
                    return 216;
                case TimeOfDay.Dusk:
                    return 217;
                case TimeOfDay.Night:
                    return 218;
                default:
                    return 0;
            }
        }

        private string GetTimeOfDayHint()
        {
            DateTime now = CEnvir.Now;
            int minute = now.Hour * 60 + now.Minute;
            string template = GameScene.Game.TimeOfDayLabel ?? string.Empty;

            if (_timeOfDayHintMinute == minute && _timeOfDayHintTemplate == template)
                return _timeOfDayHint;

            _timeOfDayHintMinute = minute;
            _timeOfDayHintTemplate = template;
            _timeOfDayHint = template.Replace("##GAME_TIME##", now.ToShortTimeString());

            return _timeOfDayHint;
        }

        #endregion

        #region IDisposable

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                Area = Rectangle.Empty;
                ScaleX = 0;
                ScaleY = 0;

                if (GameScene.Game?.MapControl != null)
                    GameScene.Game.MapControl.MapInfoChanged -= MapControl_MapInfoChanged;

                foreach (KeyValuePair<object, DXControl> pair in MapInfoObjects)
                {
                    if (pair.Value == null) continue;
                    if (pair.Value.IsDisposed) continue;

                    pair.Value.Dispose();
                }

                MapInfoObjects.Clear();
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
            }
        }

        #endregion
    }
}
