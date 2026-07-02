using Client.Controls;
using Client.Envir;
using Client.Models;
using Client.Models.Particles;
using Library;
using Library.SystemModels;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using C = Library.Network.ClientPackets;

//Cleaned
namespace Client.Scenes.Views
{
    public sealed class MapControl : DXControl
    {
        private const int MaxMapCells = 4 * 1024 * 1024;

        #region Properties

        public static UserObject User => GameScene.Game.User;

        #region MapInformation

        public MapInfo MapInfo
        {
            get => _MapInfo;
            set
            {
                if (_MapInfo == value) return;

                MapInfo oldValue = _MapInfo;
                _MapInfo = value;

                OnMapInfoChanged(oldValue, value);
            }
        }
        private MapInfo _MapInfo;

        public InstanceInfo InstanceInfo
        {
            get => _InstanceInfo;
            set
            {
                if (_InstanceInfo == value) return;

                InstanceInfo oldValue = _InstanceInfo;
                _InstanceInfo = value;
                OnInstanceInfoChanged(oldValue, value);
            }
        }
        private InstanceInfo _InstanceInfo;
        public void OnInstanceInfoChanged(InstanceInfo oValue, InstanceInfo nValue)
        {
            MapInfoChanged?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler<EventArgs> MapInfoChanged;
        public void OnMapInfoChanged(MapInfo oValue, MapInfo nValue)
        {
            TextureValid = false;
            LoadMap();

            if (oValue != null)
            {
                if (nValue == null || nValue.Music != oValue.Music)
                    DXSoundManager.Stop(oValue.Music);
            }

            if (nValue != null)
                DXSoundManager.Play(nValue.Music);

            UpdateWeather();
            LLayer.UpdateLights();
            MapInfoChanged?.Invoke(this, EventArgs.Empty);
        }

        #endregion

        #region Animation

        public int Animation
        {
            get => _Animation;
            set
            {
                if (_Animation == value) return;

                int oldValue = _Animation;
                _Animation = value;

                OnAnimationChanged(oldValue, value);
            }
        }
        private int _Animation;
        public event EventHandler<EventArgs> AnimationChanged;
        public void OnAnimationChanged(int oValue, int nValue)
        {
            AnimationChanged?.Invoke(this, EventArgs.Empty);
            TextureValid = false;
        }



        #endregion


        #region MouseLocation

        public Point MouseLocation
        {
            get => _MouseLocation;
            set
            {
                if (_MouseLocation == value) return;

                Point oldValue = _MouseLocation;
                _MouseLocation = value;

                OnMouseLocationChanged(oldValue, value);
            }
        }
        private Point _MouseLocation;
        public event EventHandler<EventArgs> MouseLocationChanged;
        public void OnMouseLocationChanged(Point oValue, Point nValue)
        {
            MouseLocationChanged?.Invoke(this, EventArgs.Empty);
            UpdateMapLocation();
        }

        #endregion

        public override void OnSizeChanged(Size oValue, Size nValue)
        {
            base.OnSizeChanged(oValue, nValue);

            if (FLayer != null)
                FLayer.Size = Size;

            if (LLayer != null)
                LLayer.Size = Size;


            OffSetX = Size.Width / 2 / CellWidth;
            OffSetY = Size.Height / 2 / CellHeight;
            PixelOffsetX = (Size.Width - CellWidth) / 2 - OffSetX * CellWidth;
            PixelOffsetY = (Size.Height - CellHeight) / 2 - OffSetY * CellHeight - ManualHeightOffset;
        }

        public MouseButtons MapButtons;
        public Point MapLocation;

        public bool Mining;
        public Point MiningPoint;
        public MirDirection MiningDirection;

        public FishingState FishingState;
        public Point FloatLocation;
        public MirDirection FishingDirection;
        public bool AutoCast;

        public Floor FLayer;
        public Light LLayer;

        public Cell[,] Cells;
        public int Width, Height;

        public List<DXControl> MapInfoObjects = new List<DXControl>();
        public List<MapObject> Objects = new List<MapObject>();
        private readonly Dictionary<uint, MapObject> ObjectLookup = new Dictionary<uint, MapObject>();
        public List<MirEffect> Effects = new List<MirEffect>();
        public List<Models.Particles.ParticleEmitter> ParticleEffects = new List<Models.Particles.ParticleEmitter>();
        private List<MapObject>[] DrawRows = Array.Empty<List<MapObject>>();
        private int DrawRowCount;
        private List<MirEffect>[] ObjectEffectRows = Array.Empty<List<MirEffect>>();
        private int ObjectEffectRowCount;
        private readonly List<MirEffect> FloorEffects = new List<MirEffect>();
        private readonly List<MirEffect> FinalEffects = new List<MirEffect>();
        private readonly List<MirEffect> UserObjectEffects = new List<MirEffect>();
        private readonly List<MagicEffect> UserMagicEffectBuffer = new List<MagicEffect>();
        private readonly List<MapObject> OverlayObjects = new List<MapObject>();

        public const int CellWidth = 48, CellHeight = 32;
        public const int ManualHeightOffset = 34;

        public int ViewRangeX = 12, ViewRangeY = 24;

        public static int OffSetX;
        public static int OffSetY;
        public static int PixelOffsetX;
        public static int PixelOffsetY;


        #endregion

        public MapControl()
        {
            DrawTexture = true;

            BackColour = Color.Empty;

            FLayer = new Floor { Parent = this, Size = Size };
            LLayer = new Light { Parent = this, Location = new Point(-GameScene.Game.Location.X, -GameScene.Game.Location.Y), Size = Size };
        }

        #region Methods

        protected override void OnClearTexture()
        {
            base.OnClearTexture();

            if (!Visible) return;

            DrawBackground();

            if (FLayer.TextureValid)
            {
                RenderTexture floorTexture = FLayer.ControlTexture;
                Rectangle floorSource = new Rectangle(0, 0, FLayer.TextureSize.Width, FLayer.TextureSize.Height);
                RectangleF floorDestination = new RectangleF(0F, 0F, FLayer.TextureSize.Width, FLayer.TextureSize.Height);
                RenderingPipelineManager.DrawTexture(floorTexture, floorSource, floorDestination, Color.White);
            }

            bool hasDrawBounds = PrepareFrameDrawLists(out int minX, out int maxX, out int minY, out int maxY);

            if (Config.DrawEffects)
            {
                for (int i = 0; i < FloorEffects.Count; i++)
                    FloorEffects[i].Draw();
            }

            DrawObjects(hasDrawBounds, minX, maxX, minY, maxY);

            if (MapObject.MouseObject != null) // && MapObject.MouseObject != MapObject.TargetObject)
                MapObject.MouseObject.DrawBlend();

            if (Config.DrawEffects)
            {
                for (int i = 0; i < FinalEffects.Count; i++)
                    FinalEffects[i].Draw();
            }

            RenderingPipelineManager.FlushSprite();

            bool previousBlendEnabled = RenderingPipelineManager.IsBlending();
            float previousBlendRate = RenderingPipelineManager.GetBlendRate();
            BlendMode previousBlendMode = RenderingPipelineManager.GetBlendMode();

            RenderingPipelineManager.SetBlend(true, 1F, BlendMode.LIGHTMAP);

            if (LLayer.TextureValid)
            {
                RenderTexture lightLayerTexture = LLayer.ControlTexture;
                Rectangle lightLayerSource = new Rectangle(0, 0, LLayer.TextureSize.Width, LLayer.TextureSize.Height);
                RectangleF lightLayerDestination = new RectangleF(0F, 0F, LLayer.TextureSize.Width, LLayer.TextureSize.Height);
                RenderingPipelineManager.DrawTexture(lightLayerTexture, lightLayerSource, lightLayerDestination, Color.White);
            }

            RenderingPipelineManager.SetBlend(previousBlendEnabled, previousBlendRate, previousBlendMode);

            for (int i = 0; i < OverlayObjects.Count; i++)
            {
                MapObject ob = OverlayObjects[i];
                if (ob.Dead) continue;

                if (ShouldDrawObjectName(ob))
                    ob.DrawName();
            }

            if (MapObject.MouseObject != null && MapObject.MouseObject.Race != ObjectType.Item)
                MapObject.MouseObject.DrawName();

            DateTime now = CEnvir.Now;

            for (int i = 0; i < OverlayObjects.Count; i++)
            {
                MapObject ob = OverlayObjects[i];

                if (ob.ChatLabel != null && !ob.ChatLabel.IsDisposed && now <= ob.ChatTime)
                    ob.DrawChat();

                if (!ob.Dead && ob.Poison != PoisonType.None)
                    ob.DrawPoison();

                if (ShouldDrawObjectHealth(ob))
                    ob.DrawHealth();
            }

            if (Config.ShowDamageNumbers)
                for (int i = 0; i < OverlayObjects.Count; i++)
                {
                    MapObject ob = OverlayObjects[i];

                    if (ob.DamageList.Count == 0) continue;

                    ob.DrawDamage();
                }

            if (IsInsideMap(MapLocation))
            {
                Cell cell = Cells[MapLocation.X, MapLocation.Y];
                int layer = 0;
                if (cell.Objects != null)
                    for (int i = cell.Objects.Count - 1; i >= 0; i--)
                    {
                        ItemObject ob = cell.Objects[i] as ItemObject;

                        ob?.DrawFocus(layer++);
                    }
            }
        }

        public override void Draw()
        {
            if (!IsVisible || Size.Width == 0 || Size.Height == 0) return;

            FLayer.CheckTexture();
            LLayer.CheckTexture();

            //CreateTexture();
            OnBeforeDraw();

            DrawControl();

            DrawBorder();

            OnAfterDraw();
        }

        private void DrawBackground()
        {
            if (MapInfo == null || MapInfo.Background <= 0) return;

            if (!CEnvir.LibraryList.TryGetValue(LibraryFile.Background, out MirLibrary library)) return;

            if (!library.TryGetTexture(MapInfo.Background, ImageType.Image, out _, out var texture, out var sourceRectangle)) return;

            PresentTexture(texture, sourceRectangle, Parent, DisplayArea, Color.White, this, 0, 0, 1F);
        }

        private void DrawObjects(bool hasDrawBounds, int minX, int maxX, int minY, int maxY)
        {
            if (!hasDrawBounds) return;

            List<MapObject>[] drawRows = DrawRows;
            List<MirEffect>[] objectEffectRows = Config.DrawEffects ? ObjectEffectRows : null;

            for (int y = minY; y <= maxY; y++)
            {
                int drawY = (y - User.CurrentLocation.Y + OffSetY + 1) * CellHeight + PixelOffsetY - User.MovingOffSet.Y - User.ShakeScreenOffset.Y;

                for (int x = minX; x <= maxX; x++)
                {
                    int drawX = (x - User.CurrentLocation.X + OffSetX) * CellWidth + PixelOffsetX - User.MovingOffSet.X - User.ShakeScreenOffset.X;

                    Cell cell = Cells[x, y];

                    EnsureCellLibraries(cell);

                    if (cell.MiddleLibrary != null)
                    {
                        int index = cell.MiddleImage - 1;

                        bool blend = false;
                        if (cell.MiddleAnimationFrame > 1 && cell.MiddleAnimationFrame < 255)
                        {
                            blend = cell.MiddleAnimationBlend;
                            int frameCount = cell.MiddleAnimationCount;
                            if (frameCount > 0)
                            {
                                index += Animation % frameCount;
                            }
                        }

                        Size s = cell.MiddleLibrary.GetSize(index);
                        DrawMapLayer(cell.MiddleLibrary, index, drawX, drawY - s.Height, blend);
                    }

                    if (cell.FrontLibrary != null)
                    {
                        int index = cell.FrontImage - 1;

                        bool blend = false;
                        if (cell.FrontAnimationFrame > 1 && cell.FrontAnimationFrame < 255)
                        {
                            blend = cell.FrontAnimationBlend;
                            int frameCount = cell.FrontAnimationCount;
                            if (frameCount > 0)
                            {
                                index += Animation % frameCount;
                            }
                        }

                        Size s = cell.FrontLibrary.GetSize(index);

                        bool cellSized = (s.Width == CellWidth && s.Height == CellHeight) ||
                                         (s.Width == CellWidth * 2 && s.Height == CellHeight * 2);

                        DrawMapLayer(cell.FrontLibrary, index, drawX, cellSized ? drawY - CellHeight : drawY - s.Height, blend);
                    }
                }

                List<MapObject> drawObjects = drawRows[y - minY];
                if (drawObjects != null)
                    for (int i = 0; i < drawObjects.Count; i++)
                        drawObjects[i].Draw();

                List<MirEffect> rowEffects = objectEffectRows?[y - minY];
                if (rowEffects != null)
                    for (int i = 0; i < rowEffects.Count; i++)
                        rowEffects[i].Draw();

            }

            if (User.Opacity == 1f)
            {
                float oldOpacity = MapObject.User.Opacity;
                MapObject.User.Opacity = 0.65F;

                MapObject.User.DrawPlayer(false, false);

                MapObject.User.Opacity = oldOpacity;
            }

            if (Config.DrawEffects)
            {
                if (Config.DrawParticles)
                {
                    for (int i = 0; i < ParticleEffects.Count; i++)
                        ParticleEffects[i].Draw();
                }

                for (int i = 0; i < UserObjectEffects.Count; i++)
                    UserObjectEffects[i].Draw();
            }
        }

        private bool PrepareFrameDrawLists(out int minX, out int maxX, out int minY, out int maxY)
        {
            OverlayObjects.Clear();

            if (!TryGetDrawBounds(out minX, out maxX, out minY, out maxY))
            {
                ClearEffectDrawLists();
                return false;
            }

            BuildDrawRows(minY, maxY);

            if (Config.DrawEffects)
                BuildEffectDrawLists(minY, maxY);
            else
                ClearEffectDrawLists();

            return true;
        }

        private bool TryGetDrawBounds(out int minX, out int maxX, out int minY, out int maxY)
        {
            minX = maxX = minY = maxY = 0;

            if (Cells == null || Width <= 0 || Height <= 0 || User == null || Objects == null) return false;

            int width = Math.Min(Width, Cells.GetLength(0));
            int height = Math.Min(Height, Cells.GetLength(1));
            if (width <= 0 || height <= 0) return false;

            minX = Math.Max(0, User.CurrentLocation.X - OffSetX - 4);
            maxX = Math.Min(width - 1, User.CurrentLocation.X + OffSetX + 4);
            minY = Math.Max(0, User.CurrentLocation.Y - OffSetY - 4);
            maxY = Math.Min(height - 1, User.CurrentLocation.Y + OffSetY + 25);

            return maxX >= minX && maxY >= minY;
        }

        private static void DrawMapLayer(MirLibrary library, int index, int x, int y, bool blend)
        {
            if (!blend)
                library.Draw(index, x, y, Color.White, false, 1F, ImageType.Image);
            else
                library.DrawBlend(index, x, y, Color.White, false, 0.5F, ImageType.Image);
        }

        private static void EnsureBackLibrary(Cell cell)
        {
            if (cell.BackLibraryLoaded) return;

            if (Libraries.KROrder.TryGetValue(cell.BackFile, out LibraryFile file))
                CEnvir.LibraryList.TryGetValue(file, out cell.BackLibrary);

            cell.BackLibraryLoaded = true;
        }

        private static void EnsureCellLibraries(Cell cell)
        {
            if (cell.LibrariesLoaded) return;

            if (Libraries.KROrder.TryGetValue(cell.MiddleFile, out LibraryFile file) && file != LibraryFile.Tilesc)
                CEnvir.LibraryList.TryGetValue(file, out cell.MiddleLibrary);

            if (Libraries.KROrder.TryGetValue(cell.FrontFile, out file) && file != LibraryFile.Tilesc)
                CEnvir.LibraryList.TryGetValue(file, out cell.FrontLibrary);

            cell.LibrariesLoaded = true;
        }

        private List<MapObject>[] BuildDrawRows(int minY, int maxY)
        {
            int rowCount = maxY - minY + 1;

            if (DrawRows.Length < rowCount)
                Array.Resize(ref DrawRows, rowCount);

            for (int i = 0; i < rowCount; i++)
                DrawRows[i]?.Clear();

            for (int i = rowCount; i < DrawRowCount && i < DrawRows.Length; i++)
                DrawRows[i]?.Clear();

            DrawRowCount = rowCount;

            for (int i = 0; i < Objects.Count; i++)
            {
                MapObject ob = Objects[i];
                int renderY = ob.RenderY;
                if (renderY < minY || renderY > maxY) continue;

                OverlayObjects.Add(ob);

                int index = renderY - minY;
                if (DrawRows[index] == null)
                    DrawRows[index] = new List<MapObject>();

                DrawRows[index].Add(ob);
            }

            return DrawRows;
        }

        private bool ShouldDrawObjectName(MapObject ob)
        {
            switch (ob.Race)
            {
                case ObjectType.Player:
                    return Config.ShowPlayerNames;
                case ObjectType.Item:
                    return Config.ShowItemNames && ob.CurrentLocation != MapLocation;
                case ObjectType.Monster:
                    return Config.ShowMonsterNames;
                default:
                    return true;
            }
        }

        private static bool ShouldDrawObjectHealth(MapObject ob)
        {
            return ob.Race == ObjectType.Player || ob.Race == ObjectType.Hero || ob.Race == ObjectType.Monster;
        }

        private void ClearEffectDrawLists()
        {
            FloorEffects.Clear();
            FinalEffects.Clear();
            UserObjectEffects.Clear();

            for (int i = 0; i < ObjectEffectRowCount && i < ObjectEffectRows.Length; i++)
                ObjectEffectRows[i]?.Clear();

            ObjectEffectRowCount = 0;
        }

        private void BuildEffectDrawLists(int minY, int maxY)
        {
            int rowCount = maxY - minY + 1;

            if (ObjectEffectRows.Length < rowCount)
                Array.Resize(ref ObjectEffectRows, rowCount);

            for (int i = 0; i < rowCount; i++)
                ObjectEffectRows[i]?.Clear();

            for (int i = rowCount; i < ObjectEffectRowCount && i < ObjectEffectRows.Length; i++)
                ObjectEffectRows[i]?.Clear();

            ObjectEffectRowCount = rowCount;
            FloorEffects.Clear();
            FinalEffects.Clear();
            UserObjectEffects.Clear();

            if (Effects == null) return;

            for (int i = 0; i < Effects.Count; i++)
            {
                MirEffect effect = Effects[i];

                switch (effect.DrawType)
                {
                    case DrawType.Floor:
                        FloorEffects.Add(effect);
                        continue;
                    case DrawType.Final:
                        FinalEffects.Add(effect);
                        continue;
                    case DrawType.Object:
                        break;
                    default:
                        continue;
                }

                int renderY;

                if (effect.MapTarget.IsEmpty && effect.Target != null)
                {
                    if (effect.Target == User)
                    {
                        UserObjectEffects.Add(effect);
                        continue;
                    }

                    renderY = effect.Target.RenderY;
                }
                else
                {
                    renderY = effect.MapTarget.Y;
                }

                if (renderY < minY || renderY > maxY) continue;

                int index = renderY - minY;
                if (ObjectEffectRows[index] == null)
                    ObjectEffectRows[index] = new List<MirEffect>();

                ObjectEffectRows[index].Add(effect);
            }
        }

        private void ClearObjectCells()
        {
            if (Objects == null) return;

            for (int i = 0; i < Objects.Count; i++)
                Objects[i].CurrentCell?.RemoveObject(Objects[i]);
        }

        private void LoadMap()
        {
            ClearObjectCells();

            Width = 0;
            Height = 0;
            Cells = new Cell[0, 0];

            try
            {
                if (MapInfo == null || string.IsNullOrWhiteSpace(MapInfo.FileName)) return;

                var path = Path.Combine(Config.MapPath, MapInfo.FileName + ".map");

                if (!File.Exists(path)) return;

                using (FileStream mStream = File.OpenRead(path))
                using (BinaryReader reader = new BinaryReader(mStream))
                {
                    if (mStream.Length < 26) return;

                    mStream.Seek(22, SeekOrigin.Begin);
                    int width = reader.ReadInt16();
                    int height = reader.ReadInt16();

                    if (width <= 0 || height <= 0) return;
                    if ((long)width * height > MaxMapCells) return;
                    if (!HasExpectedMapDataLength(mStream.Length, width, height)) return;

                    mStream.Seek(28, SeekOrigin.Begin);

                    Cell[,] cells = new Cell[width, height];
                    for (int x = 0; x < width; x++)
                        for (int y = 0; y < height; y++)
                            cells[x, y] = new Cell();

                    for (int x = 0; x < width / 2; x++)
                        for (int y = 0; y < height / 2; y++)
                        {
                            cells[(x * 2), (y * 2)].BackFile = reader.ReadByte();
                            cells[(x * 2), (y * 2)].BackImage = reader.ReadUInt16();
                        }

                    for (int x = 0; x < width; x++)
                        for (int y = 0; y < height; y++)
                        {
                            byte flag = reader.ReadByte();
                            byte value = reader.ReadByte();
                            cells[x, y].MiddleAnimationFrame = value == 255 ? 0 : value;
                            cells[x, y].MiddleAnimationFrame &= Cell.MiddleFrameMask | Cell.MiddleBlendBit;

                            value = reader.ReadByte();
                            cells[x, y].FrontAnimationFrame = value == 255 ? 0 : value;
                            cells[x, y].FrontAnimationFrame &= Cell.FrontFrameMask | Cell.FrontBlendBit;

                            cells[x, y].FrontFile = reader.ReadByte();
                            cells[x, y].MiddleFile = reader.ReadByte();

                            cells[x, y].MiddleImage = reader.ReadUInt16() + 1;
                            cells[x, y].FrontImage = reader.ReadUInt16() + 1;

                            mStream.Seek(3, SeekOrigin.Current);

                            cells[x, y].Light = (byte)(reader.ReadByte() & 0x0F) * 2;

                            mStream.Seek(1, SeekOrigin.Current);

                            cells[x, y].Flag = ((flag & 0x01) != 1) || ((flag & 0x02) != 2);
                        }

                    Width = width;
                    Height = height;
                    Cells = cells;
                }
            }
            catch (Exception ex)
            {
                CEnvir.SaveException(ex);
            }

            if (Objects != null)
                for (int i = 0; i < Objects.Count; i++)
                {
                    MapObject ob = Objects[i];

                    if (ob.CurrentLocation.X >= 0 && ob.CurrentLocation.Y >= 0 &&
                        ob.CurrentLocation.X < Width && ob.CurrentLocation.Y < Height)
                        Cells[ob.CurrentLocation.X, ob.CurrentLocation.Y].AddObject(ob);
                }
        }

        private static bool HasExpectedMapDataLength(long length, int width, int height)
        {
            long backgroundCells = (long)(width / 2) * (height / 2);
            long mapCells = (long)width * height;
            long expectedLength = 28L + backgroundCells * 3L + mapCells * 14L;

            return length >= expectedLength;
        }

        public void UpdateWeather()
        {
            ClearParticleEffects();

            if (MapInfo == null) return;

            if (!Config.DrawWeather || !Config.DrawParticles || !Config.DrawEffects) return;

            var point = new Point((Size.Width) / 2, (Size.Height) / 2);
            Weather weather = MapInfo.Weather;

            if ((weather & Weather.Rain) == Weather.Rain)
            {
                var emitter = (ParticleEmitter)Activator.CreateInstance(typeof(Rain), point);
                ParticleEffects.Add(emitter);
            }

            if ((weather & Weather.Snow) == Weather.Snow)
            {
                var emitter = (ParticleEmitter)Activator.CreateInstance(typeof(Snow), point);
                ParticleEffects.Add(emitter);
            }

            if ((weather & Weather.Fog) == Weather.Fog)
            {
                var emitter = (ParticleEmitter)Activator.CreateInstance(typeof(Fog), point);
                ParticleEffects.Add(emitter);
            }

            if ((weather & Weather.Lightning) == Weather.Lightning)
            {
                var emitter = (ParticleEmitter)Activator.CreateInstance(typeof(Lightning), point);
                ParticleEffects.Add(emitter);
            }
        }

        public override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            MouseLocation = e.Location;
        }
        public override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (GameScene.Game.Observer) return;

            MapButtons |= e.Button;

            if (e.Button == MouseButtons.Right)
            {
                if (Config.RightClickDeTarget && MapObject.TargetObject?.Race == ObjectType.Monster)
                    MapObject.TargetObject = null;
            }

            if (e.Button != MouseButtons.Left) return;

            DXItemCell cell = DXItemCell.SelectedCell;
            if (cell != null)
            {
                MapButtons &= ~e.Button;

                if (cell.GridType == GridType.Belt)
                {
                    cell.QuickInfo = null;
                    cell.QuickItem = null;
                    DXItemCell.SelectedCell = null;

                    ClientBeltLink link = GameScene.Game.BeltBox.Links[cell.Slot];
                    CEnvir.Enqueue(new C.BeltLinkChanged { Slot = link.Slot, LinkIndex = link.LinkInfoIndex, LinkItemIndex = link.LinkItemIndex }); //Update serve
                    return;
                }

                if (cell.GridType == GridType.AutoPotion)
                {
                    cell.QuickInfo = null;
                    cell.QuickItem = null;
                    DXItemCell.SelectedCell = null;

                    GameScene.Game.AutoPotionBox.Rows[cell.Slot].SendUpdate();
                    return;
                }

                if ((cell.Item.Flags & UserItemFlags.Locked) == UserItemFlags.Locked || (cell.GridType != GridType.Inventory && cell.GridType != GridType.CompanionInventory))
                {
                    DXItemCell.SelectedCell = null;
                    return;
                }

                DXItemAmountWindow window = new DXItemAmountWindow("Drop Item", cell.Item);

                window.ConfirmButton.MouseClick += (o, a) =>
                {
                    if (window.Amount <= 0) return;

                    CEnvir.Enqueue(new C.ItemDrop
                    {
                        Link = new CellLinkInfo { GridType = cell.GridType, Slot = cell.Slot, Count = window.Amount }
                    });

                    cell.Locked = true;
                };

                DXItemCell.SelectedCell = null;
                return;
            }

            if (GameScene.Game.CurrencyPickedUp != null)
            {
                MapButtons &= ~e.Button;

                int index = GameScene.Game.CurrencyPickedUp.Info.Index;

                DXItemAmountWindow window = new DXItemAmountWindow("Drop Item", new ClientUserItem(GameScene.Game.CurrencyPickedUp.Info.DropItem, GameScene.Game.CurrencyPickedUp.Amount));

                window.ConfirmButton.MouseClick += (o, a) =>
                {
                    if (window.Amount <= 0) return;

                    CEnvir.Enqueue(new C.CurrencyDrop
                    {
                        CurrencyIndex = index,
                        Amount = window.Amount
                    });
                };

                GameScene.Game.CurrencyPickedUp = null;
                return;
            }

            if (CanAttack(MapObject.MouseObject))
            {
                MapObject.TargetObject = MapObject.MouseObject;

                #region Shuriken
                if (!Functions.InRange(MapObject.TargetObject.CurrentLocation, User.CurrentLocation, Globals.MagicRange) && User.LibraryWeaponShape == Globals.ShurikenLibraryWeaponShape)
                {

                    GameScene.Game.OutputTime = CEnvir.Now.AddSeconds(1);
                    GameScene.Game.ReceiveChat(string.Format(CEnvir.Language.GameSceneThrowTooFar, "Shuriken"), MessageType.Hint);

                    Stop();

                    return;
                }

                if (User.Horse == HorseType.None && User.LibraryWeaponShape == Globals.ShurikenLibraryWeaponShape && MapObject.TargetObject != null &&
                    (MapObject.TargetObject.Race == ObjectType.Monster || MapObject.TargetObject.Race == ObjectType.Player || MapObject.TargetObject.Race == ObjectType.Hero))
                {

                    if (CEnvir.Now < User.AttackTime)
                    {
                        Stop();

                        return;
                    }


                    if (CEnvir.Now >= User.AttackTime)
                    {
                        MapObject.User.AttemptAction(
                            new ObjectAction
                            (
                                      MirAction.RangeAttack,
                                      Functions.DirectionFromPoint(MapObject.User.CurrentLocation, MapObject.TargetObject.CurrentLocation),
                                      MapObject.User.CurrentLocation,
                                      MapObject.TargetObject.ObjectID, //Ranged Attack Target ID;
                                      MagicType.Shuriken
                            ));

                        Stop();

                        return;
                    }


                }

                #endregion Shukiran

                if (MapObject.MouseObject.Race == ObjectType.Monster && ((MonsterObject)MapObject.MouseObject).MonsterInfo.AI >= 0) //Check if AI is guard
                {
                    MapObject.MagicObject = MapObject.TargetObject;
                    GameScene.Game.FocusObject = MapObject.MouseObject;
                }
                return;
            }

            MapObject.TargetObject = null;
            GameScene.Game.FocusObject = null;
            //GameScene.Game.OldTargetObjectID = 0;
        }

        private void Stop()
        {
            Functions.Move(Location, MapObject.TargetObject.Direction, 0);
            GameScene.Game.FocusObject = null;
            MapObject.TargetObject = null;
        }
        public override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            switch (e.Button)
            {
                case MouseButtons.Left:
                    if (GameScene.Game.Observer) return;

                    GameScene.Game.AutoRun = false;
                    if (MapObject.MouseObject == null) return;
                    NPCObject npc = MapObject.MouseObject as NPCObject;
                    if (npc != null)
                    {
                        if (CEnvir.Now <= GameScene.Game.NPCTime) return;

                        GameScene.Game.NPCTime = CEnvir.Now.AddSeconds(1);

                        CEnvir.Enqueue(new C.NPCCall { ObjectID = npc.ObjectID });
                    }
                    break;
                case MouseButtons.Right:
                    GameScene.Game.AutoRun = false;

                    if (User.CurrentAction == MirAction.Standing)
                        GameScene.Game.CanRun = false;

                    if (!CEnvir.Ctrl) return;

                    PlayerObject player = MapObject.MouseObject as PlayerObject;

                    if (player == null || player == MapObject.User) return;
                    if (CEnvir.Now <= GameScene.Game.InspectTime && player.ObjectID == GameScene.Game.InspectID) return;

                    GameScene.Game.InspectTime = CEnvir.Now.AddMilliseconds(2500);
                    GameScene.Game.InspectID = player.ObjectID;
                    CEnvir.Enqueue(new C.Inspect { Index = player.CharacterIndex });
                    break;
            }
        }

        public void CheckCursor()
        {
            if (Cells == null) return;

            int width = Math.Min(Width, Cells.GetLength(0));
            int height = Math.Min(Height, Cells.GetLength(1));
            if (width <= 0 || height <= 0) return;

            MapObject deadObject = null, itemObject = null;

            for (int d = 0; d < 4; d++)
            {
                for (int y = MapLocation.Y - d; y <= MapLocation.Y + d; y++)
                {
                    if (y < 0 || y >= height) continue;

                    for (int x = MapLocation.X - d; x <= MapLocation.X + d; x++)
                    {
                        if (x < 0 || x >= width) continue;
                        if (d > 0 && x > MapLocation.X - d && x < MapLocation.X + d && y > MapLocation.Y - d && y < MapLocation.Y + d) continue;

                        List<MapObject> list = Cells[x, y].Objects;
                        if (list == null) continue;

                        MapObject cellSelect = null;
                        for (int i = list.Count - 1; i >= 0; i--)
                        {
                            MapObject ob = list[i];

                            if (ob == MapObject.User || ob.Race == ObjectType.Spell || ((x != MapLocation.X || y != MapLocation.Y) && !ob.MouseOver(MouseLocation))) continue;

                            if (ob.Dead || (ob.Race == ObjectType.Monster && ((MonsterObject)ob).CompanionObject != null))
                            {
                                if (deadObject == null)
                                    deadObject = ob;
                                continue;
                            }
                            if (ob.Race == ObjectType.Item)
                            {
                                if (itemObject == null)
                                    itemObject = ob;
                                continue;
                            }
                            if (x == MapLocation.X && y == MapLocation.Y && !ob.MouseOver(MouseLocation))
                            {
                                if (cellSelect == null)
                                    cellSelect = ob;
                            }
                            else
                            {
                                MapObject.MouseObject = ob;
                                return;
                            }
                        }

                        if (cellSelect != null)
                        {
                            MapObject.MouseObject = cellSelect;
                            return;
                        }
                    }
                }
            }

            MapObject mouseOb = deadObject ?? itemObject;

            if (mouseOb == null)
            {
                if ((User.CurrentLocation.X == MapLocation.X && User.CurrentLocation.Y == MapLocation.Y) || User.MouseOver(MouseLocation))
                    mouseOb = User;
            }


            MapObject.MouseObject = mouseOb;
        }

        public void ProcessInput()
        {
            if (GameScene.Game.Observer) return;

            if (User.Dead || (User.Poison & PoisonType.Paralysis) == PoisonType.Paralysis || (User.Poison & PoisonType.Containment) == PoisonType.Containment || User.HasBuff(BuffType.DragonRepulse)) return; //Para or Frozen??

            if (User.MagicAction != null)
            {
                if (CEnvir.Now < MapObject.User.NextActionTime || MapObject.User.ActionQueue.Count != 0) return;

                MapObject.User.AttemptAction(User.MagicAction);
                User.MagicAction = null;
                Mining = false;
            }

            bool haselementalhurricane = MapObject.User.VisibleBuffs.ContainsKey(BuffType.ElementalHurricane);

            if (!haselementalhurricane && MapObject.TargetObject != null && !MapObject.TargetObject.Dead && ((MapObject.TargetObject.Race == ObjectType.Monster && string.IsNullOrEmpty(MapObject.TargetObject.PetOwner)) || CEnvir.Shift))
            {
                if (Functions.Distance(MapObject.TargetObject.CurrentLocation, MapObject.User.CurrentLocation) == 1 && CEnvir.Now > User.AttackTime && User.Horse == HorseType.None)
                {
                    MapObject.User.AttemptAction(new ObjectAction(
                        MirAction.Attack,
                        Functions.DirectionFromPoint(MapObject.User.CurrentLocation, MapObject.TargetObject.CurrentLocation),
                        MapObject.User.CurrentLocation,
                        0, //Ranged Attack Target ID
                        MagicType.None,
                        Element.None));
                    return;
                }
            }

            //If  auto run

            if (GameScene.Game.AutoRun && !haselementalhurricane)
            {
                if (!GameScene.Game.MoveFrame || (User.Poison & PoisonType.WraithGrip) == PoisonType.WraithGrip) return;
                Run(MouseDirection());
                return;
            }

            if (MouseControl == this)
            {
                switch (MapButtons)
                {
                    case MouseButtons.Left:
                    {
                        MirDirection direction = MouseDirection();
                        Mining = false;

                        if (GameScene.Game.AutoFight)
                            GameScene.Game.AutoFight = false;
                        else if (GameScene.Game.AutoPath)
                            GameScene.Game.AutoPath = false;

                        if (CEnvir.Shift && MapObject.TargetObject == null)
                        {
                            if (FishingState != FishingState.None) return;

                            if (CEnvir.Now > User.AttackTime && User.Horse == HorseType.None && !haselementalhurricane)
                                MapObject.User.AttemptAction(new ObjectAction(MirAction.Attack, direction, MapObject.User.CurrentLocation, 0, MagicType.None, Element.None));

                            return;
                        }

                        ClientUserItem weap = GameScene.Game.Equipment[(int)EquipmentSlot.Weapon];
                        ClientUserItem armour = GameScene.Game.Equipment[(int)EquipmentSlot.Armour];

                        if (CEnvir.Alt)
                        {
                            if (haselementalhurricane) return;
                            if (User.Horse != HorseType.None) return;
                            if (FishingState != FishingState.None) return;

                            if (weap?.Info.ItemEffect == ItemEffect.FishingRod && armour?.Info.ItemEffect == ItemEffect.FishingRobe)
                            {
                                FloatLocation = MapLocation;
                                FishingDirection = Functions.DirectionFromPoint(MapObject.User.CurrentLocation, FloatLocation);

                                var distance = Functions.Distance(MapObject.User.CurrentLocation, FloatLocation);

                                if (Functions.FishingZone(Globals.FishingInfoList, MapInfo, Width, Height, FloatLocation) != null && Functions.ValidFishingDistance(distance, User.Stats[Stat.ThrowDistance]))
                                {
                                    FishingState = FishingState.Cast;
                                    break;
                                }
                            }
                            else
                            {
                                MapObject.User.AttemptAction(new ObjectAction(MirAction.Harvest, direction, MapObject.User.CurrentLocation));
                                return;
                            }
                        }

                        if (FishingState != FishingState.None)
                        {
                            FishingState = FishingState.Cancel;
                            break;
                        }

                        if (MapLocation == MapObject.User.CurrentLocation)
                        {
                            if (CEnvir.Now <= GameScene.Game.PickUpTime) return;

                            CEnvir.Enqueue(new C.PickUp());
                            GameScene.Game.PickUpTime = CEnvir.Now.AddMilliseconds(250);

                            return;
                        }

                        if (MapObject.MouseObject != null && MapObject.MouseObject.Race != ObjectType.Item && !MapObject.MouseObject.Dead) break;

                        if (!haselementalhurricane && MapInfo != null && MapInfo.CanMine && weap != null && weap.Info.ItemEffect == ItemEffect.PickAxe)
                        {
                            MiningPoint = Functions.Move(User.CurrentLocation, direction);

                            if (IsInsideMap(MiningPoint) && Cells[MiningPoint.X, MiningPoint.Y].Flag)
                            {
                                Mining = true;
                                break;
                            }
                        }

                        if (!CanMove(direction, 1) || haselementalhurricane)
                        {
                            MirDirection best = MouseDirectionBest(direction, 1);

                            if (best == direction)
                            {
                                if (direction != User.Direction)
                                    MapObject.User.AttemptAction(new ObjectAction(MirAction.Standing, direction, MapObject.User.CurrentLocation));
                                return;
                            }

                            direction = best;
                        }

                        if (!haselementalhurricane && GameScene.Game.MoveFrame && (User.Poison & PoisonType.WraithGrip) != PoisonType.WraithGrip)
                            MapObject.User.AttemptAction(new ObjectAction(MirAction.Moving, direction, Functions.Move(MapObject.User.CurrentLocation, direction), 1, MagicType.None));
                        return;
                    }
                    case MouseButtons.Right:
                    {
                        MirDirection direction = MouseDirection();
                        Mining = false;

                        if (FishingState != FishingState.None)
                        {
                            FishingState = FishingState.Cancel;
                            break;
                        }

                        if (MapObject.MouseObject is PlayerObject && MapObject.MouseObject != MapObject.User && CEnvir.Ctrl) break;

                        if (!GameScene.Game.MoveFrame || (User.Poison & PoisonType.WraithGrip) == PoisonType.WraithGrip) break;

                        if (Functions.InRange(MapLocation, MapObject.User.CurrentLocation, 2) || haselementalhurricane)
                        {
                            if (direction != User.Direction)
                                MapObject.User.AttemptAction(new ObjectAction(MirAction.Standing, direction, MapObject.User.CurrentLocation));
                            return;
                        }

                        Run(direction);

                        return;
                    }
                }
            }

            if (Mining)
            {
                ClientUserItem weap = GameScene.Game.Equipment[(int)EquipmentSlot.Weapon];

                if (MapInfo != null && MapInfo.CanMine && weap != null && (weap.CurrentDurability > 0 || weap.Info.Durability == 0) && weap.Info.ItemEffect == ItemEffect.PickAxe &&
                    IsInsideMap(MiningPoint) && Cells[MiningPoint.X, MiningPoint.Y].Flag &&
                    Functions.Distance(MiningPoint, MapObject.User.CurrentLocation) == 1 && User.Horse == HorseType.None)
                {
                    if (CEnvir.Now > User.AttackTime)
                    {
                        MapObject.User.AttemptAction(new ObjectAction(
                            MirAction.Mining,
                            Functions.DirectionFromPoint(MapObject.User.CurrentLocation, MiningPoint),
                            MapObject.User.CurrentLocation,
                            false));
                    }
                }
                else
                {
                    Mining = false;
                }
            }

            if (GameScene.Game.AutoPath)
            {
                if (GameScene.Game.PathFinder == null || GameScene.Game.CurrentPath == null || GameScene.Game.CurrentPath.Count == 0)
                {
                    GameScene.Game.AutoPath = false;
                    return;
                }

                int pathIndex = GameScene.Game.CurrentPathIndex;

                for (int i = pathIndex; i < Math.Min(pathIndex + 3, GameScene.Game.CurrentPath.Count); i++)
                {
                    if (GameScene.Game.CurrentPath[i].Location != User.CurrentLocation) continue;

                    GameScene.Game.CurrentPathIndex = i + 1;
                    break;
                }

                if (GameScene.Game.CurrentPathIndex >= GameScene.Game.CurrentPath.Count)
                {
                    GameScene.Game.AutoPath = false;
                    return;
                }

                Node nextNode = GameScene.Game.CurrentPath[GameScene.Game.CurrentPathIndex];
                MirDirection dir = Functions.DirectionFromPoint(User.CurrentLocation, nextNode.Location);
                bool canMove = CanMove(dir, 1);

                if (!canMove)
                {
                    Point target = GameScene.Game.CurrentPath[GameScene.Game.CurrentPath.Count - 1].Location;
                    List<Node> path = GameScene.Game.PathFinder.FindPath(MapObject.User.CurrentLocation, target);

                    if (path == null || path.Count == 0)
                    {
                        GameScene.Game.AutoPath = false;
                        return;
                    }

                    GameScene.Game.CurrentPath = path;
                    GameScene.Game.CurrentPathIndex = 0;
                    return;
                }

                Point runLocation = Functions.Move(User.CurrentLocation, dir, 2);
                bool canRunOnPath = GameScene.Game.CurrentPathIndex + 1 < GameScene.Game.CurrentPath.Count &&
                                    GameScene.Game.CurrentPath[GameScene.Game.CurrentPathIndex + 1].Location == runLocation;

                if (GameScene.Game.CanRun && CEnvir.Now > User.NextRunTime && User.CurrentHP >= 10 && canRunOnPath)
                {
                    if (!GameScene.Game.MoveFrame || (User.Poison & PoisonType.WraithGrip) == PoisonType.WraithGrip) return;

                    Run(dir);
                    return;
                }

                if (!GameScene.Game.MoveFrame || (User.Poison & PoisonType.WraithGrip) == PoisonType.WraithGrip) return;

                MapObject.User.AttemptAction(new ObjectAction(MirAction.Moving, dir, Functions.Move(MapObject.User.CurrentLocation, dir), 1, MagicType.None));
                return;
            }

            if (FishingState != FishingState.None || AutoCast)
            {
                if (CEnvir.Now > User.AttackTime)
                {
                    if (FishingState == FishingState.Reel)
                    {
                        FishingState = FishingState.None;
                    }
                    else
                    {
                        if (AutoCast)
                        {
                            FishingState = FishingState.Cast;
                            AutoCast = false;
                        }

                        MapObject.User.AttemptAction(new ObjectAction(MirAction.Fishing, FishingDirection, MapObject.User.CurrentLocation, FishingState, FloatLocation, MapObject.User.FishFound));
                    }
                }
            }

            if (MapObject.TargetObject == null || MapObject.TargetObject.Dead) return;
            if ((MapObject.TargetObject.Race == ObjectType.Player || MapObject.TargetObject.Race == ObjectType.Hero || !string.IsNullOrEmpty(MapObject.TargetObject.PetOwner)) && !CEnvir.Shift) return;
            if (Functions.InRange(MapObject.TargetObject.CurrentLocation, MapObject.User.CurrentLocation, 1)) return;
            if (GameScene.Game.AutoFight && (User.Class == MirClass.Wizard || User.Class == MirClass.Taoist)) return;

            MirDirection targetDirection = Functions.DirectionFromPoint(MapObject.User.CurrentLocation, MapObject.TargetObject.CurrentLocation);

            if (!CanMove(targetDirection, 1) || haselementalhurricane)
            {
                MirDirection best = DirectionBest(targetDirection, 1, MapObject.TargetObject.CurrentLocation);

                if (best == targetDirection)
                {
                    if (targetDirection != User.Direction)
                        MapObject.User.AttemptAction(new ObjectAction(MirAction.Standing, targetDirection, MapObject.User.CurrentLocation));
                    return;
                }
                targetDirection = best;
            }

            if (!haselementalhurricane && GameScene.Game.MoveFrame && (User.Poison & PoisonType.WraithGrip) != PoisonType.WraithGrip)
                MapObject.User.AttemptAction(new ObjectAction(MirAction.Moving, targetDirection, Functions.Move(MapObject.User.CurrentLocation, targetDirection), 1, MagicType.None));
        }

        public void Run(MirDirection direction)
        {
            int steps = 1;

            if (GameScene.Game.CanRun && CEnvir.Now >= User.NextRunTime && User.BagWeight <= User.Stats[Stat.BagWeight] && User.WearWeight <= User.Stats[Stat.WearWeight])
            {
                steps++;
                if (User.Horse != HorseType.None)
                    steps++;
            }

            for (int i = 1; i <= steps; i++)
            {
                if (CanMove(direction, i)) continue;

                MirDirection best = MouseDirectionBest(direction, 1);

                if (best == direction)
                {
                    if (i == 1)
                    {
                        if (direction != User.Direction)
                            MapObject.User.AttemptAction(new ObjectAction(MirAction.Standing, direction, MapObject.User.CurrentLocation));
                        return;
                    }

                    steps = i - 1;
                }
                else
                {
                    steps = 1;
                }

                direction = best;
                break;
            }

            MapObject.User.AttemptAction(new ObjectAction(MirAction.Moving, direction, Functions.Move(MapObject.User.CurrentLocation, direction, steps), steps, MagicType.None));
        }

        public MirDirection MouseDirectionBest(MirDirection dir, int distance) //22.5 = 16
        {

            Point loc = Functions.Move(MapObject.User.CurrentLocation, dir, distance);

            if (IsInsideMap(loc) && !Cells[loc.X, loc.Y].Blocking()) return dir;

            Point localMouse = new Point(MouseLocation.X - GameScene.Game.Location.X, MouseLocation.Y - GameScene.Game.Location.Y);

            PointF c = new PointF(OffSetX * CellWidth + PixelOffsetX + CellWidth / 2F, OffSetY * CellHeight + PixelOffsetY + CellHeight / 2F);
            PointF a = new PointF(c.X, 0);
            PointF b = localMouse;
            float bc = (float)Functions.Distance(c, b);
            float ac = bc;
            b.Y -= c.Y;
            c.Y += bc;
            b.Y += bc;
            double ab = (float)Functions.Distance(b, a);
            double x = (ac * ac + bc * bc - ab * ab) / (2 * ac * bc);
            double angle = Math.Acos(x);

            angle *= 180 / Math.PI;

            if (localMouse.X < c.X) angle = 360 - angle;

            MirDirection best = (MirDirection)(angle / 45F);

            if (best == dir)
                best = Functions.ShiftDirection(dir, 1);

            MirDirection next = Functions.ShiftDirection(dir, -((int)best - (int)dir));

            if (CanMove(best, distance))
                return best;

            if (CanMove(next, distance))
                return next;

            return dir;
        }
        public MirDirection DirectionBest(MirDirection dir, int distance, Point targetLocation) //22.5 = 16
        {
            Point loc = Functions.Move(MapObject.User.CurrentLocation, dir, distance);

            if (IsInsideMap(loc) && !Cells[loc.X, loc.Y].Blocking()) return dir;


            PointF c = new PointF(MapObject.OffSetX * MapObject.CellWidth + PixelOffsetX + MapObject.CellWidth / 2F, MapObject.OffSetY * MapObject.CellHeight + PixelOffsetY + MapObject.CellHeight / 2F);
            PointF a = new PointF(c.X, 0);
            PointF b = new PointF((targetLocation.X - MapObject.User.CurrentLocation.X + MapObject.OffSetX) * MapObject.CellWidth + PixelOffsetX + MapObject.CellWidth / 2F,
                (targetLocation.Y - MapObject.User.CurrentLocation.Y + MapObject.OffSetY) * MapObject.CellHeight + PixelOffsetY + MapObject.CellHeight / 2F);
            float bc = (float)Functions.Distance(c, b);
            float ac = bc;
            b.Y -= c.Y;
            c.Y += bc;
            b.Y += bc;
            double ab = (float)Functions.Distance(b, a);
            double x = (ac * ac + bc * bc - ab * ab) / (2 * ac * bc);
            double angle = Math.Acos(x);

            angle *= 180 / Math.PI;

            if (b.X < c.X) angle = 360 - angle;

            MirDirection best = (MirDirection)(angle / 45F);

            if (best == dir)
                best = Functions.ShiftDirection(dir, 1);

            MirDirection next = Functions.ShiftDirection(dir, -((int)best - (int)dir));

            if (CanMove(best, distance))
                return best;

            return CanMove(next, distance) ? next : dir;
        }

        public bool CanMove(MirDirection dir, int distance)
        {
            if (Cells == null) return false;

            for (int i = 1; i <= distance; i++)
            {
                Point loc = Functions.Move(User.CurrentLocation, dir, i);

                if (!IsInsideMap(loc)) return false;

                if (Cells[loc.X, loc.Y].Blocking())
                    return false;
            }
            return true;
        }

        public MirDirection MouseDirection() //22.5 = 16
        {
            Point localMouse = new Point(MouseLocation.X - GameScene.Game.Location.X, MouseLocation.Y - GameScene.Game.Location.Y);
            PointF p = new PointF((localMouse.X - PixelOffsetX) / (float)CellWidth, (localMouse.Y - PixelOffsetY) / (float)CellHeight);
            Point point = Point.Truncate(p);

            //If close proximity then co by co ords 
            if (Functions.InRange(new Point(OffSetX, OffSetY), point, 2))
                return Functions.DirectionFromPoint(new Point(OffSetX, OffSetY), point);

            PointF c = new PointF(OffSetX * CellWidth + PixelOffsetX + CellWidth / 2F, OffSetY * CellHeight + PixelOffsetY + CellHeight / 2F);
            PointF a = new PointF(c.X, 0);
            PointF b = new PointF(localMouse.X, localMouse.Y);
            float bc = (float)Functions.Distance(c, b);
            float ac = bc;
            b.Y -= c.Y;
            c.Y += bc;
            b.Y += bc;
            float ab = (float)Functions.Distance(b, a);
            double x = (ac * ac + bc * bc - ab * ab) / (2 * ac * bc);
            double angle = Math.Acos(x);

            angle *= 180 / Math.PI;

            if (localMouse.X < c.X) angle = 360 - angle;
            angle += 22.5F;
            if (angle > 360) angle -= 360;


            return (MirDirection)(angle / 45F);
        }

        public void AddObject(MapObject ob)
        {
            if (ob == null) return;

            if (ObjectLookup.TryGetValue(ob.ObjectID, out MapObject existing))
            {
                if (ReferenceEquals(existing, ob)) return;

                RemoveObject(existing);
            }

            Objects.Add(ob);
            ObjectLookup[ob.ObjectID] = ob;

            if (IsInsideMap(ob.CurrentLocation))
                Cells[ob.CurrentLocation.X, ob.CurrentLocation.Y].AddObject(ob);
        }

        public void RemoveObject(MapObject ob)
        {
            if (ob == null) return;

            Objects.Remove(ob);

            if (ObjectLookup.TryGetValue(ob.ObjectID, out MapObject existing) && ReferenceEquals(existing, ob))
                ObjectLookup.Remove(ob.ObjectID);

            if (ob.CurrentCell != null)
                ob.CurrentCell.RemoveObject(ob);
            else if (IsInsideMap(ob.CurrentLocation))
                Cells[ob.CurrentLocation.X, ob.CurrentLocation.Y].RemoveObject(ob);
        }

        public bool TryGetObject(uint objectID, out MapObject ob)
        {
            return ObjectLookup.TryGetValue(objectID, out ob);
        }

        public void ClearForMapChange()
        {
            MapButtons = MouseButtons.None;
            Mining = false;
            FishingState = FishingState.None;
            AutoCast = false;
            GameScene.Game.SetAutoPath(false, false);
            GameScene.Game.PathFinder = null;
            GameScene.Game.CurrentPath = null;
            GameScene.Game.CurrentPathIndex = 0;

            MapObject.MouseObject = null;
            MapObject.TargetObject = null;
            MapObject.MagicObject = null;
            GameScene.Game.FocusObject = null;

            if (GameScene.Game.NPCID != 0)
            {
                GameScene.Game.NPCID = 0;
                GameScene.Game.NPCBox.Visible = false;
            }

            if (GameScene.Game.MonsterBox.Monster != null)
                GameScene.Game.MonsterBox.Monster = null;

            for (int i = Objects.Count - 1; i >= 0; i--)
            {
                MapObject ob = Objects[i];
                if (ob == User) continue;

                ob.Remove();
            }

            if (User != null)
            {
                UserMagicEffectBuffer.Clear();

                foreach (MagicEffect effect in User.MagicEffects.Keys)
                    UserMagicEffectBuffer.Add(effect);

                for (int i = 0; i < UserMagicEffectBuffer.Count; i++)
                    User.EndMagicEffect(UserMagicEffectBuffer[i]);

                UserMagicEffectBuffer.Clear();

                User.Effects.Clear();
            }

            for (int i = Effects.Count - 1; i >= 0; i--)
                Effects[i].Remove();

            if (ParticleEffects != null)
                ClearParticleEffects();

            if (FLayer != null)
                FLayer.TextureValid = false;

            if (LLayer != null)
                LLayer.TextureValid = false;

            TextureValid = false;
        }

        public bool CanAttack(MapObject ob)
        {
            if (ob == null || ob == User) return false;
            if (ob is HeroObject hero && string.Equals(hero.OwnerName, User?.Name, StringComparison.OrdinalIgnoreCase)) return false;

            switch (ob.Race)
            {
                case ObjectType.Player:
                case ObjectType.Hero:
                    break;
                case ObjectType.Monster:
                    MonsterObject mob = (MonsterObject)ob;

                    if (mob.MonsterInfo.AI < 0) return false;

                    break;
                default:
                    return false;
            }

            return !ob.Dead;
        }

        public void UpdateMapLocation()
        {
            if (User == null) return;

            GameScene.Game.MapControl.MapLocation = new Point((GameScene.Game.MapControl.MouseLocation.X - GameScene.Game.Location.X - PixelOffsetX) / CellWidth - OffSetX + User.CurrentLocation.X,
                                                              (GameScene.Game.MapControl.MouseLocation.Y - GameScene.Game.Location.Y - PixelOffsetY) / CellHeight - OffSetY + User.CurrentLocation.Y);
        }

        public bool HasTarget(Point loc)
        {
            if (!IsInsideMap(loc)) return false;

            Cell cell = Cells[loc.X, loc.Y];

            if (cell.Objects == null) return false;

            foreach (MapObject ob in cell.Objects)
                if (ob.Blocking) return true;

            return false;
        }

        public bool CanEnergyBlast(MirDirection direction)
        {
            return HasTarget(Functions.Move(MapObject.User.CurrentLocation, direction, 2));
        }

        public bool CanHalfMoon(MirDirection direction)
        {
            if (HasTarget(Functions.Move(MapObject.User.CurrentLocation, Functions.ShiftDirection(direction, -1)))) return true;
            if (HasTarget(Functions.Move(MapObject.User.CurrentLocation, Functions.ShiftDirection(direction, 1)))) return true;
            if (HasTarget(Functions.Move(MapObject.User.CurrentLocation, Functions.ShiftDirection(direction, 2)))) return true;

            return false;
        }

        public bool CanDestructiveSurge(MirDirection direction)
        {
            for (int i = 1; i < 8; i++)
                if (HasTarget(Functions.Move(MapObject.User.CurrentLocation, Functions.ShiftDirection(direction, i)))) return true;

            return false;
        }

        public bool EmptyCell(Point location)
        {
            if (!IsInsideMap(location)) return false;

            Cell cell = Cells[location.X, location.Y];

            return (cell.BackImage & 0x20000000) == 0 && (cell.FrontImage & 0x8000) == 0 && !cell.Blocking();
        }

        public bool ValidCell(Point location)
        {
            if (!IsInsideMap(location)) return false;

            return !Cells[location.X, location.Y].Flag;
        }

        private bool IsInsideMap(Point location)
        {
            if (Cells == null || Width <= 0 || Height <= 0) return false;

            int width = Math.Min(Width, Cells.GetLength(0));
            int height = Math.Min(Height, Cells.GetLength(1));

            return location.X >= 0 && location.Y >= 0 &&
                   location.X < width && location.Y < height;
        }
        #endregion

        #region IDisposable

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                _MapInfo = null;
                MapInfoChanged = null;

                _InstanceInfo = null;

                _Animation = 0;
                AnimationChanged = null;

                MapButtons = 0;
                MapLocation = Point.Empty;
                Mining = false;
                MiningPoint = Point.Empty;
                MiningDirection = 0;

                FishingState = FishingState.None;
                FloatLocation = Point.Empty;
                FishingDirection = 0;
                AutoCast = false;

                if (FLayer != null)
                {
                    if (!FLayer.IsDisposed)
                        FLayer.Dispose();

                    FLayer = null;
                }

                if (LLayer != null)
                {
                    if (!LLayer.IsDisposed)
                        LLayer.Dispose();

                    LLayer = null;
                }

                Cells = null;

                Width = 0;
                Height = 0;

                MapInfoObjects.Clear();
                MapInfoObjects = null;

                Objects.Clear();
                ObjectLookup.Clear();
                Objects = null;

                Effects.Clear();
                Effects = null;
                ViewRangeX = 0;
                ViewRangeY = 0;
                OffSetX = 0;
                OffSetY = 0;

                ClearParticleEffects();
                ParticleEffects = null;
            }
        }

        public void ClearVisualEffects()
        {
            if (Effects == null) return;

            for (int i = Effects.Count - 1; i >= 0; i--)
                Effects[i].Remove();

            ClearEffectDrawLists();

            if (LLayer != null)
                LLayer.TextureValid = false;

            TextureValid = false;
        }

        public void ClearParticleEffects()
        {
            if (ParticleEffects == null) return;

            for (int i = ParticleEffects.Count - 1; i >= 0; i--)
                ParticleEffects[i].Dispose();

            ParticleEffects.Clear();
        }

        #endregion

        public sealed class Floor : DXControl
        {
            public Floor()
            {
                IsControl = false;
            }

            #region Methods
            public void CheckTexture()
            {
                if (!TextureValid)
                    CreateTexture();
            }

            protected override void OnClearTexture()
            {
                base.OnClearTexture();

                MapControl map = GameScene.Game?.MapControl;
                if (map?.Cells == null || map.Width <= 0 || map.Height <= 0 || MapObject.User == null) return;

                int width = Math.Min(map.Width, map.Cells.GetLength(0));
                int height = Math.Min(map.Height, map.Cells.GetLength(1));
                if (width <= 0 || height <= 0) return;

                int minX = Math.Max(0, User.CurrentLocation.X - OffSetX - 4), maxX = Math.Min(width - 1, User.CurrentLocation.X + OffSetX + 4);
                int minY = Math.Max(0, User.CurrentLocation.Y - OffSetY - 4), maxY = Math.Min(height - 1, User.CurrentLocation.Y + OffSetY + 4);

                for (int y = minY; y <= maxY; y++)
                {
                    if (y < 0) continue;
                    if (y >= height) break;

                    int drawY = (y - User.CurrentLocation.Y + OffSetY) * CellHeight + PixelOffsetY - User.MovingOffSet.Y - User.ShakeScreenOffset.Y;

                    for (int x = minX; x <= maxX; x++)
                    {
                        if (x < 0) continue;
                        if (x >= width) break;

                        int drawX = (x - User.CurrentLocation.X + OffSetX) * CellWidth + PixelOffsetX - User.MovingOffSet.X - User.ShakeScreenOffset.X;

                        Cell tile = map.Cells[x, y];

                        if (y % 2 == 0 && x % 2 == 0)
                        {
                            EnsureBackLibrary(tile);

                            MirLibrary library = tile.BackLibrary;

                            if (library == null) continue;

                            library.Draw(tile.BackImage, drawX, drawY, Color.White, false, 1F, ImageType.Image);
                        }
                    }
                }

                for (int y = minY; y <= maxY; y++)
                {
                    int drawY = (y - User.CurrentLocation.Y + OffSetY + 1) * CellHeight + PixelOffsetY - User.MovingOffSet.Y - User.ShakeScreenOffset.Y;

                    for (int x = minX; x <= maxX; x++)
                    {
                        int drawX = (x - User.CurrentLocation.X + OffSetX) * CellWidth + PixelOffsetX - User.MovingOffSet.X - User.ShakeScreenOffset.X;

                        Cell cell = map.Cells[x, y];

                        EnsureCellLibraries(cell);

                        MirLibrary library = cell.MiddleLibrary;
                        if (library != null)
                        {
                            int index = cell.MiddleImage - 1;

                            if (cell.MiddleAnimationFrame > 1 && cell.MiddleAnimationFrame < 255)
                                continue;//   index += GameScene.Game.MapControl.Animation % cell.MiddleAnimationFrame;

                            Size s = library.GetSize(index);

                            if ((s.Width == CellWidth && s.Height == CellHeight) || (s.Width == CellWidth * 2 && s.Height == CellHeight * 2))
                                library.Draw(index, drawX, drawY - CellHeight, Color.White, false, 1F, ImageType.Image);
                        }


                        library = cell.FrontLibrary;
                        if (library != null)
                        {
                            int index = cell.FrontImage - 1;

                            if (cell.FrontAnimationFrame > 1 && cell.FrontAnimationFrame < 255)
                                continue;//  index += GameScene.Game.MapControl.Animation % cell.FrontAnimationFrame;

                            Size s = library.GetSize(index);

                            if ((s.Width == CellWidth && s.Height == CellHeight) || (s.Width == CellWidth * 2 && s.Height == CellHeight * 2))
                                library.Draw(index, drawX, drawY - CellHeight, Color.White, false, 1F, ImageType.Image);
                        }
                    }
                }
            }

            public override void Draw()
            {
            }
            protected override void DrawControl()
            {
            }

            #endregion
        }

        public sealed class Light : DXControl
        {
            private const float LightScale = 0.02F;
            private const float BaseLightSize = 0.1F;
            private const float TileLightScaleMultiplier = 30F;
            private const float EffectLightScaleDivisor = 5F;
            private const int TileLightSearchPadding = 15;

            private int _lastLightSignature;

            public Light()
            {
                IsControl = false;
                BackColour = Color.FromArgb(15, 15, 15);
            }

            #region Methods

            public void CheckTexture()
            {
                UpdateAmbientLight();

                if (!ShouldRenderLightLayer())
                {
                    _lastLightSignature = 0;

                    if (TextureValid)
                        DisposeTexture();

                    return;
                }

                int signature = GetLightSignature();

                if (TextureValid && signature == _lastLightSignature)
                    return;

                CreateTexture();
                _lastLightSignature = signature;
            }

            protected override void OnClearTexture()
            {
                base.OnClearTexture();

                MapControl map = GameScene.Game?.MapControl;
                UserObject user = MapObject.User;

                if (map?.Cells == null || map.MapInfo == null || map.Width <= 0 || map.Height <= 0 || user == null) return;

                int width = Math.Min(map.Width, map.Cells.GetLength(0));
                int height = Math.Min(map.Height, map.Cells.GetLength(1));
                if (width <= 0 || height <= 0) return;

                if (user.Dead)
                {
                    RenderingPipelineManager.Clear(RenderClearFlags.Target, Color.IndianRed, 0, 0);
                    return;
                }

                bool previousBlendEnabled = RenderingPipelineManager.IsBlending();
                float previousBlendRate = RenderingPipelineManager.GetBlendRate();
                BlendMode previousBlendMode = RenderingPipelineManager.GetBlendMode();

                RenderingPipelineManager.SetBlend(true, 1F, BlendMode.COLORFY);

                RenderTexture lightTexture;
                Size lightSize;

                try
                {
                    lightTexture = RenderingPipelineManager.GetLightTexture();
                    lightSize = RenderingPipelineManager.GetLightTextureSize();
                }
                catch (InvalidOperationException)
                {
                    RenderingPipelineManager.SetBlend(previousBlendEnabled, previousBlendRate, previousBlendMode);
                    return;
                }

                Rectangle lightSource = new Rectangle(0, 0, lightSize.Width, lightSize.Height);

                if ((user.Poison & PoisonType.Abyss) == PoisonType.Abyss)
                {
                    RenderingPipelineManager.Clear(RenderClearFlags.Target, Color.Black, 0, 0);

                    float scale = BaseLightSize + 4 * LightScale;
                    float abyssX = OffSetX * CellWidth + PixelOffsetX + CellWidth / 2F - (lightSize.Width * scale) / 2F;
                    float abyssY = OffSetY * CellHeight + PixelOffsetY - (lightSize.Height * scale) / 2F;

                    DrawLight(lightTexture, lightSource, lightSize, abyssX, abyssY, scale, Color.White);

                    RenderingPipelineManager.SetBlend(previousBlendEnabled, previousBlendRate, previousBlendMode);

                    var abyssEffects = user.CreateMagicEffect(MagicEffect.Abyss);

                    foreach (var effect in abyssEffects)
                    {
                        effect.Draw();
                    }
                    return;
                }

                if (map.Objects != null)
                    for (int i = 0; i < map.Objects.Count; i++)
                    {
                        MapObject ob = map.Objects[i];
                        if (!ShouldDrawObjectLight(ob, user))
                            continue;

                        float scale = BaseLightSize + ob.Light * 2 * LightScale;
                        float objectX = (OffSetX + ob.CurrentLocation.X - user.CurrentLocation.X) * CellWidth + PixelOffsetX + ob.MovingOffSet.X - user.MovingOffSet.X + CellWidth / 2F - (lightSize.Width * scale) / 2F;
                        float objectY = (OffSetY + ob.CurrentLocation.Y - user.CurrentLocation.Y) * CellHeight + PixelOffsetY + ob.MovingOffSet.Y - user.MovingOffSet.Y - (lightSize.Height * scale) / 2F;

                        DrawLight(lightTexture, lightSource, lightSize, objectX, objectY, scale, ob.LightColour);
                    }

                if (map.Effects != null)
                    for (int i = 0; i < map.Effects.Count; i++)
                    {
                        MirEffect ob = map.Effects[i];
                        float frameLight = ob.FrameLight;

                        if (frameLight > 0)
                        {
                            float scale = BaseLightSize + frameLight * 2 * LightScale / EffectLightScaleDivisor;
                            float effectX = ob.DrawX + CellWidth / 2F - (lightSize.Width * scale) / 2F;
                            float effectY = ob.DrawY + CellHeight / 2F - (lightSize.Height * scale) / 2F;

                            DrawLight(lightTexture, lightSource, lightSize, effectX, effectY, scale, ob.FrameLightColour);
                        }
                    }

                int minX = Math.Max(0, user.CurrentLocation.X - OffSetX - TileLightSearchPadding);
                int maxX = Math.Min(width - 1, user.CurrentLocation.X + OffSetX + TileLightSearchPadding);
                int minY = Math.Max(0, user.CurrentLocation.Y - OffSetY - TileLightSearchPadding);
                int maxY = Math.Min(height - 1, user.CurrentLocation.Y + OffSetY + TileLightSearchPadding);

                for (int y = minY; y <= maxY; y++)
                {
                    if (y < 0) continue;
                    if (y >= height) break;

                    int drawY = (y - user.CurrentLocation.Y + OffSetY) * CellHeight + PixelOffsetY - user.MovingOffSet.Y - user.ShakeScreenOffset.Y;

                    for (int x = minX; x <= maxX; x++)
                    {
                        if (x < 0) continue;
                        if (x >= width) break;

                        int drawX = (x - user.CurrentLocation.X + OffSetX) * CellWidth + PixelOffsetX - user.MovingOffSet.X - user.ShakeScreenOffset.X;

                        Cell tile = map.Cells[x, y];

                        if (tile.Light == 0) continue;

                        float scale = BaseLightSize + tile.Light * TileLightScaleMultiplier * LightScale;
                        float tileX = drawX + CellWidth / 2F - (lightSize.Width * scale) / 2F;
                        float tileY = drawY + CellHeight / 2F - (lightSize.Height * scale) / 2F;

                        DrawLight(lightTexture, lightSource, lightSize, tileX, tileY, scale, Color.White);
                    }
                }

                RenderingPipelineManager.SetBlend(previousBlendEnabled, previousBlendRate, previousBlendMode);
            }

            private void DrawLight(RenderTexture lightTexture, Rectangle sourceRectangle, Size lightSize, float topLeftX, float topLeftY, float scale, Color colour)
            {
                float width = lightSize.Width * scale;
                float height = lightSize.Height * scale;

                if (width <= 0 || height <= 0)
                {
                    return;
                }

                RectangleF destination = new RectangleF(topLeftX, topLeftY, width, height);
                RenderingPipelineManager.DrawTexture(lightTexture, sourceRectangle, destination, colour);
            }

            private static bool ShouldDrawObjectLight(MapObject ob, UserObject user)
            {
                return ob.Light > 0 && (!ob.Dead || ob == user || ob.Race == ObjectType.Spell);
            }

            public void UpdateLights()
            {
                UpdateAmbientLight();
                TextureValid = false;
            }

            private void UpdateAmbientLight()
            {
                MapControl map = GameScene.Game?.MapControl;

                if (map?.MapInfo == null)
                {
                    Visible = false;
                    return;
                }

                switch (map.MapInfo.Light)
                {
                    case LightSetting.Default:
                        byte shading = (byte)(255 * GameScene.Game.DayTime);
                        BackColour = Color.FromArgb(shading, shading, shading);
                        Visible = true;
                        break;
                    case LightSetting.Night:
                        BackColour = Color.FromArgb(15, 15, 15);
                        Visible = true;
                        break;
                    case LightSetting.Twilight:
                        BackColour = Color.FromArgb(100, 100, 100);
                        Visible = true;
                        break;
                    case LightSetting.Light:
                        BackColour = Color.FromArgb(255, 255, 255);
                        Visible = true;
                        break;
                }

                if (MapObject.User != null && (MapObject.User.Poison & PoisonType.Abyss) == PoisonType.Abyss)
                {
                    BackColour = Color.FromArgb(15, 15, 15);
                    Visible = false;
                }
            }

            private static bool ShouldRenderLightLayer()
            {
                MapControl map = GameScene.Game?.MapControl;
                UserObject user = MapObject.User;

                if (map?.MapInfo == null)
                    return false;

                if (user != null && (user.Dead || (user.Poison & PoisonType.Abyss) == PoisonType.Abyss))
                    return true;

                Light lightLayer = map.LLayer;

                if (map.MapInfo.Light == LightSetting.Light)
                    return false;

                return lightLayer.BackColour.R < 255 || lightLayer.BackColour.G < 255 || lightLayer.BackColour.B < 255;
            }

            private static int GetLightSignature()
            {
                unchecked
                {
                    int hash = 17;
                    MapControl map = GameScene.Game?.MapControl;
                    UserObject user = MapObject.User;

                    if (map?.Cells == null || map.MapInfo == null || user == null)
                        return 0;

                    hash = hash * 31 + map.Size.Width;
                    hash = hash * 31 + map.Size.Height;
                    hash = hash * 31 + (int)map.MapInfo.Light;
                    hash = hash * 31 + map.LLayer.BackColour.ToArgb();
                    hash = hash * 31 + user.CurrentLocation.X;
                    hash = hash * 31 + user.CurrentLocation.Y;
                    hash = hash * 31 + user.MovingOffSet.X;
                    hash = hash * 31 + user.MovingOffSet.Y;
                    hash = hash * 31 + user.ShakeScreenOffset.X;
                    hash = hash * 31 + user.ShakeScreenOffset.Y;
                    hash = hash * 31 + user.Light;
                    hash = hash * 31 + user.LightColour.ToArgb();
                    hash = hash * 31 + (int)user.Poison;

                    if (map.Objects != null)
                        for (int i = 0; i < map.Objects.Count; i++)
                        {
                            MapObject ob = map.Objects[i];
                            if (!ShouldDrawObjectLight(ob, user))
                                continue;

                            hash = hash * 31 + ob.ObjectID.GetHashCode();
                            hash = hash * 31 + ob.CurrentLocation.X;
                            hash = hash * 31 + ob.CurrentLocation.Y;
                            hash = hash * 31 + ob.MovingOffSet.X;
                            hash = hash * 31 + ob.MovingOffSet.Y;
                            hash = hash * 31 + ob.Light;
                            hash = hash * 31 + ob.LightColour.ToArgb();
                            hash = hash * 31 + (ob.Dead ? 1 : 0);
                        }

                    if (map.Effects != null)
                        for (int i = 0; i < map.Effects.Count; i++)
                        {
                            MirEffect effect = map.Effects[i];
                            float frameLight = effect.FrameLight;
                            if (frameLight <= 0)
                                continue;

                            hash = hash * 31 + effect.DrawX;
                            hash = hash * 31 + effect.DrawY;
                            hash = hash * 31 + (int)(frameLight * 100);
                            hash = hash * 31 + effect.FrameLightColour.ToArgb();
                        }

                    return hash;
                }
            }
            protected override void DrawControl()
            {
            }

            public override void Draw()
            {
            }
            #endregion
        }
    }

    public sealed class Cell
    {
        public int BackFile;
        public int BackImage;

        public int MiddleFile;
        public int MiddleImage;

        public int FrontFile;
        public int FrontImage;

        public int FrontAnimationFrame { get; set; }
        public int FrontAnimationTick { get; set; }
        public int FrontAnimationCount => FrontAnimationFrame & FrontFrameMask;
        public bool FrontAnimationBlend => (FrontAnimationFrame & FrontBlendBit) != 0;

        public int MiddleAnimationFrame { get; set; }
        public int MiddleAnimationTick { get; set; }
        public int MiddleAnimationCount => MiddleAnimationFrame & MiddleFrameMask;
        public bool MiddleAnimationBlend => (MiddleAnimationFrame & MiddleBlendBit) != 0;

        public int Light;

        public bool Flag;

        // --- FRONT ANIMATION ENCODING ---
        public const int FrontFrameMask = 0x0F; // lower 4 bits = frame count
        public const int FrontBlendBit = 0x80;  // bit 7 = blend flag

        // --- MIDDLE ANIMATION ENCODING ---
        public const int MiddleFrameMask = 0x0F; // lower 4 bits = frame count
        public const int MiddleBlendBit = 0x80;  // bit 7 = blend flag

        public MirLibrary BackLibrary;
        public bool BackLibraryLoaded;

        public MirLibrary MiddleLibrary;
        public MirLibrary FrontLibrary;
        public bool LibrariesLoaded;

        public List<MapObject> Objects;

        public bool Blocking()
        {
            List<MapObject> objects = Objects;

            if (objects != null)
            {
                for (int i = 0; i < objects.Count; i++)
                    if (objects[i].Blocking) return true;
            }

            return Flag;
        }

        public void AddObject(MapObject ob)
        {
            if (Objects == null)
                Objects = new List<MapObject>();

            if (ob.Race == ObjectType.Spell)
                Objects.Insert(0, ob);
            else
                Objects.Add(ob);

            ob.CurrentCell = this;
        }

        public void RemoveObject(MapObject ob)
        {
            if (Objects == null)
            {
                ob.CurrentCell = null;
                return;
            }

            Objects.Remove(ob);

            if (Objects.Count == 0)
                Objects = null;

            ob.CurrentCell = null;
        }
    }

}
